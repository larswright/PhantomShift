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

        // 3) Expande com backtracking até TargetRooms
        var open = new List<int> { foyerNode.Id };
        int nextId = 1;
        int loops = 0;

        while (g.Nodes.Count < program.TargetRooms && open.Count > 0) {
            var attachFrom = open[rng.Next(open.Count)];
            var parent = g.Nodes.First(n => n.Id == attachFrom);
            
            // Escolhe próximo tipo respeitando mínimos -> pesos
            string pickId = PickArchetypeId(dict.Values, need, rng);
            if (pickId == null) break;

            var a = dict[pickId];
            var node = new RoomNode {
                Id = nextId++,
                ArchetypeId = pickId,
                W = rng.Next(a.SizeMin.x, a.SizeMax.x + 1),
                H = rng.Next(a.SizeMin.y, a.SizeMax.y + 1),
            };
            g.Nodes.Add(node);

            // Decide se conecta por porta direta ou corredor
            var edge = new RoomEdge {
                A = parent.Id, B = node.Id,
                Kind = (a.Id == "Bedroom" || a.Id == "Bathroom") ? EdgeKind.Door : 
                       (rng.NextDouble() < 0.5 ? EdgeKind.Door : EdgeKind.Corridor)
            };
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
                    g.Edges.Add(new RoomEdge { A = node.Id, B = other.Id, Kind = EdgeKind.Corridor });
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
                        W = Random.Range(a.SizeMin.x, a.SizeMax.x + 1),
                        H = Random.Range(a.SizeMin.y, a.SizeMax.y + 1) };
                    g.Nodes.Add(node);
                    g.Edges.Add(new RoomEdge { A = foyerNode.Id, B = node.Id, Kind = EdgeKind.Door });
                }
            }
        }

        return g;
    }

    static string PickArchetypeId(IEnumerable<RoomArchetype> all, Dictionary<string,int> need, System.Random rng) {
        // Prioriza mínimos pendentes
        if (need.Count > 0) {
            var pickNeeded = need.Keys.ToArray();
            return pickNeeded[rng.Next(pickNeeded.Length)];
        }
        // Peso simples
        var list = all.Where(a => a.MaxCount > 0).ToArray();
        float sum = list.Sum(a => a.Weight);
        if (sum <= 0f) return list[rng.Next(list.Length)].Id;
        float t = (float)rng.NextDouble() * sum;
        float acc = 0f;
        foreach (var a in list) {
            acc += a.Weight;
            if (t <= acc) return a.Id;
        }
        return list.Last().Id;
    }

    static bool HasEdge(RoomGraph g, int a, int b) {
        return g.Edges.Any(e => (e.A == a && e.B == b) || (e.A == b && e.B == a));
    }
}

