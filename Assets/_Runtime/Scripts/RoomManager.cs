// RoomManager.cs (minimizado para seed + sincronização)
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// Gerencia apenas uma seed compartilhada e a sincroniza para todos os players.
/// - Se <see cref="randomizeSeed"/> for true, a seed é randomizada no servidor ao iniciar.
/// - Expõe evento estático quando a seed estiver pronta, para sockets consumirem.
/// </summary>
[AddComponentMenu("Level/Room Manager")]
public class RoomManager : NetworkBehaviour
{
    public static RoomManager Instance { get; private set; }

    [Header("Seed")] 
    [Tooltip("Seed global sincronizada entre todos os jogadores (definida pelo servidor).")]
    [SyncVar(hook = nameof(OnSeedChanged))]
    public int seed = 123456;

    [Tooltip("Se true, o servidor randomiza a seed ao iniciar a partida.")]
    public bool randomizeSeed = true;

    // Estado estático para fácil consulta pelos sockets
    public static bool HasSeed => _hasSeed;
    public static int CurrentSeed => _currentSeed;

    public static event Action<int> SeedReady;

    private static bool _hasSeed;
    private static int _currentSeed;
    
    [Header("Forced Rooms")]
    [Tooltip("Rooms that must be spawned regardless of randomness. Each entry sets a min/max count (1-10). Spawns deterministically per seed.")]
    public List<ForcedRoom> forcedRooms = new List<ForcedRoom>();

    [Tooltip("If true, applies forced room spawns automatically after the seed is set (on both server and client).")]
    public bool autoApplyForcedRooms = true;

    [Tooltip("Enable debug logs for forced room placement.")]
    public bool debugForcedRooms = false;

    [Header("Limits")]
    [Tooltip("Número máximo de salas instanciadas. 0 ou negativo = ilimitado.")]
    public int maxRooms = 0;  // NOVO

    private bool _forcedApplied = false;

    private static readonly HashSet<int> _spawnedIds = new HashSet<int>(); // NOVO
    public static int CurrentRooms => _spawnedIds.Count; // NOVO
    public static bool IsAtMaxRooms => Instance != null && Instance.maxRooms > 0 && _spawnedIds.Count >= Instance.maxRooms; // NOVO

