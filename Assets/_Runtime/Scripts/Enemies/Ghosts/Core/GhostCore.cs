using System.Collections.Generic;
using Mirror;
using UnityEngine;

[RequireComponent(typeof(NetworkIdentity))]
public class GhostCore : NetworkBehaviour {
    [Header("Definition")]
    public GhostDefinition def; // SO com Archetype + Traits

    private readonly List<IGhostModule> modules = new List<IGhostModule>();
    private readonly List<IAbility> abilities = new List<IAbility>();
    [SerializeField] private float tickBudget = 0.05f; // alvo 20 Hz por LOD

    private bool scheduledByAIScheduler;

    public GhostArchetype Archetype => def ? def.baseArchetype : null;

    public override void OnStartServer() {
        base.OnStartServer();

        // Coleta módulos (componentes) que implementem IGhostModule
        foreach (var mb in GetComponents<MonoBehaviour>()) {
            if (mb is IGhostModule m) modules.Add(m);
        }

        // Instancia abilities a partir de Traits do Definition
        if (def && def.traits != null) {
            foreach (var t in def.traits) {
                if (!t) continue;
                var ability = t.Instantiate();
                if (ability != null) abilities.Add(ability);
            }
        }

        // Inicializa em servidor
        foreach (var m in modules) m.ServerInit(this);
        foreach (var a in abilities) a.ServerInit(this);

        // Tenta registrar no AIScheduler se existir na cena
        var sched = Object.FindObjectOfType<AIScheduler>();
        if (sched) {
            sched.Register(this);
            scheduledByAIScheduler = true;
        }
    }

    void Update() {
        if (!isServer) return;
        if (scheduledByAIScheduler) return; // AIScheduler chamará ServerTickForAll()

        float dt = Time.deltaTime;
        ServerTickForAll(dt);
    }

    // Chamado pelo AIScheduler
    public void ServerTickForAll(float dt) {
        foreach (var m in modules) m.ServerTick(dt);
        foreach (var a in abilities) a.ServerTick(dt);
    }
}

