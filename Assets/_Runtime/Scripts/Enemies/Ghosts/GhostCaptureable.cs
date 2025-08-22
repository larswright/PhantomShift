using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(Ghost))]
[RequireComponent(typeof(NavMeshAgent))]
public class GhostCaptureable : NetworkBehaviour
{
    [SerializeField] private GhostArchetype archetype;

    [Header("Capture (UV/Stun)")]
    public float uvSecondsToStun = 2.0f;
    public float stunSeconds = 3.0f;
    public float exposureGraceWindow = 0.6f;
    public float fleeDecisionInterval = 0.25f;
    public Vector2 fleeStepRange = new Vector2(1.5f, 3.5f);
    public float fleeRadius = 10f;

    [System.Serializable] public class FloatEvent : UnityEvent<float> {}
    public UnityEvent onFleeStart;
    public UnityEvent onFleeEnd;
    public UnityEvent onStunStart;
    public UnityEvent onStunEnd;
    public FloatEvent onEscapeIntensity;
    public FloatEvent onStunIntensity;

    [SyncVar(hook = nameof(OnFleeState))]  private bool fleeing;
    [SyncVar(hook = nameof(OnStunState))]  private bool stunned;
    [SyncVar(hook = nameof(OnEscapeIntensity))] private float escapeIntensity;
    [SyncVar(hook = nameof(OnStunIntensity))]   private float stunIntensity;

    private Ghost ghost;
    private NavMeshAgent agent;
    private Coroutine fleeCo;
    private Coroutine stunCo;
    private float exposureTimer;
    private float lastUVTime;
    private Vector3 lastOrigin;
    private Vector3 lastRayDir; // direção do feixe UV (XZ)

    public override void OnStartServer()
    {
        ghost = GetComponent<Ghost>();
        agent = GetComponent<NavMeshAgent>();
        if (!ghost) Debug.LogWarning("[GhostCaptureable] Ghost component missing", this);
        if (!agent) Debug.LogWarning("[GhostCaptureable] NavMeshAgent missing", this);
        ApplyConfig();
        if (archetype) ApplyMotionStats(archetype.defaultStats, false);
    }

    void ApplyConfig()
    {
        if (!archetype) return;
        uvSecondsToStun = archetype.capture_uvSecondsToStun;
        stunSeconds = archetype.capture_stunSeconds;
        exposureGraceWindow = archetype.capture_exposureGraceWindow;
        fleeDecisionInterval = archetype.capture_fleeDecisionInterval;
        fleeStepRange = archetype.capture_fleeStepRange;
        fleeRadius = archetype.capture_fleeRadius;
    }

    void ApplyMotionStats(GhostArchetype.MotionStats stats, bool flee)
    {
        if (!agent) return;
        agent.speed = stats.moveSpeed;
        agent.acceleration = stats.acceleration;
        agent.angularSpeed = stats.angularSpeed;
        agent.stoppingDistance = stats.stoppingDistance;
        agent.autoBraking = !flee;
        agent.obstacleAvoidanceType = flee
            ? ObstacleAvoidanceType.GoodQualityObstacleAvoidance
            : ObstacleAvoidanceType.HighQualityObstacleAvoidance;
    }

    [Server]
    public void ServerApplyUVHit(Vector3 origin, Vector3 dir, float dt)
    {
        float now = Time.time;
        if (now - lastUVTime > exposureGraceWindow)
            exposureTimer = 0f;

        lastUVTime = now;
        exposureTimer += dt;

        lastOrigin = origin;
        lastRayDir  = new Vector3(dir.x, 0f, dir.z).normalized; // guarda a direção do feixe no plano

        Debug.Log($"[GhostCaptureable] {name} recebeu UV: {exposureTimer:F2}/{uvSecondsToStun:F2}s");

        if (!stunned)
        {
            if (exposureTimer >= uvSecondsToStun) StartStun();
            else StartFlee();
        }
    }

    [ServerCallback]
    void Update()
    {
        if (!stunned && Time.time - lastUVTime > exposureGraceWindow)
        {
            if (exposureTimer > 0f)
                Debug.Log($"[GhostCaptureable] {name} perdeu exposição de UV");
            exposureTimer = 0f;
            StopFlee();
        }
    }

    [Server]
    void StartFlee()
    {
        if (fleeing || stunned) return;
        fleeing = true;
        ghost.ServerSetExternalControl(true);
        if (archetype) ApplyMotionStats(archetype.fleeStats, true);
        fleeCo = StartCoroutine(FleeLoop());
    }

