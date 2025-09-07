using UnityEngine;
using System.Collections.Generic;

public class HouseBuilder : MonoBehaviour {
    public HouseProgram Program;
    public PrefabCatalog Catalog;
    public Transform Root;

    [Header("Seed")]
    public int Seed = 12345;

    System.Random _rngRooms;
    System.Random _rngCorrs;

    [ContextMenu("Build")]
    public void Build() {
        ClearChildren(Root);

        var g = GraphSampler.Sample(Program, Seed);
        var lay = LayoutEmbedder.Embed(g, Program, Seed + 1);

        // RNGs determinísticos por categoria
        _rngRooms = Rng.Get(Seed + 10);
        _rngCorrs = Rng.Get(Seed + 11);

        // 1) Instancia salas
        var goByNode = new Dictionary<int, GameObject>();
        foreach (var pr in lay.Rooms.Values) {
            var prefab = Catalog.PickRoom(pr.Node.ArchetypeId, _rngRooms);
            if (prefab == null) continue;
            var worldPos = CellToWorld(pr.Rect.position, Program.CellSizeMeters);
            var go = Instantiate(prefab, worldPos, Quaternion.identity, Root);
            go.name = $"Room_{pr.Node.ArchetypeId}_{pr.Node.Id}";
            goByNode[pr.Node.Id] = go;

            // Ajusta escala aproximada para caber no retângulo (opcional)
            FitPrefabToRect(go, pr.Rect, Program.CellSizeMeters);
        }

        // 2) Conecta portas diretas
        foreach (var link in lay.Doors) {
            int A = link.A; int B = link.B;
            if (!goByNode.ContainsKey(A) || !goByNode.ContainsKey(B)) continue;
            TryConnectByDoor(goByNode[A], goByNode[B]);
        }

        // 3) Constrói corredores quando necessário
        foreach (var link in lay.Corridors) {
            int A = link.A; int B = link.B;
            if (!goByNode.ContainsKey(A) || !goByNode.ContainsKey(B)) continue;
            PlaceCorridorBetween(goByNode[A], goByNode[B]);
        }

        // 4) Validação
        var validator = new LayoutValidator();
        validator.ValidateConnectivity(goByNode, Root);

        // 5) Props
        PropPlacer.Populate(Root, Seed + 2);
    }

    Vector3 CellToWorld(Vector2Int cell, Vector2 cellMeters) {
        return new Vector3(cell.x * cellMeters.x, 0f, cell.y * cellMeters.y);
    }

    void FitPrefabToRect(GameObject room, RectInt rect, Vector2 cellMeters) {
        // Simplificado: centraliza sobre o retângulo. Ajustes finos por artista.
        var size = new Vector3(rect.width * cellMeters.x, 1f, rect.height * cellMeters.y);
        // Opcional: escalar “casco” (colliders/volumes) do room para encaixe visual.
        // Deixe paredes internas/portas fixas via sockets para manter compatibilidade.
    }

    void TryConnectByDoor(GameObject a, GameObject b) {
        if (a == null || b == null) return;
        var sa = a.GetComponentsInChildren<DoorSocket>();
        var sb = b.GetComponentsInChildren<DoorSocket>();

        foreach (var A in sa) {
            foreach (var B in sb) {
                if (A.Profile == null || B.Profile == null) continue;
                if (!IsCompatible(A.Profile, B.Profile)) continue;

                // Alinha B à posição/normal de A (snap + giro)
                AlignSocketPair(A, B);
                // “Abrir” porta (remover parede, spawn de batente, etc.)
                OpenDoorBetween(A, B);
                return;
            }
        }
    }

    void PlaceCorridorBetween(GameObject a, GameObject b) {
        // Tile por célula em L ao longo do grid
        Vector2 cell = Program.CellSizeMeters;
        Vector2Int Grid(Vector3 w) => new Vector2Int(
            Mathf.RoundToInt(w.x / cell.x),
            Mathf.RoundToInt(w.z / cell.y));
        Vector3 World(Vector2Int c) => new Vector3(c.x * cell.x, 0f, c.y * cell.y);

        var ga = Grid(a.transform.position);
        var gb = Grid(b.transform.position);

        var cur = ga;
        while (cur.x != gb.x) {
            cur.x += (gb.x > cur.x) ? 1 : -1;
            SpawnCorridorSegment(World(cur), (gb.x > ga.x) ? Quaternion.identity : Quaternion.Euler(0, 180, 0));
        }
        while (cur.y != gb.y) {
            cur.y += (gb.y > cur.y) ? 1 : -1;
            SpawnCorridorSegment(World(cur), (gb.y > ga.y) ? Quaternion.Euler(0, 90, 0) : Quaternion.Euler(0, -90, 0));
        }
    }

    void SpawnCorridorSegment(Vector3 pos, Quaternion rot) {
        var prefab = Catalog.PickCorridor(_rngCorrs);
        if (prefab == null) return;
        var go = Instantiate(prefab, pos, rot, Root);
        go.name = "Corridor_Segment";
    }

    bool IsCompatible(SocketProfile A, SocketProfile B) {
        if (A == null || B == null) return false;
        if (A.CompatibleTags != null)
            foreach (var t in A.CompatibleTags) if (t == B.Tag) return true;
        if (B.CompatibleTags != null)
            foreach (var t in B.CompatibleTags) if (t == A.Tag) return true;
        return false;
    }

    Transform GetRoomRoot(Transform t) {
        var r = t.GetComponentInParent<RoomRoot>();
        return (r != null) ? r.transform : t.root;
    }

    void AlignSocketPair(DoorSocket A, DoorSocket B) {
        var roomRoot = GetRoomRoot(B.transform);
        var nA = A.transform.rotation * A.NormalLocal;
        var nB = B.transform.rotation * B.NormalLocal;

        // 1) rotaciona o roomRoot para que o socket B aponte contra A
        var rotDelta = Quaternion.FromToRotation(nB, -nA);
        var pivot = B.transform.position;
        var pos = roomRoot.position;
        roomRoot.rotation = rotDelta * roomRoot.rotation;
        roomRoot.position = pivot + rotDelta * (pos - pivot);

        // 2) translada o roomRoot para coincidir os sockets
        var delta = A.transform.position - B.transform.position;
        roomRoot.position += delta;
    }

    void OpenDoorBetween(DoorSocket A, DoorSocket B) {
        // Exemplos: desativar malha da parede, instanciar “door frame” compatível, etc.
        // Deixe o conteúdo visual ao artista; aqui apenas sinalize que a conexão foi criada.
        Debug.DrawLine(A.transform.position, B.transform.position, Color.green, 5f);
    }

    static void ClearChildren(Transform t) {
        if (t == null) return;
        for (int i = t.childCount - 1; i >= 0; i--) {
#if UNITY_EDITOR
            GameObject.DestroyImmediate(t.GetChild(i).gameObject);
#else
            GameObject.Destroy(t.GetChild(i).gameObject);
#endif
        }
    }
}
