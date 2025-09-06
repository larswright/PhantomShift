using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scheduler simples que fatia atualizações de IAs em "batches" por frame.
/// Registre GhostCore e ele chamará ServerTickForAll() em fases.
/// </summary>
public class AIScheduler : MonoBehaviour {
    [Range(1,8)] public int slices = 4; // fatias por frame
    private readonly List<GhostCore> cores = new List<GhostCore>();
    private int currentSlice;

    public void Register(GhostCore core) {
        if (!core) return;
        if (!cores.Contains(core)) cores.Add(core);
    }

    public void Unregister(GhostCore core) {
        if (!core) return;
        cores.Remove(core);
    }

    void Update() {
        if (cores.Count == 0 || slices <= 0) return;
        float dt = Time.deltaTime;

        // distribui em slices: cada frame roda 1 fatia
        int count = cores.Count;
        int perSlice = Mathf.CeilToInt(count / (float)slices);
        int start = currentSlice * perSlice;
        int end = Mathf.Min(start + perSlice, count);

        for (int i = start; i < end; i++) {
            var c = cores[i];
            if (c) c.ServerTickForAll(dt);
        }

        currentSlice = (currentSlice + 1) % Mathf.Max(1, slices);
    }
}

