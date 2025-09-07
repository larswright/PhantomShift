using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class HouseBuilder : MonoBehaviour {
    public HouseProgram Program;
    public PrefabCatalog Catalog;
    public Transform Root;

    [Header("Seed")]
    public int Seed = 12345;

    System.Random _rngRooms;
    System.Random _rngCorrs;

    // Sockets já usados nesta build
    HashSet<DoorSocket> _usedSockets = new HashSet<DoorSocket>();

    // Ocupação de grade (salas e corredores) e retângulos por GameObject
    Dictionary<GameObject, RectInt> _rectByGo = new Dictionary<GameObject, RectInt>();
    HashSet<Vector2Int> _occupiedRoomCells = new HashSet<Vector2Int>();
    HashSet<Vector2Int> _occupiedCorrCells = new HashSet<Vector2Int>();

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
        _rectByGo.Clear();
        foreach (var pr in lay.Rooms.Values) {
            var prefab = Catalog.PickRoom(pr.Node.ArchetypeId, _rngRooms);
            if (prefab == null) continue;
            // Instancia no centro do retângulo em vez do canto inferior esquerdo
            var center = new Vector2(
                pr.Rect.x + pr.Rect.width * 0.5f,
                pr.Rect.y + pr.Rect.height * 0.5f);
            var worldPos = CellToWorld(center, Program.CellSizeMeters);
            var go = Instantiate(prefab, worldPos, Quaternion.identity, Root);
            go.name = $"Room_{pr.Node.ArchetypeId}_{pr.Node.Id}";
            goByNode[pr.Node.Id] = go;
            _rectByGo[go] = pr.Rect;

            // Ajusta escala aproximada para caber no retângulo (opcional)
            FitPrefabToRect(go, pr.Rect, Program.CellSizeMeters);
        }

        // Zera controle de uso por build
        _usedSockets.Clear();
        _occupiedCorrCells.Clear();
        _occupiedRoomCells = BuildRoomOccupancy(lay.Rooms.Values.Select(r => r.Rect));

        // 2) Conecta portas diretas; se falhar, deixa para fallback por corredor
        var doorFallback = new List<(GameObject A, GameObject B)>();
        foreach (var link in lay.Doors) {
            int A = link.A; int B = link.B;
            if (!goByNode.ContainsKey(A) || !goByNode.ContainsKey(B)) continue;
            bool ok = TryConnectByDoor_NoReuse(goByNode[A], goByNode[B]);
            if (!ok) doorFallback.Add((goByNode[A], goByNode[B]));
        }

        // 3) Constrói corredores quando necessário
        foreach (var link in lay.Corridors) {
            int A = link.A; int B = link.B;
            if (!goByNode.ContainsKey(A) || !goByNode.ContainsKey(B)) continue;
            PlaceCorridorBetween(goByNode[A], goByNode[B]);
        }

        // 3b) Fallback: corredores para pares sem sockets livres
        foreach (var (A, B) in doorFallback) {
            PlaceCorridorBetween(A, B);
        }

        // 4) Validação
        var validator = new LayoutValidator();
        validator.ValidateConnectivity(goByNode, Root);

        // 5) Props
        PropPlacer.Populate(Root, Seed + 2);
    }

    Vector3 CellToWorld(Vector2 cell, Vector2 cellMeters) {
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

    // Versão que evita reuso de socket e prioriza melhor alinhamento
    bool TryConnectByDoor_NoReuse(GameObject a, GameObject b) {
        if (a == null || b == null) return false;

        var sa = a.GetComponentsInChildren<DoorSocket>(true);
        var sb = b.GetComponentsInChildren<DoorSocket>(true);
        if (sa == null || sa.Length == 0 || sb == null || sb.Length == 0) return false;

        // Apenas tente alinhar se os retângulos estiverem adjacentes (encostando)
        if (!RoomsTouching(a, b)) return false;

        // Direção A->B e B->A
        Vector3 ab = (b.transform.position - a.transform.position).normalized;
        Vector3 ba = -ab;

        // Ordena candidatos, ignorando sockets já usados
        var candA = sa.Where(SokFree)
                      .OrderByDescending(s => DotWorld(s, ab))
                      .ThenBy(s => (s.transform.position - b.transform.position).sqrMagnitude)
                      .ToArray();

        var candB = sb.Where(SokFree)
                      .OrderByDescending(s => DotWorld(s, ba))
                      .ThenBy(s => (s.transform.position - a.transform.position).sqrMagnitude)
                      .ToArray();

        float maxSnap = Mathf.Min(Program.CellSizeMeters.x, Program.CellSizeMeters.y) * 1.2f;

        foreach (var A in candA) {
            foreach (var B in candB) {
                if (!Compatible(A, B)) continue;

                // Ignora se sockets estão muito distantes para um micro-snap
                if ((A.transform.position - B.transform.position).magnitude > maxSnap) continue;

                // Encaixa B em A e abre a passagem (micro-ajuste apenas)
                if (AlignSocketPair(A, B, maxSnap)) {
                    OpenDoorBetween(A, B);

                    // Marca como usados
                    _usedSockets.Add(A);
                    _usedSockets.Add(B);
                    return true;
                }
            }
        }
        return false;

        // ----- helpers locais -----
        bool SokFree(DoorSocket s) => s != null && !_usedSockets.Contains(s) && s.Profile != null;
        bool Compatible(DoorSocket A, DoorSocket B) {
            if (A?.Profile == null || B?.Profile == null) return false;
            return IsCompatible(A.Profile, B.Profile);
        }
        float DotWorld(DoorSocket s, Vector3 dir) {
            var n = s.transform.rotation * s.NormalLocal;
            return Vector3.Dot(n.normalized, dir);
        }
    }

    void PlaceCorridorBetween(GameObject a, GameObject b) {
        // Pathfinding em grade evitando células ocupadas por salas e deduplicando segmentos
        Vector2 cell = Program.CellSizeMeters;
        Vector2Int Grid(Vector3 w) => new Vector2Int(
            Mathf.RoundToInt(w.x / cell.x),
            Mathf.RoundToInt(w.z / cell.y));
        Vector3 World(Vector2Int c) => new Vector3(c.x * cell.x, 0f, c.y * cell.y);

        // Determina pontos de partida/chegada na borda externa dos retângulos
        if (!_rectByGo.TryGetValue(a, out var ra) || !_rectByGo.TryGetValue(b, out var rb)) return;

        var start = PickBestBorderCellOutside(ra, rb);
        var goal  = PickBestBorderCellOutside(rb, ra);

        var path = FindPath(start, goal, _occupiedRoomCells);
        if (path == null || path.Count == 0) return;

        for (int i = 0; i < path.Count; i++) {
            var c = path[i];
            if (_occupiedRoomCells.Contains(c)) continue; // segurança: não atravessar salas
            if (_occupiedCorrCells.Contains(c)) continue; // dedupe

            Quaternion rot = Quaternion.identity;
            if (i > 0) {
                var prev = path[i - 1];
                var d = c - prev;
                if (d.x != 0) rot = (d.x > 0) ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
                else if (d.y != 0) rot = (d.y > 0) ? Quaternion.Euler(0, 90, 0) : Quaternion.Euler(0, -90, 0);
            }

            SpawnCorridorSegment(World(c), rot);
            _occupiedCorrCells.Add(c);
        }

        // ---- locais ----
        Vector2Int PickBestBorderCellOutside(RectInt from, RectInt toward) {
            var candidates = BorderCellsOutside(from).Where(c => !_occupiedRoomCells.Contains(c)).ToList();
            if (candidates.Count == 0) return new Vector2Int(from.xMax, from.y); // fallback
            // heurística: escolhe o mais próximo do centro do alvo
            var tc = new Vector2(toward.x + toward.width * 0.5f, toward.y + toward.height * 0.5f);
            candidates.Sort((c1, c2) =>
                (Manhattan(c1, tc)).CompareTo(Manhattan(c2, tc))
            );
            return candidates[0];
        }

        float Manhattan(Vector2Int a, Vector2 b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
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

    // Alinha com micro-ajuste: yaw-only e deslocamento limitado
    bool AlignSocketPair(DoorSocket A, DoorSocket B, float maxSnap) {
        var roomRoot = GetRoomRoot(B.transform);
        var nA = (A.transform.rotation * A.NormalLocal);
        var nB = (B.transform.rotation * B.NormalLocal);

        // Alinha apenas no plano XZ (yaw)
        Vector3 nA2 = new Vector3(nA.x, 0f, nA.z).normalized;
        Vector3 nB2 = new Vector3(nB.x, 0f, nB.z).normalized;
        if (nA2.sqrMagnitude < 1e-4f || nB2.sqrMagnitude < 1e-4f) return false;

        float angle = Vector3.SignedAngle(nB2, -nA2, Vector3.up);
        var rotDelta = Quaternion.AngleAxis(angle, Vector3.up);
        var pivot = B.transform.position;
        var pos = roomRoot.position;

        // Aplica rotação em torno do pivot
        roomRoot.rotation = rotDelta * roomRoot.rotation;
        roomRoot.position = pivot + rotDelta * (pos - pivot);

        // Agora tenta pequeno deslocamento para coincidir posições
        var delta = A.transform.position - B.transform.position;
        if (delta.magnitude > maxSnap) return false; // não move muito
        roomRoot.position += delta;
        return true;
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

    // ---- Helpers de grade/ocupação ----
    HashSet<Vector2Int> BuildRoomOccupancy(IEnumerable<RectInt> rects) {
        var occ = new HashSet<Vector2Int>();
        foreach (var r in rects) {
            for (int y = r.y; y < r.yMax; y++)
                for (int x = r.x; x < r.xMax; x++)
                    occ.Add(new Vector2Int(x, y));
        }
        return occ;
    }

    IEnumerable<Vector2Int> BorderCellsOutside(RectInt r) {
        // Uma volta em torno do retângulo, do lado de fora
        // Horizontal acima e abaixo
        for (int x = r.x; x < r.xMax; x++) {
            yield return new Vector2Int(x, r.y - 1);
            yield return new Vector2Int(x, r.yMax);
        }
        // Vertical esquerda e direita
        for (int y = r.y; y < r.yMax; y++) {
            yield return new Vector2Int(r.x - 1, y);
            yield return new Vector2Int(r.xMax, y);
        }
    }

    bool RoomsTouching(GameObject a, GameObject b) {
        if (!_rectByGo.TryGetValue(a, out var ra) || !_rectByGo.TryGetValue(b, out var rb)) return false;
        return Touching(ra, rb);
    }

    bool Touching(RectInt a, RectInt b) {
        bool xTouch = (a.xMax == b.xMin) || (b.xMax == a.xMin);
        bool yOverlap = !(a.yMax <= b.yMin || b.yMax <= a.yMin);
        bool yTouch = (a.yMax == b.yMin) || (b.yMax == a.yMin);
        bool xOverlap = !(a.xMax <= b.xMin || b.xMax <= a.xMin);
        return (xTouch && yOverlap) || (yTouch && xOverlap);
    }

    // Conveniência: usa um maxSnap padrão baseado no tamanho da célula
    bool AlignSocketPair(DoorSocket A, DoorSocket B)
    {
        float maxSnap = Mathf.Min(Program.CellSizeMeters.x, Program.CellSizeMeters.y) * 0.75f;
        return AlignSocketPair(A, B, maxSnap);
    }


    List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal, HashSet<Vector2Int> blocked) {
        if (start == goal) return new List<Vector2Int> { start };

        var q = new Queue<Vector2Int>();
        var prev = new Dictionary<Vector2Int, Vector2Int>();
        var seen = new HashSet<Vector2Int>();
        q.Enqueue(start);
        seen.Add(start);

        // Limite de iterações para evitar loops patológicos
        int iter = 0, iterMax = 50000;
        var dirs = new Vector2Int[] { new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1) };
        while (q.Count > 0 && iter++ < iterMax) {
            var cur = q.Dequeue();
            if (cur == goal) break;
            foreach (var d in dirs) {
                var nx = cur + d;
                if (seen.Contains(nx)) continue;
                if (blocked.Contains(nx)) continue; // não atravessa salas
                seen.Add(nx);
                prev[nx] = cur;
                q.Enqueue(nx);
            }
        }

        if (!prev.ContainsKey(goal) && start != goal) return null;

        var path = new List<Vector2Int>();
        var p = goal;
        path.Add(p);
        while (prev.ContainsKey(p)) { p = prev[p]; path.Add(p); }
        path.Reverse();
        return path;
    }
}