    [Server]
    void StopFlee()
    {
        if (!fleeing) return;
        fleeing = false;
        ghost.ServerSetExternalControl(false);
        if (archetype) ApplyMotionStats(archetype.defaultStats, false);
        if (fleeCo != null) StopCoroutine(fleeCo);
        fleeCo = null;
    }

    [Server]
    IEnumerator FleeLoop()
    {
        while (fleeing && !stunned)
        {
            // Se não recebeu direção ainda, usa forward só para evitar zero.
            Vector3 d = lastRayDir.sqrMagnitude > 0.0001f ? lastRayDir : transform.forward;

            // Vetor lateral (direita do feixe) no plano XZ; esquerda = -right
            Vector3 right = new Vector3(d.z, 0f, -d.x);

            // Fantasma está à direita (+) ou à esquerda (-) do eixo do feixe?
            Vector3 v = transform.position - lastOrigin;
            float side = Vector3.Dot(v, right);

            // Escolhe o lado que aumenta |distância lateral| (afasta do eixo do feixe)
            Vector3 lateral = side >= 0f ? right : -right;

            // Ruído apenas lateral
            Vector3 rnd = Random.insideUnitSphere; rnd.y = 0f;
            rnd -= Vector3.Project(rnd, d);
            rnd = Vector3.ClampMagnitude(rnd, 0.35f);

            float step = Random.Range(fleeStepRange.x, fleeStepRange.y);
            Vector3 target = transform.position + (lateral + rnd).normalized * step;

            if (Ghost.TryGetRandomPointOnNavmesh(target, fleeRadius, out var dest, agent.areaMask, 2f))
                agent.SetDestination(dest);

            // Intensidade linear 0..1 pela fração de exposição
            escapeIntensity = Mathf.Clamp01(exposureTimer / Mathf.Max(0.01f, uvSecondsToStun));

            yield return new WaitForSeconds(fleeDecisionInterval);
        }
    }

    [Server]
    void StartStun()
    {
        if (stunned) return;
        stunned = true;
        fleeing = false;
        ghost.ServerSetExternalControl(true);
        if (fleeCo != null) StopCoroutine(fleeCo);
        fleeCo = null;
        if (archetype) ApplyMotionStats(archetype.defaultStats, false);
        stunCo = StartCoroutine(StunLoop());
    }

    [Server]
    IEnumerator StunLoop()
    {
        float end = Time.time + stunSeconds;
        while (Time.time < end)
        {
            float t = 1f - ((end - Time.time) / Mathf.Max(0.01f, stunSeconds)); // 0..1 linear
            stunIntensity = Mathf.Clamp01(t);
            yield return null;
        }
        stunned = false;
        ghost.ServerSetExternalControl(false);
        stunCo = null;
        exposureTimer = 0f;
        // volta para os stats padrão ao sair do stun
        if (archetype) ApplyMotionStats(archetype.defaultStats, false);
    }

    void OnFleeState(bool _, bool newVal)
    {
        if (newVal) onFleeStart?.Invoke();
        else onFleeEnd?.Invoke();
    }

    void OnStunState(bool _, bool newVal)
    {
        if (newVal) onStunStart?.Invoke();
        else onStunEnd?.Invoke();
    }

    void OnEscapeIntensity(float _, float newVal) => onEscapeIntensity?.Invoke(newVal);
    void OnStunIntensity(float _, float newVal)   => onStunIntensity?.Invoke(newVal);

    // ====== ADIÇÕES SIMPLES (colar dentro de GhostCaptureable) ======

    // Getters simples para outras classes
    [Server] public bool IsStunned() => stunned;
    public GhostArchetype GetArchetype() => archetype;

    // Força sair do stun IMEDIATAMENTE, mantendo o fantasma travado.
    // Útil quando começa a sucção: "zera o stun, mas mantenha parado".
    [Server]
    public void ServerForceExitStunKeepFrozen()
    {
        if (!stunned) return;

        if (stunCo != null)
        {
            StopCoroutine(stunCo);
            stunCo = null;
        }

        stunned = false;                       // dispara hook onStunEnd nos clientes
        ghost.ServerSetExternalControl(true);  // permanece parado
        if (archetype) ApplyMotionStats(archetype.defaultStats, false);
        // zera acúmulo de exposição
        exposureTimer = 0f;
    }
}
