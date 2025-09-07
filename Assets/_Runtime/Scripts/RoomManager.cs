// RoomManager.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Geração determinística de casa a partir de uma seed.
/// - Garante um cômodo inicial (existente na cena ou instanciado de um prefab).
/// - Mantém uma fronteira de sockets livres e decide, por seed, o que instanciar em cada um.
/// - Ao instanciar, marca o socket fonte e o socket “de retorno” do novo cômodo como ocupados.
/// </summary>
[AddComponentMenu("Level/Room Manager")]
public class RoomManager : MonoBehaviour
{
    [Header("Seed & Execução")]
    [Tooltip("Seed para geração determinística.")]
    public int seed = 123456;
    [Tooltip("Rodar automaticamente no Start().")]
    public bool autoRunOnStart = true;
    [Tooltip("Usar BFS (true) ou DFS (false) na fronteira.")]
    public bool useBFS = true;

    [Header("Limites")]
    [Tooltip("Número máximo de cômodos, incluindo o inicial.")]
    public int maxRooms = 12;
    [Tooltip("Limite de iterações de segurança, evita loops.")]
    public int safetyIterations = 10000;

    [Header("Cena inicial")]
    [Tooltip("Se definido, este objeto já é o 'hall/entrada' existente na cena.")]
    public GameObject existingInitialRoom;
    [Tooltip("Se não houver existingInitialRoom, usa este prefab.")]
    public GameObject initialRoomPrefab;
    [Tooltip("Âncora para instanciar o cômodo inicial (apenas quando usado prefab).")]
    public Transform initialAnchor;

    [Header("Parenting")]
    [Tooltip("Pai global para os cômodos instanciados (opcional).")]
    public Transform worldParent;

    [Header("Log")]
    public bool verboseLog = false;

    // RNG próprio (não interfere no UnityEngine.Random global)
    System.Random rng;

    // Estado
    readonly Queue<RoomSocket> frontierQueue = new Queue<RoomSocket>();
    readonly Stack<RoomSocket> frontierStack = new Stack<RoomSocket>();
    int roomsPlaced = 0;

    void Start()
    {
        if (autoRunOnStart) Generate();
    }

    public void Generate()
    {
        rng = new System.Random(seed);
        roomsPlaced = 0;
        frontierQueue.Clear();
        frontierStack.Clear();

        GameObject startRoom = BootstrapInitialRoom();
        if (startRoom == null)
        {
            Debug.LogError("[RoomManager] Sala inicial não definida.");
            return;
        }

        CollectFrontierFrom(startRoom, exclude: null);

        int iterations = 0;
        while (HasFrontier() && roomsPlaced < Mathf.Max(1, maxRooms) && iterations < safetyIterations)
        {
            iterations++;

            var socket = PopFrontier();
            if (socket == null) continue;
            if (socket.deadEnd || socket.IsOccupied || !socket.HasOptions) continue;

            // Seleciona deterministicamente uma opção válida
            var candidates = GetValidOptions(socket);
            if (candidates.Count == 0) continue;

            int pick = rng.Next(candidates.Count);
            var chosen = candidates[pick];

            var instance = socket.TrySpawn(chosen);
            if (instance == null) continue;

            // Reparent global, se solicitado
            if (worldParent != null) instance.transform.SetParent(worldParent, true);

            roomsPlaced++;

            // Marcar o socket “de retorno” do novo cômodo e adicionar novos sockets à fronteira
            SealBackConnectionAndExpand(socket, instance);

            // Parada: respeitar maxRooms (inclui a inicial)
            if (roomsPlaced >= maxRooms)
                break;
        }

        if (verboseLog)
            Debug.Log($"[RoomManager] Geração concluída. Iterações={iterations}, Rooms={roomsPlaced} (inclui inicial).");
    }

