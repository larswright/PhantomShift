using UnityEngine;
using UnityEngine.AI;

[CreateAssetMenu(menuName="PhantomShift/Trait/PhaseWalls")]
public class Trait_PhaseWalls : GhostTraitSO {
    public float phaseDuration = 1.5f;
    public float cooldown = 8f;

    public override IAbility Instantiate() => new Impl(this);

    class Impl : IAbility {
        private readonly Trait_PhaseWalls data;
        private GhostCore core;
        private NavMeshAgent agent;
        private float cd;
        private float t;
        private bool active;

        public Impl(Trait_PhaseWalls d) { data = d; }

        public void ServerInit(GhostCore c) {
            core = c;
            agent = c.GetComponent<NavMeshAgent>();
            cd = Random.Range(0f, data.cooldown); // desincroniza instâncias
        }

        public void ServerTick(float dt) {
            if (!agent) return;
            if (active) {
                t -= dt;
                if (t <= 0f) {
                    active = false;
                    agent.avoidancePriority = 50;
                    agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                }
            }
            else {
                cd -= dt;
                if (cd <= 0f) {
                    active = true;
                    t = data.phaseDuration;
                    cd = data.cooldown;
                    agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                }
            }
        }

        public void OnPulseStart() { }
        public void OnStunStart() { }
        public void OnStunEnd() { }
        public void OnCaptureStart() { }
        public void OnCaptureEnd(bool success) { }
    }
}