    // Exposto para que os sockets saibam quando priorizar/aguardar forced rooms
    public static bool ForcedApplied => Instance != null && Instance._forcedApplied;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        // subscribe to seed ready to schedule forced rooms after seed propagation
        SeedReady += HandleSeedReadyInternal;
    }

    void OnDisable()
    {
        SeedReady -= HandleSeedReadyInternal;
    }

    public override void OnStartServer()
    {
        // Apenas o servidor decide a seed final
        if (randomizeSeed)
        {
            // Evita 0 para que mudanças fiquem mais evidentes, mas 0 também funcionaria
            seed = UnityEngine.Random.Range(int.MinValue + 1, int.MaxValue);
        }

        // Anuncia localmente no servidor (clientes receberão via SyncVar hook)
        AnnounceSeed(seed);
    }

    public override void OnStartClient()
    {
        // Em clientes, o valor virá sincronizado; ainda assim, se já tivermos seed, anuncia.
        if (!_hasSeed)
            AnnounceSeed(seed);
    }

    void Start()
    {
        // Modo offline/sem rede: garante anúncio com o valor do inspector
        if (!NetworkClient.active && !NetworkServer.active)
        {
            AnnounceSeed(seed);
        }
    }

    void OnSeedChanged(int oldValue, int newValue)
    {
        AnnounceSeed(newValue);
    }

    private static void AnnounceSeed(int value)
    {
        _currentSeed = value;
       _hasSeed = true;
        try { SeedReady?.Invoke(value); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ---- Spawn tracking & sealing ----
    public static void NotifyRoomSpawned(GameObject go)
    {
        if (go == null) return;
        if (_spawnedIds.Add(go.GetInstanceID()))
        {
            if (IsAtMaxRooms)
                Instance?.StartCoroutine(Instance.SealOpenSocketsDeferred());
        }
    }

    public static void NotifyRoomDespawned(GameObject go)
    {
        if (go == null) return;
        _spawnedIds.Remove(go.GetInstanceID());
    }

    private IEnumerator SealOpenSocketsDeferred()
    {
        // dá um frame para evitar corrida com spawns em andamento
        yield return null;
        SealOpenSocketsNow();
    }

    public void SealOpenSocketsNow()
    {
        var all = RoomSocket.All;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s == null) continue;
            if (s.IsOccupied) continue;
            if (!s.HasOptions) continue;

            RoomSocketOption chosen = null;
            var opts = s.options;
            for (int k = 0; k < opts.Count; k++)
            {
                var opt = opts[k];
                if (opt != null && opt.prefab != null && opt.isDeadEnd)
                {
                    chosen = opt;
                    break;
                }
            }

            if (chosen != null)
                s.TrySpawn(chosen); // permitido pois é dead end
        }
    }

    // ---------------- Forced Rooms logic ----------------
    [Serializable]
    public class ForcedRoom
    {
        [Tooltip("Room prefab to force spawn (must exist in a RoomSocket option).")]
        public GameObject prefab;

        [Tooltip("Minimum number of this room to spawn (1-10).")]
        [Range(1, 10)] public int min = 1;

        [Tooltip("Maximum number of this room to spawn (1-10).")]
        [Range(1, 10)] public int max = 1;
    }

    private void HandleSeedReadyInternal(int readySeed)
    {
        if (!autoApplyForcedRooms) return;
        if (_forcedApplied) return;
        // Defer a couple of frames so sockets that spawn on seed can resolve first
        StartCoroutine(ApplyForcedRoomsDeferred());
    }

    private IEnumerator ApplyForcedRoomsDeferred()
    {
        // Wait 2 frames to allow RoomSocket spawnOnStart to run
        yield return null;
        yield return null;
        ApplyForcedRoomsNow();
    }

    public void ApplyForcedRoomsNow()
    {
        if (_forcedApplied) return;
        if (forcedRooms == null || forcedRooms.Count == 0) { _forcedApplied = true; return; }

        int gseed = CurrentSeed;

        foreach (var fr in forcedRooms)
        {
            if (fr == null || fr.prefab == null) continue;

            // Normalize range
            int min = Mathf.Clamp(Mathf.Min(fr.min, fr.max), 1, 10);
            int max = Mathf.Clamp(Mathf.Max(fr.min, fr.max), 1, 10);

            // Deterministic count from seed + prefab name
            int target = DeterministicRange(gseed, GetSafeName(fr.prefab), min, max);
            if (target <= 0) continue;

            // Collect candidate sockets that can spawn this prefab via one of their options
            var candidates = CollectEligibleSocketsForPrefab(fr.prefab);

            // Sort deterministically: by distance to manager, then by hierarchy path string
            Vector3 origin = transform != null ? transform.position : Vector3.zero;
            candidates.Sort((a, b) =>
            {
                float da = (a.socket.transform.position - origin).sqrMagnitude;
                float db = (b.socket.transform.position - origin).sqrMagnitude;
                if (Mathf.Approximately(da, db))
                {
                    string pa = GetHierarchyPath(a.socket.transform);
                    string pb = GetHierarchyPath(b.socket.transform);
                    return string.Compare(pa, pb, StringComparison.Ordinal);
                }
                return da < db ? -1 : 1;
            });

            int placed = 0;
            for (int i = 0; i < candidates.Count && placed < target; i++)
            {
                var c = candidates[i];
                if (!IsEligible(c.socket)) continue;

                var go = c.socket.TrySpawn(c.option);
                if (go != null)
                {
                    placed++;
                    if (debugForcedRooms)
                        Debug.Log($"[RoomManager] Forced spawn '{GetSafeName(fr.prefab)}' at {GetHierarchyPath(c.socket.transform)}");
                }
            }

            if (debugForcedRooms)
                Debug.Log($"[RoomManager] Forced '{GetSafeName(fr.prefab)}': requested {target}, placed {placed}, candidates {candidates.Count}");
        }

        _forcedApplied = true;
    }

    private static bool IsEligible(RoomSocket s)
    {
        if (s == null) return false;
        if (s.deadEnd) return false;
        if (s.IsOccupied) return false;
        if (!s.HasOptions) return false;
        if (s.HasDescendantOccupied()) return false;
        return true;
    }

    private static List<(RoomSocket socket, RoomSocketOption option)> CollectEligibleSocketsForPrefab(GameObject prefab)
    {
        var list = new List<(RoomSocket, RoomSocketOption)>();
        var all = RoomSocket.All;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s == null) continue;
            var opts = s.options;
            if (opts == null) continue;
            for (int k = 0; k < opts.Count; k++)
            {
                var opt = opts[k];
                if (opt == null || opt.prefab == null) continue;
                if (ReferenceEquals(opt.prefab, prefab))
                {
                    list.Add((s, opt));
                }
            }
        }
        return list;
    }

    private static int DeterministicRange(int seed, string key, int min, int max)
    {
        if (min > max) { var t = min; min = max; max = t; }
        unchecked
        {
            int h = StableHash(seed.ToString() + ":" + key);
            uint uh = (uint)h;
            uint span = (uint)(max - min + 1);
            int v = (int)(uh % span);
            return min + v;
        }
    }

    private static string GetHierarchyPath(Transform t)
    {
        if (t == null) return string.Empty;
        var names = new System.Collections.Generic.Stack<string>();
        Transform it = t;
        while (it != null)
        {
            names.Push(it.name);
            it = it.parent;
        }
        return string.Join("/", names);
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            const int fnvPrime = 16777619;
            int hash = (int)2166136261;
            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= fnvPrime;
            }
            return hash;
        }
    }

    private static string GetSafeName(UnityEngine.Object o)
    {
        return o != null ? o.name : "(null)";
    }
}
