using UnityEngine;
using System.Linq;

public static class PropPlacer {
    public static void Populate(Transform root, int seed) {
        var rng = Rng.Get(seed);
        if (root == null) return;
        var anchors = root.GetComponentsInChildren<PropAnchor>(true);
        foreach (var a in anchors) {
            // Exemplo simples: ativa 50–80% dos props filhos
            var children = a.GetComponentsInChildren<Transform>(true).Where(t => t != a.transform).ToArray();
            foreach (var c in children) {
                if (rng.NextDouble() < 0.65) c.gameObject.SetActive(true);
                else c.gameObject.SetActive(false);
            }
        }
    }
}

