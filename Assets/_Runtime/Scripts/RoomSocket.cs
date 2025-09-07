// RoomSocket.cs
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class RoomSocketOption
{
    public GameObject prefab;     // Prefab a instanciar
    public Vector3 localOffset;   // Offset relativo ao socket (espaço local)
    public Vector3 localEuler;    // Rotação relativa ao socket (Euler)
}

[AddComponentMenu("Level/Room Socket")]
public class RoomSocket : MonoBehaviour
{
    [Header("Config")]
    public List<RoomSocketOption> options = new List<RoomSocketOption>();
    public bool spawnOnStart = false;          // Geralmente falso quando usado com RoomManager
    public bool deadEnd = false;               // Se true, não gera novos cômodos
    public Transform instanceParent;           // Pai do instanciado (default = este transform)

    [Header("Runtime (read-only)")]
    [SerializeField] private bool occupied = false;
    [SerializeField] private GameObject currentInstance;

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
    }

    void OnDisable()
    {
        All.Remove(this);
        Occupied.Remove(this);
    }

    void Start()
    {
        Invoke("PostStart", 2.0f);
    }

    void PostStart()
    {
        if (spawnOnStart) TrySpawnRandom();
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
        int i = Random.Range(0, options.Count);
        return TrySpawn(options[i]);
    }

    public GameObject TrySpawn(RoomSocketOption option)
    {
        if (!CanSpawn() || option == null || option.prefab == null)
            return null;

        Vector3 worldPos = transform.TransformPoint(option.localOffset);
        Quaternion worldRot = transform.rotation * Quaternion.Euler(option.localEuler);
        Transform parent = instanceParent ? instanceParent : transform;

        currentInstance = Instantiate(option.prefab, worldPos, worldRot, parent);
        MarkOccupied(true);

        // Registrar propriedade do objeto instanciado
        RegisterOwnedRoot(currentInstance.transform);

        // REQUISITO: logar o nome do socket mais próximo do instanciador
        // e SÓ marcar ocupado se o socket pertencer a um objeto instanciado por ESTE RoomSocket.
        LogAndOccupyNearestSocketOwnedOnly();

        return currentInstance;
    }

    public void Clear()
    {
        if (currentInstance != null)
        {
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

    /// <summary>
    /// Registra um root como "propriedade" deste RoomSocket.
    /// </summary>
    private void RegisterOwnedRoot(Transform root)
    {
        if (root == null) return;

        // Marca o root com um componente de ownership (informativo/depuração)
        var own = root.GetComponent<SpawnOwnership>();
        if (own == null) own = root.gameObject.AddComponent<SpawnOwnership>();
        own.owner = this;

        if (!ownedRoots.Contains(root))
            ownedRoots.Add(root);

        // Higieniza referências nulas antigas
        CleanupOwnedRoots();
    }

    /// <summary>
    /// Remove um root da lista de propriedade (quando destruído/clear).
    /// </summary>
    private void UnregisterOwnedRoot(Transform root)
    {
        if (root == null) return;
        ownedRoots.Remove(root);
        CleanupOwnedRoots();
    }

    /// <summary>
    /// Verdadeiro se 't' é o próprio root instanciado por este socket
    /// ou está dentro (descendente) de algum root instanciado por este socket.
    /// </summary>
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

    /// <summary>
    /// Remove entradas nulas da lista de ownership.
    /// </summary>
    private void CleanupOwnedRoots()
    {
        for (int i = ownedRoots.Count - 1; i >= 0; i--)
        {
            if (ownedRoots[i] == null)
                ownedRoots.RemoveAt(i);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (options != null)
        {
            foreach (var opt in options)
            {
                if (opt == null) continue;
                Vector3 p = transform.TransformPoint(opt.localOffset);
                Gizmos.DrawWireSphere(p, 0.1f);
            }
        }

        if (deadEnd)
        {
            Vector3 c = transform.position;
            float s = 0.3f;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(c + new Vector3(-s, 0, -s), c + new Vector3(s, 0, s));
            Gizmos.DrawLine(c + new Vector3(-s, 0, s), c + new Vector3(s, 0, -s));
        }
    }
#endif
}

/// <summary>
/// Marca um root instanciado e o associa ao RoomSocket "dono".
/// </summary>
public sealed class SpawnOwnership : MonoBehaviour
{
    public RoomSocket owner;
}
