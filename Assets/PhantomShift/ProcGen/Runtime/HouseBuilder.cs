using UnityEngine;
using System.Collections.Generic;

public class HouseBuilder : MonoBehaviour {
    public HouseProgram Program;
    public PrefabCatalog Catalog;
    public Transform Root;

    [Header("Seed")]
    public int Seed = 12345;

    [ContextMenu("Build")]
    public void Build() {
        ClearChildren(Root);

        var g = GraphSampler.Sample(Program, Seed);
        var lay = LayoutEmbedder.Embed(g, Program, Seed + 1);

        // 1) Instancia salas
        var goByNode = new Dictionary<int, GameObject>();
        foreach (var pr in lay.Rooms.Values) {
            var prefab = Catalog.PickRoom(pr.Node.ArchetypeId, Rng.Get(Seed));
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
        // Estratégia simples: instanciar um corredor retilíneo entre centros projetados (ou pontos de sockets)
        var midA = a.GetComponent<Renderer>()?.bounds.center ?? a.transform.position;
        var midB = b.GetComponent<Renderer>()?.bounds.center ?? b.transform.position;
        var dir = (midB - midA); dir.y = 0f;
        var prefab = Catalog.PickCorridor(Rng.Shared);
        if (prefab == null) return;

        var corridor = Instantiate(prefab, (midA + midB) * 0.5f, Quaternion.LookRotation(dir.normalized, Vector3.up), Root);
        corridor.name = $"Corridor_{a.name}_{b.name}";
        // Opcional: modularizar em segmentos e garantir colisão/navmesh contínuo.
    }

    bool IsCompatible(SocketProfile A, SocketProfile B) {
        if (A == null || B == null) return false;
        if (A.CompatibleTags != null)
            foreach (var t in A.CompatibleTags) if (t == B.Tag) return true;
        if (B.CompatibleTags != null)
            foreach (var t in B.CompatibleTags) if (t == A.Tag) return true;
        return false;
    }

    void AlignSocketPair(DoorSocket A, DoorSocket B) {
        // Giro de B para que sua normal aponte contra a de A
        var worldA = A.transform.position;
        var worldN = A.transform.rotation * A.NormalLocal;
        var targetRot = Quaternion.LookRotation(-worldN, Vector3.up);
        B.transform.rotation = targetRot;

        // Traslada B para sobrepor âncoras
        var delta = worldA - B.transform.position;
        B.transform.position += delta;
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

