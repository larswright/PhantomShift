// RoomSocket.cs
using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using Unity.AI.Navigation; // NavMeshSurface

[System.Serializable]
public class RoomSocketOption
{
    public GameObject prefab;     // Prefab a instanciar
    public Vector3 localOffset;   // Offset relativo ao socket (espaço local)
    public Vector3 localEuler;    // Rotação relativa ao socket (Euler)

    [Tooltip("Se verdadeiro, este prefab é uma peça de término (dead end).")]
    public bool isDeadEnd = false;   // NOVO
}

[AddComponentMenu("Level/Room Socket")]
public class RoomSocket : MonoBehaviour, ISeedReceiver
{
    [Header("Config")]
    public List<RoomSocketOption> options = new List<RoomSocketOption>();
    public bool spawnOnStart = false;          // Se true, este socket instancia ao receber a seed
    public bool deadEnd = false;               // Se true, não gera novos cômodos
    public Transform instanceParent;           // Pai do instanciado (default = este transform)

    [Header("Runtime (read-only)")]
    [SerializeField] private bool occupied = false;
    [SerializeField] private GameObject currentInstance;

    [Header("Seed (runtime)")]
    [SerializeField] private bool hasSeed = false;
    [SerializeField] private int receivedSeed = 0;   // Seed recebida do RoomManager

    // Registro global
    public static readonly List<RoomSocket> All = new List<RoomSocket>();
    public static readonly HashSet<RoomSocket> Occupied = new HashSet<RoomSocket>();

    // Raízes de objetos que ESTE socket instanciou (controle de "propriedade")
    [SerializeField] private List<Transform> ownedRoots = new List<Transform>();

