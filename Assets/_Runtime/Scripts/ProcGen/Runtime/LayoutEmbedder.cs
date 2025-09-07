using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PlacedRoom {
    public RoomNode Node;
    public RectInt Rect; // em células (x,y,w,h)
}

public class Layout {
    public Dictionary<int, PlacedRoom> Rooms = new Dictionary<int, PlacedRoom>();
    public List<(int A, int B)> Doors = new List<(int A, int B)>();
    public List<(int A, int B)> Corridors = new List<(int A, int B)>();
    public Vector2 CellMeters;
}

public static class LayoutEmbedder {
    public static Layout Embed(RoomGraph g, HouseProgram program, int seed, int maxIter = 2000) {
        var rng = Rng.Get(seed);
        var lay = new Layout { CellMeters = program.CellSizeMeters };

        // 1) Ordena por grau (começa pelos maiores/centrais)
        var order = g.Nodes.OrderByDescending(n => Degree(g, n.Id)).ToList();
        var placedRects = new List<RectInt>();
        int x = 0, y = 0;

        foreach (var node in order) {
            // posicionamento greedy em “prateleiras” (shelf packing)
            var rect = new RectInt(x, y, node.W, node.H);
            int tries = 0;
            while (IntersectsAny(rect, placedRects) && tries < 128) {
                x += 1;
                if (x > 40) { x = 0; y += 1; }
                rect = new RectInt(x, y, node.W, node.H);
                tries++;
            }
            placedRects.Add(rect);
            lay.Rooms[node.Id] = new PlacedRoom { Node = node, Rect = rect };
        }

        // 2) Conecta arestas, separando tipos
        foreach (var e in g.Edges) {
            if (e.A == e.B) continue;
            if (e.A < 0 || e.B < 0) continue;
            if (e.Kind == EdgeKind.Door) lay.Doors.Add((e.A, e.B));
            else lay.Corridors.Add((e.A, e.B));
        }

        // 3) Refinamento barato: leve "annealing" por swaps aleatórios que reduzem custo
        float best = Cost(lay);
        for (int it = 0; it < maxIter; it++) {
            var a = order[rng.Next(order.Count)];
            var b = order[rng.Next(order.Count)];
            if (a.Id == b.Id) continue;

            // tenta swap
            var ra = lay.Rooms[a.Id].Rect; var rb = lay.Rooms[b.Id].Rect;
            lay.Rooms[a.Id].Rect = rb; lay.Rooms[b.Id].Rect = ra;
            float c = Cost(lay);
            if (c <= best) { best = c; } 
            else { // reverte
                lay.Rooms[a.Id].Rect = ra; lay.Rooms[b.Id].Rect = rb;
            }
        }

        // 4) Encosta pares com porta (snap)
        SnapDoors(lay);

        return lay;
    }

    static int Degree(RoomGraph g, int id) => g.Edges.Count(e => e.A == id || e.B == id);

    static bool IntersectsAny(RectInt r, List<RectInt> list) {
        foreach (var o in list) if (r.Overlaps(o)) return true;
        return false;
    }

    static float Cost(Layout lay) {
        // Custo = soma dos comprimentos de arestas + penalidade por sobreposição (já evitada) + desalinhamento
        float c = 0f;
        // portas/corredores: distância Manhattan entre centros
        foreach (var d in lay.Doors) {
            c += Dist(lay.Rooms[d.A].Rect, lay.Rooms[d.B].Rect);
        }
        foreach (var co in lay.Corridors) {
            c += 1.2f * Dist(lay.Rooms[co.A].Rect, lay.Rooms[co.B].Rect); // corredor mais caro
        }
        return c;
    }

    static float Dist(RectInt a, RectInt b) {
        Vector2 ac = new Vector2(a.x + a.width * 0.5f, a.y + a.height * 0.5f);
        Vector2 bc = new Vector2(b.x + b.width * 0.5f, b.y + b.height * 0.5f);
        return Mathf.Abs(ac.x - bc.x) + Mathf.Abs(ac.y - bc.y);
    }

    public static void SnapDoors(Layout lay, int maxPasses = 64) {
        int passes = 0;
        while (passes++ < maxPasses) {
            bool movedAny = false;
            foreach (var link in lay.Doors.ToArray()) {
                var ra = lay.Rooms[link.A].Rect;
                var rb = lay.Rooms[link.B].Rect;
                if (ra.Overlaps(rb) || Touching(ra, rb)) continue;

                int dx = 0, dy = 0;
                if (ra.xMax < rb.xMin) dx = ra.xMax - rb.xMin; else if (rb.xMax < ra.xMin) dx = ra.xMin - rb.xMax;
                if (ra.yMax < rb.yMin) dy = ra.yMax - rb.yMin; else if (rb.yMax < ra.yMin) dy = ra.yMin - rb.yMax;

                // Escolhe menor deslocamento absoluto, mantendo outro eixo quando possível
                if (dx != 0 && (dy == 0 || Mathf.Abs(dx) <= Mathf.Abs(dy))) {
                    rb.position += new Vector2Int(dx, 0);
                    movedAny = true;
                } else if (dy != 0) {
                    rb.position += new Vector2Int(0, dy);
                    movedAny = true;
                }

                if (movedAny) lay.Rooms[link.B].Rect = rb;
            }
            if (!movedAny) break;
        }
    }

    static bool Touching(RectInt a, RectInt b) {
        bool xTouch = (a.xMax == b.xMin) || (b.xMax == a.xMin);
        bool yOverlap = !(a.yMax <= b.yMin || b.yMax <= a.yMin);
        bool yTouch = (a.yMax == b.yMin) || (b.yMax == a.yMin);
        bool xOverlap = !(a.xMax <= b.xMin || b.xMax <= a.xMin);
        return (xTouch && yOverlap) || (yTouch && xOverlap);
    }
}
