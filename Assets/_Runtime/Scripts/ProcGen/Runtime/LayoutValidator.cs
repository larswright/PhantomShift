using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;            // NavMesh, NavMeshAgent
using Unity.AI.Navigation;       // NavMeshSurface (package: com.unity.ai.navigation)

public class LayoutValidator {
    public bool ValidateConnectivity(Dictionary<int, GameObject> nodes, Transform root) {
        if (nodes == null || nodes.Count == 0) return false;

        // Build/refresh NavMesh on provided surfaces
        if (root != null) {
            var samplers = root.GetComponentsInChildren<NavMeshSurface>();
            foreach (var s in samplers) if (s != null) s.BuildNavMesh();
        }

        var startGo = nodes.Values.FirstOrDefault();
        if (startGo == null) return false;

        int reachable = 0;
        foreach (var kv in nodes) {
            var from = startGo.transform.position;
            var to = kv.Value.transform.position;
            if (NavMesh.SamplePosition(from, out var h1, 2f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(to, out var h2, 2f, NavMesh.AllAreas)) {
                var path = new NavMeshPath();
                NavMesh.CalculatePath(h1.position, h2.position, NavMesh.AllAreas, path);
                if (path.status == NavMeshPathStatus.PathComplete) reachable++;
            }
        }

        bool ok = (reachable == nodes.Count);
        if (!ok) Debug.LogWarning($"Conectividade falhou: {reachable}/{nodes.Count} alcançáveis via NavMesh.");
        return ok;
    }
}
