using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;            // NavMesh, NavMeshAgent
using Unity.AI.Navigation;       // NavMeshSurface (package: com.unity.ai.navigation)

public class LayoutValidator {
    public bool ValidateConnectivity(Dictionary<int, GameObject> nodes, Transform root) {
        // BFS “grosseiro”: usa proximidade dos colliders/centros
        var ids = new List<int>(nodes.Keys);
        if (ids.Count == 0) return false;

        var visited = new HashSet<int>();
        var q = new Queue<int>();
        q.Enqueue(ids[0]); visited.Add(ids[0]);

        while (q.Count > 0) {
            var cur = q.Dequeue();
            var p = nodes[cur].transform.position;
            for (int i = 0; i < ids.Count; i++) {
                int other = ids[i];
                if (visited.Contains(other) || other == cur) continue;
                var d = Vector3.Distance(p, nodes[other].transform.position);
                if (d < 6f) { visited.Add(other); q.Enqueue(other); }
            }
        }

        bool allReachable = visited.Count == ids.Count;
        if (!allReachable) Debug.LogWarning("Nem todos os cômodos parecem conectados.");

        // NavMesh islands
        if (root != null) {
            var samplers = root.GetComponentsInChildren<NavMeshSurface>();
            foreach (var s in samplers) if (s != null) s.BuildNavMesh();
        }

        return allReachable;
    }
}

