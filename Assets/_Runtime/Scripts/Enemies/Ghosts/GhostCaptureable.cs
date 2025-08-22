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

    public override void OnStartServer()
    {
        ghost = GetComponent<Ghost>();
        agent = GetComponent<NavMeshAgent>();
        if (!ghost) Debug.LogWarning("[GhostCaptureable] Ghost component missing", this);
        if (!agent) Debug.LogWarning("[GhostCaptureable] NavMeshAgent missing", this);
        ApplyConfig();
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
        fleeCo = StartCoroutine(FleeLoop());
    }

    [Server]
    void StopFlee()
    {
        if (!fleeing) return;
        fleeing = false;
        ghost.ServerSetExternalControl(false);
        if (fleeCo != null) StopCoroutine(fleeCo);
        fleeCo = null;
    }

    [Server]
    IEnumerator FleeLoop()
    {
        while (fleeing && !stunned)
        {
            Vector3 away = (transform.position - lastOrigin).normalized;
            Vector3 random = Random.insideUnitSphere; random.y = 0f;
            float step = Random.Range(fleeStepRange.x, fleeStepRange.y);
            Vector3 target = transform.position + (away + random).normalized * step;
            if (Ghost.TryGetRandomPointOnNavmesh(target, fleeRadius, out var dest, agent.areaMask, 2f))
                agent.SetDestination(dest);

            float exposure01 = Mathf.Clamp01(exposureTimer / Mathf.Max(0.01f, uvSecondsToStun));
            escapeIntensity = escapeCurve.Evaluate(exposure01);
            yield return new WaitForSeconds(fleeDecisionInterval);
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