    GameObject BootstrapInitialRoom()
    {
        GameObject start = existingInitialRoom;
        if (start == null && initialRoomPrefab != null)
        {
            Vector3 pos = initialAnchor ? initialAnchor.position : Vector3.zero;
            Quaternion rot = initialAnchor ? initialAnchor.rotation : Quaternion.identity;
            start = Instantiate(initialRoomPrefab, pos, rot, worldParent ? worldParent : null);
            roomsPlaced++; // conta a sala inicial
            if (verboseLog) Debug.Log("[RoomManager] Inicial instanciado via prefab.");
        }
        else if (start != null)
        {
            roomsPlaced++; // conta a sala inicial existente
            if (verboseLog) Debug.Log("[RoomManager] Inicial existente encontrado na cena.");
        }
        return start;
    }

    void CollectFrontierFrom(GameObject root, RoomSocket exclude)
    {
        var sockets = root.GetComponentsInChildren<RoomSocket>(true);
        foreach (var s in sockets)
        {
            if (s == null) continue;
            if (s == exclude) continue;
            if (s.deadEnd) continue;
            if (s.IsOccupied) continue;
            PushFrontier(s);
        }
    }

    List<RoomSocketOption> GetValidOptions(RoomSocket socket)
    {
        var list = new List<RoomSocketOption>();
        if (socket.options == null) return list;
        foreach (var o in socket.options)
        {
            if (o != null && o.prefab != null)
                list.Add(o);
        }
        // Embaralha determinísticamente pela seed corrente (opcional)
        FisherYates(list);
        return list;
    }

    void SealBackConnectionAndExpand(RoomSocket sourceSocket, GameObject newRoom)
    {
        // Encontrar no novo cômodo o socket que “casou” com o sourceSocket:
        // heurística: menor distância ao socket fonte + orientação oposta (maior dot negativo).
        var candidateSockets = newRoom.GetComponentsInChildren<RoomSocket>(true);
        RoomSocket best = null;
        float bestScore = float.PositiveInfinity;

        Vector3 srcPos = sourceSocket.transform.position;
        Vector3 srcFwd = sourceSocket.transform.forward;

        foreach (var s in candidateSockets)
        {
            if (s == null) continue;

            // Não considerar sockets já ocupados ou deadEnd para a volta
            if (s.IsOccupied) continue;

            float dist = (s.transform.position - srcPos).sqrMagnitude;
            float facing = 1f - Mathf.Max(-1f, Vector3.Dot(s.transform.forward, -srcFwd));
            // facing ~0 quando opostos, ~2 quando alinhados; somar ao custo
            float score = dist + facing * 0.25f; // peso leve para orientação

            if (score < bestScore)
            {
                bestScore = score;
                best = s;
            }
        }

        // Marca os dois lados como ocupados (source já foi marcado ao instanciar)
        if (best != null)
            best.MarkOccupied(true);

        // Expande fronteira com os demais sockets do novo cômodo
        foreach (var s in candidateSockets)
        {
            if (s == null) continue;
            if (s == best) continue;
            if (s.deadEnd) continue;
            if (s.IsOccupied) continue;
            PushFrontier(s);
        }
    }

    // ---- Fronteira (BFS/DFS) ----
    bool HasFrontier()
    {
        return useBFS ? frontierQueue.Count > 0 : frontierStack.Count > 0;
    }

    RoomSocket PopFrontier()
    {
        if (useBFS)
        {
            while (frontierQueue.Count > 0)
            {
                var s = frontierQueue.Dequeue();
                if (s != null) return s;
            }
        }
        else
        {
            while (frontierStack.Count > 0)
            {
                var s = frontierStack.Pop();
                if (s != null) return s;
            }
        }
        return null;
    }

    void PushFrontier(RoomSocket s)
    {
        if (useBFS) frontierQueue.Enqueue(s);
        else frontierStack.Push(s);
    }

    // ---- Util ----
    void FisherYates<T>(IList<T> arr)
    {
        for (int i = 0; i < arr.Count; i++)
        {
            int j = i + rng.Next(arr.Count - i);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
    }
}
