using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public static class GraphSampler {
    public static RoomGraph Sample(HouseProgram program, int seed) {
        var rng = Rng.Get(seed);
        var dict = program.Archetypes.ToDictionary(a => a.Id, a => a);

        // 1) Começa pelo foyer (obriga existir)
        if (!dict.TryGetValue("Foyer", out var foyer))
            throw new System.Exception("Archetype 'Foyer' obrigatório.");
        
        var g = new RoomGraph();
        var foyerNode = new RoomNode {
            Id = 0, ArchetypeId = foyer.Id,
            W = rng.Next(foyer.SizeMin.x, foyer.SizeMax.x + 1),
            H = rng.Next(foyer.SizeMin.y, foyer.SizeMax.y + 1),
        };
        g.Nodes.Add(foyerNode);

        // 2) Satisfaz mínimos
        var need = new Dictionary<string, int>();
        foreach (var a in program.Archetypes)
            if (a.MinCount > 0) need[a.Id] = a.MinCount - (a.Id == "Foyer" ? 1 : 0);

        // Contagem corrente por tipo (para respeitar MaxCount)
        var current = program.Archetypes.ToDictionary(a => a.Id, a => 0);
        current[foyer.Id] = 1;

        // 3) Expande com backtracking até TargetRooms
        var open = new List<int> { foyerNode.Id };
        int nextId = 1;
        int loops = 0;

        // Orçamento aproximado de corredores
        int corridorCount = 0;
        int corridorLimit = Mathf.Max(0, Mathf.CeilToInt(program.CorridorAreaShare * program.TargetRooms));

        while (g.Nodes.Count < program.TargetRooms && open.Count > 0) {
            var attachFrom = open[rng.Next(open.Count)];
            var parent = g.Nodes.First(n => n.Id == attachFrom);
            
            // Escolhe próximo tipo respeitando mínimos -> pesos
            string pickId = PickArchetypeId(dict.Values, need, current, parent.ArchetypeId, rng);
            if (pickId == null) break;

            var a = dict[pickId];
            var node = new RoomNode {
                Id = nextId++,
                ArchetypeId = pickId,
                W = rng.Next(a.SizeMin.x, a.SizeMax.x + 1),
                H = rng.Next(a.SizeMin.y, a.SizeMax.y + 1),
            };
            g.Nodes.Add(node);
            current[pickId] = current.TryGetValue(pickId, out var c0) ? c0 + 1 : 1;

            // Decide se conecta por porta direta ou corredor
            var edge = new RoomEdge {
                A = parent.Id, B = node.Id,
                Kind = (a.Id == "Bedroom" || a.Id == "Bathroom") ? EdgeKind.Door : 
                       (rng.NextDouble() < 0.5 ? EdgeKind.Door : EdgeKind.Corridor)
            };
            // Aplica teto de corredores
            if (edge.Kind == EdgeKind.Corridor) {
                if (corridorCount >= corridorLimit) edge.Kind = EdgeKind.Door;
                else corridorCount++;
            }
            g.Edges.Add(edge);

            // Atualiza mínimos
            if (need.ContainsKey(pickId)) {
                need[pickId]--;
                if (need[pickId] <= 0) need.Remove(pickId);
            }

            // Conexões extras raras (loops controlados)
            if (loops < program.MaxLoops && g.Nodes.Count > 4 && rng.NextDouble() < 0.2) {
                var other = g.Nodes[rng.Next(g.Nodes.Count)];
                if (other.Id != node.Id && !HasEdge(g, node.Id, other.Id)) {
                    var k = EdgeKind.Corridor;
                    if (corridorCount >= corridorLimit) k = EdgeKind.Door; else corridorCount++;
                    g.Edges.Add(new RoomEdge { A = node.Id, B = other.Id, Kind = k });
                    loops++;
                }
            }

            // Continua expandindo deste novo nó com probabilidade controlada
            if (rng.NextDouble() < 0.7) open.Add(node.Id);
            // redução ocasional para evitar árvore muito “bushy”
            if (open.Count > 3 && rng.NextDouble() < 0.3) open.RemoveAt(rng.Next(open.Count));
        }

        // Verificação final dos mínimos
        if (need.Count > 0) {
            // fallback: injeta nós faltantes conectados ao foyer
            foreach (var kv in need.ToArray()) {
                var a = dict[kv.Key];
                for (int i = 0; i < kv.Value; i++) {
                    var node = new RoomNode { Id = nextId++, ArchetypeId = a.Id,
                        W = NextInclusive(rng, a.SizeMin.x, a.SizeMax.x),
                        H = NextInclusive(rng, a.SizeMin.y, a.SizeMax.y) };
                    g.Nodes.Add(node);
                    g.Edges.Add(new RoomEdge { A = foyerNode.Id, B = node.Id, Kind = EdgeKind.Door });
                    current[a.Id] = current.TryGetValue(a.Id, out var c1) ? c1 + 1 : 1;
                }
            }
        }

        // Reparo simples de adjacências obrigatórias
        RepairAdjacency(g, dict);

        return g;
    }

    static int NextInclusive(System.Random rng, int min, int maxInc) => rng.Next(min, maxInc + 1);

    static string PickArchetypeId(IEnumerable<RoomArchetype> all, Dictionary<string,int> need, Dictionary<string,int> current, string parentArchetypeId, System.Random rng) {
        // Prioriza mínimos pendentes
        if (need.Count > 0) {
            var pickNeeded = need.Keys.ToArray();
            return pickNeeded[rng.Next(pickNeeded.Length)];
        }
        // Peso simples
        var list = all.Where(a => a.MaxCount > 0 && current.TryGetValue(a.Id, out var c) ? c < a.MaxCount : true).ToArray();
        if (list.Length == 0) return null;

        // Ajuste leve por preferência de adjacência ao pai
        float Adjust(RoomArchetype a) {
            float w = Mathf.Max(0.0001f, a.Weight);
            if (!string.IsNullOrEmpty(parentArchetypeId) && a.PreferAdjacentTo != null && a.PreferAdjacentTo.Contains(parentArchetypeId))
                w *= 1.3f;
            return w;
        }

        float sum = list.Sum(a => Adjust(a));
        if (sum <= 0f) return list[rng.Next(list.Length)].Id;
        float t = (float)rng.NextDouble() * sum;
        float acc = 0f;
        foreach (var a in list) {
            acc += Adjust(a);
            if (t <= acc) return a.Id;
        }
        return list.Last().Id;
    }

    static bool HasEdge(RoomGraph g, int a, int b) {
        return g.Edges.Any(e => (e.A == a && e.B == b) || (e.A == b && e.B == a));
    }

    static void RepairAdjacency(RoomGraph g, Dictionary<string,RoomArchetype> dict) {
        foreach (var n in g.Nodes) {
            if (!dict.TryGetValue(n.ArchetypeId, out var a)) continue;
            if (a.MustBeAdjacentTo == null || a.MustBeAdjacentTo.Length == 0) continue;

            var neighTypes = g.Edges
                .Where(e => e.A == n.Id || e.B == n.Id)
                .Select(e => (e.A == n.Id ? g.Nodes.First(x => x.Id == e.B) : g.Nodes.First(x => x.Id == e.A)).ArchetypeId)
                .ToHashSet();

            foreach (var needType in a.MustBeAdjacentTo) {
                if (neighTypes.Contains(needType)) continue;
                var target = g.Nodes.FirstOrDefault(x => x.ArchetypeId == needType && x.Id != n.Id);
                if (target != null && !HasEdge(g, n.Id, target.Id)) {
                    g.Edges.Add(new RoomEdge { A = n.Id, B = target.Id, Kind = EdgeKind.Door });
                }
            }
        }
    }
}
