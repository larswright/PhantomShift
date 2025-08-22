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
    public AnimationCurve escapeCurve = AnimationCurve.EaseInOut(0,0,1,1);
    public AnimationCurve stunCurve   = AnimationCurve.EaseInOut(0,1,1,0);

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
    private float baseSpeed, baseAngular, baseAccel;

    public override void OnStartServer()
    {
        ghost = GetComponent<Ghost>();
        agent = GetComponent<NavMeshAgent>();
        if (!ghost) Debug.LogWarning("[GhostCaptureable] Ghost component missing", this);
        if (!agent) Debug.LogWarning("[GhostCaptureable] NavMeshAgent missing", this);
        ApplyConfig();
        baseSpeed = agent.speed;
        baseAngular = agent.angularSpeed;
        baseAccel = agent.acceleration;
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
        escapeCurve = archetype.capture_escapeCurve;
        stunCurve = archetype.capture_stunCurve;
    }

    void SetFleeBoost(bool on)
    {
        if (!agent) return;
        if (on)
        {
            // velocidade e resposta agressivas
            agent.speed = baseSpeed * (archetype ? Mathf.Max(1f, archetype.capture_fleeSpeedMultiplier) : 2.2f);
            agent.angularSpeed = Mathf.Max(baseAngular, 900f);
            agent.acceleration = Mathf.Max(baseAccel, 40f);
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance; // menos suavização que High
        }
        else
        {
            agent.speed = baseSpeed;
            agent.angularSpeed = baseAngular;
            agent.acceleration = baseAccel;
            agent.autoBraking = true;
            // restaura o padrão do Ghost.ApplyConfig()
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        }
    }

    [Server]
    public void ServerApplyUVHit(Vector3 origin, float dt)
    {
        float now = Time.time;
        if (now - lastUVTime > exposureGraceWindow)
            exposureTimer = 0f;

        lastUVTime = now;
        exposureTimer += dt;
        lastOrigin = origin;
        Debug.Log($"[GhostCaptureable] {name} recebeu UV: {exposureTimer:F2}/{uvSecondsToStun:F2}s");

        if (!stunned)
        {
            if (exposureTimer >= uvSecondsToStun)
            {
                StartStun();
            }
            else
            {
                StartFlee();
            }
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
        Debug.Log($"[GhostCaptureable] {name} fugindo da UV");
        ghost.ServerSetExternalControl(true);
        SetFleeBoost(true);
        fleeCo = StartCoroutine(FleeLoop());
    }

    [Server]
    void StopFlee()
    {
        if (!fleeing) return;
        fleeing = false;
        ghost.ServerSetExternalControl(false);
        SetFleeBoost(false);
        if (fleeCo != null) StopCoroutine(fleeCo);
        fleeCo = null;
    }

    [Server]
    IEnumerator FleeLoop()
    {
        float segMin = archetype ? archetype.capture_fleeSegmentDuration.x : 0.8f;
        float segMax = archetype ? archetype.capture_fleeSegmentDuration.y : 1.4f;
        float turnChance = archetype ? archetype.capture_hardTurnChance : 0.40f;
        Vector2 turnDeg = archetype ? archetype.capture_hardTurnDegrees : new Vector2(60f, 140f);

        while (fleeing && !stunned)
        {
            // direção base: afastar do UV
            Vector3 dir = (transform.position - lastOrigin).normalized;

            // vira seco aleatório para quebrar previsibilidade
            if (Random.value < turnChance)
            {
                float deg = Random.Range(turnDeg.x, turnDeg.y) * (Random.value < 0.5f ? -1f : 1f);
                dir = Quaternion.Euler(0f, deg, 0f) * dir;
            }

            // passo longo + ruído leve (evita “tremido”)
            Vector3 random = Random.insideUnitSphere * 0.35f; random.y = 0f;
            float step = Random.Range(fleeStepRange.x, fleeStepRange.y);  // do Archetype
            Vector3 target = transform.position + (dir + random).normalized * step;

            if (Ghost.TryGetRandomPointOnNavmesh(target, fleeRadius, out var dest, agent.areaMask, 2f))
                agent.SetDestination(dest);  // define e mantém

            // mantém o destino por toda a duração do segmento (sem retarget)
            float end = Time.time + Random.Range(segMin, segMax);
            while (Time.time < end && fleeing && !stunned)
            {
                float exposure01 = Mathf.Clamp01(exposureTimer / Mathf.Max(0.01f, uvSecondsToStun));
                escapeIntensity = escapeCurve.Evaluate(exposure01);
                // sai cedo se já chegou
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f) break;
                yield return null;
            }
        }
    }

    [Server]
    void StartStun()
    {
        if (stunned) return;
        stunned = true;
        Debug.Log($"[GhostCaptureable] {name} foi atordoado pela UV");
        fleeing = false;
        ghost.ServerSetExternalControl(true);
        SetFleeBoost(false);
        if (fleeCo != null) StopCoroutine(fleeCo);
        fleeCo = null;
        stunCo = StartCoroutine(StunLoop());
    }

    [Server]
    IEnumerator StunLoop()
    {
        float end = Time.time + stunSeconds;
        while (Time.time < end)
        {
            float t = 1f - ((end - Time.time) / Mathf.Max(0.01f, stunSeconds));
            stunIntensity = stunCurve.Evaluate(t);
            yield return null;
        }
        stunned = false;
        ghost.ServerSetExternalControl(false);
        stunCo = null;
        exposureTimer = 0f;
        Debug.Log($"[GhostCaptureable] {name} recuperou do stun");
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
}