    public bool IsOccupied => occupied;
    public bool HasOptions => options != null && options.Count > 0;
    public GameObject CurrentInstance => currentInstance;

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        if (occupied) Occupied.Add(this);
        RoomManager.SeedReady += HandleSeedReady;
    }

    void OnDisable()
    {
        All.Remove(this);
        Occupied.Remove(this);
        RoomManager.SeedReady -= HandleSeedReady;
    }

    void Start()
    {
        // Tenta obter a seed imediatamente se já estiver disponível
        if (RoomManager.HasSeed)
        {
            HandleSeedReady(RoomManager.CurrentSeed);
        }
        else
        {
            // Fallback: modo offline (sem rede) pode pegar do Instance direto
            var mgr = RoomManager.Instance;
            if (mgr != null)
            {
                HandleSeedReady(mgr.seed);
            }
        }
    }

    void PostStart()
    {
        // Intencionalmente vazio: spawns agora ocorrem ao receber a seed global
    }

    /// <summary>
    /// Retorna true se QUALQUER RoomSocket descendente (excluindo este) estiver ocupado.
    /// Inclui inativos para ser conservador.
    /// </summary>
    public bool HasDescendantOccupied()
    {
        var descendants = GetComponentsInChildren<RoomSocket>(includeInactive: true);
        for (int i = 0; i < descendants.Length; i++)
        {
            var s = descendants[i];
            if (s == null || s == this) continue;
            if (s.occupied) return true;
        }
        return false;
    }

    /// <summary>
    /// Elegibilidade para spawn. Inclui checagem de descendentes ocupados.
    /// </summary>
    bool CanSpawn()
    {
        if (deadEnd) return false;
        if (occupied) return false;
        if (!HasOptions) return false;
        if (HasDescendantOccupied()) return false;
        return true;
    }

    public GameObject TrySpawnRandom()
    {
        if (!CanSpawn()) return null;

        // Define o pool respeitando o limite global
        List<RoomSocketOption> pool = options;
        if (RoomManager.IsAtMaxRooms)
        {
            pool = new List<RoomSocketOption>();
            for (int k = 0; k < options.Count; k++)
            {
                var o = options[k];
                if (o != null && o.prefab != null && o.isDeadEnd)
                    pool.Add(o);
            }
            if (pool.Count == 0) return null; // sem dead ends para selar
        }

        int idx;
        if (hasSeed)
        {
            int h = StableHash(receivedSeed.ToString() + ":" + GetHierarchyPath(transform));
            uint uh = unchecked((uint)h);
            idx = (int)(uh % (uint)pool.Count);
        }
        else
        {
            idx = UnityEngine.Random.Range(0, pool.Count);
        }

        return TrySpawn(pool[idx]);
    }

    /// <summary>
    /// Spawn usando uma opção específica
    /// </summary>
    public GameObject TrySpawn(RoomSocketOption option)
    {
        if (!CanSpawn() || option == null || option.prefab == null)
            return null;

        // Se já atingiu o máximo de salas, apenas dead ends podem ser instanciados
        if (RoomManager.IsAtMaxRooms && !option.isDeadEnd)
            return null;

        Vector3 worldPos = transform.TransformPoint(option.localOffset);
        Quaternion worldRot = transform.rotation * Quaternion.Euler(option.localEuler);
        Transform parent = instanceParent ? instanceParent : transform;

        currentInstance = Instantiate(option.prefab, worldPos, worldRot, parent);
        MarkOccupied(true);

        // Registrar propriedade do objeto instanciado
        RegisterOwnedRoot(currentInstance.transform);

        // Assim que o socket instanciar um cômodo, procurar NavMeshSurface no root
        // do objeto instanciado (ou em seus filhos) e fazer o bake.
        TryBakeNavMesh(currentInstance);

        // REQUISITO: logar o nome do socket mais próximo do instanciador
        // e SÓ marcar ocupado se o socket pertencer a um objeto instanciado por ESTE RoomSocket.
        LogAndOccupyNearestSocketOwnedOnly();

        RoomManager.NotifyRoomSpawned(currentInstance); // NOVO

        return currentInstance;
    }

    private void TryBakeNavMesh(GameObject root)
    {
        if (root == null) return;

        // Procura por um ou mais NavMeshSurface no objeto instanciado
        // (inclui filhos inativos para garantir que não fique de fora).
        var surfaces = root.GetComponentsInChildren<NavMeshSurface>(includeInactive: true);
        if (surfaces == null || surfaces.Length == 0)
        {
            // Se não houver no instanciado, como fallback tenta no root do instanciador
            // (algumas arquiteturas deixam o Surface no pai da hierarquia).
            var fallback = transform.root.GetComponentsInChildren<NavMeshSurface>(includeInactive: true);
            surfaces = fallback ?? System.Array.Empty<NavMeshSurface>();
        }

        for (int i = 0; i < surfaces.Length; i++)
        {
            var s = surfaces[i];
            if (s == null) continue;
            try { s.BuildNavMesh(); }
            catch (System.Exception e) { Debug.LogException(e, s); }
        }
    }

    /// <summary>
    /// Tenta spawnar deterministicamente usando a seed recebida.
    /// </summary>
    public GameObject TrySpawnDeterministic()
    {
        if (!hasSeed) return null;
        return TrySpawnRandom();
    }

    public void Clear()
    {
        if (currentInstance != null)
        {
            RoomManager.NotifyRoomDespawned(currentInstance); // NOVO

            // Remover da lista de propriedade, se presente
            UnregisterOwnedRoot(currentInstance.transform);

            Destroy(currentInstance);
            currentInstance = null;
        }
        MarkOccupied(false);
    }

    public void MarkOccupied(bool value)
    {
        occupied = value;
        if (value) Occupied.Add(this);
        else Occupied.Remove(this);
    }

    /// <summary>
    /// Procura o socket elegível MAIS PRÓXIMO do "from", de forma determinística.
    /// Elegível = !deadEnd, !occupied, HasOptions, !HasDescendantOccupied().
    /// Empate desempatado via menor InstanceID.
    /// </summary>
    public static RoomSocket FindNearestEligible(RoomSocket from)
    {
        if (from == null) return null;

        RoomSocket best = null;
        float bestDistSq = float.PositiveInfinity;
        int bestId = int.MaxValue;
        Vector3 p = from.transform.position;

        for (int i = 0; i < All.Count; i++)
        {
            var s = All[i];
            if (s == null || s == from) continue;
            if (s.deadEnd) continue;
            if (s.occupied) continue;
            if (!s.HasOptions) continue;
            if (s.HasDescendantOccupied()) continue;

            float d2 = (s.transform.position - p).sqrMagnitude;

            if (d2 < bestDistSq || (Mathf.Approximately(d2, bestDistSq) && s.GetInstanceID() < bestId))
            {
                best = s;
                bestDistSq = d2;
                bestId = s.GetInstanceID();
            }
        }
        return best;
    }

    /// <summary>
    /// Spawna a partir do socket elegível mais próximo do "source".
    /// Reforça o MarkOccupied no socket fonte, por segurança.
    /// </summary>
    public static GameObject SpawnNearestFrom(RoomSocket source, RoomSocketOption option)
    {
        if (source == null || option == null || option.prefab == null) return null;
        var target = FindNearestEligible(source);
        if (target == null) return null;

        var go = target.TrySpawn(option);
        source.MarkOccupied(true);
        return go;
    }

    /// <summary>
    /// Loga o nome do socket mais próximo (qualquer), mas SÓ marca ocupado
    /// se o socket estiver dentro de um objeto que ESTE RoomSocket instanciou.
    /// </summary>
    private void LogAndOccupyNearestSocketOwnedOnly()
    {
        RoomSocket nearest = null;
        float bestDistSq = float.PositiveInfinity;
        int bestId = int.MaxValue;
        Vector3 p = transform.position;

        for (int i = 0; i < All.Count; i++)
        {
            var s = All[i];
            if (s == null || s == this) continue;

            float d2 = (s.transform.position - p).sqrMagnitude;
            if (d2 < bestDistSq || (Mathf.Approximately(d2, bestDistSq) && s.GetInstanceID() < bestId))
            {
                nearest = s;
                bestDistSq = d2;
                bestId = s.GetInstanceID();
            }
        }

        if (nearest != null)
        {
            Debug.Log(nearest.gameObject.name);

            // Somente marcar como ocupado se o socket pertencer a um root instanciado por este RoomSocket
            if (IsOwnedTransform(nearest.transform))
            {
                nearest.MarkOccupied(true);
            }
        }
    }

    // ----- Ownership helpers -----
    private void RegisterOwnedRoot(Transform root)
    {
        if (root == null) return;

        var own = root.GetComponent<SpawnOwnership>();
        if (own == null) own = root.gameObject.AddComponent<SpawnOwnership>();
        own.owner = this;

        if (!ownedRoots.Contains(root))
            ownedRoots.Add(root);

        CleanupOwnedRoots();
    }

    private void UnregisterOwnedRoot(Transform root)
    {
        if (root == null) return;
        ownedRoots.Remove(root);
        CleanupOwnedRoots();
    }

    private bool IsOwnedTransform(Transform t)
    {
        if (t == null) return false;
        CleanupOwnedRoots();
        for (int i = 0; i < ownedRoots.Count; i++)
        {
            var r = ownedRoots[i];
            if (r == null) continue;
            if (t == r || t.IsChildOf(r))
                return true;
        }
        return false;
    }

    private void CleanupOwnedRoots()
    {
        for (int i = ownedRoots.Count - 1; i >= 0; i--)
        {
            if (ownedRoots[i] == null)
                ownedRoots.RemoveAt(i);
        }
    }

    // ---------------- Seed helpers ----------------
    private void HandleSeedReady(int globalSeed)
    {
        if (hasSeed && receivedSeed == globalSeed)
            return; // já configurado

        receivedSeed = globalSeed;
        hasSeed = true;

        if (spawnOnStart && currentInstance == null)
        {
            if (!RoomManager.ForcedApplied)
            {
                StartCoroutine(SpawnAfterForcedApplied());
            }
            else
            {
                TrySpawnDeterministic();
            }
        }
    }

    private IEnumerator SpawnAfterForcedApplied()
    {
        const int maxFrames = 300; // ~5s a 60 FPS
        int frames = 0;
        while (RoomManager.Instance != null && RoomManager.Instance.autoApplyForcedRooms && !RoomManager.ForcedApplied && frames < maxFrames)
        {
            frames++;
            yield return null;
        }

        if (currentInstance == null)
        {
            TrySpawnDeterministic();
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
        // FNV-1a 32-bit hash (estável entre execuções)
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

    // ISeedReceiver para compatibilidade
    public void SetSeed(int seed)
    {
        HandleSeedReady(seed);
    }
}

/// <summary>
/// Marca um root instanciado e o associa ao RoomSocket "dono".
/// </summary>
public sealed class SpawnOwnership : MonoBehaviour
{
    public RoomSocket owner;
}
