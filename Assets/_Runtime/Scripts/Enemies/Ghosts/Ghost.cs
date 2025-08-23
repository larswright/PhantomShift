using UnityEngine;
using UnityEngine.AI;
using Mirror;
using System.Collections;

[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(NavMeshAgent))]
public class Ghost : NetworkBehaviour
{
    [SerializeField] private GhostArchetype archetype;

    private NavMeshAgent agent;
    private bool externalControl;
    private float baseSpeed;
    private int zigSign = 1;

    public override void OnStartServer()
    {
        base.OnStartServer();
        agent = GetComponent<NavMeshAgent>();
        ApplyConfig();
        baseSpeed = agent.speed;
        // Escolhe o loop conforme o archetype
        if (archetype && archetype.erratic)
            StartCoroutine(ErraticLoop());
        else
            StartCoroutine(WanderLoop());
    }

    void ApplyConfig()
    {
        if (!agent || archetype == null) return;
        var s = archetype.defaultStats;
        agent.speed = s.moveSpeed;
        agent.acceleration = s.acceleration;
        agent.angularSpeed = s.angularSpeed;
        agent.stoppingDistance = s.stoppingDistance;
        agent.areaMask = archetype.areaMask;
        agent.autoRepath = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
    }

    [Server]
    public void ServerSetExternalControl(bool enable)
    {
        externalControl = enable;
        if (enable)
        {
            var a = GetComponent<NavMeshAgent>();
            if (a)
            {
                a.ResetPath();
                a.velocity = Vector3.zero;
            }
        }
    }

    [ServerCallback]
    private IEnumerator ErraticLoop()
    {
        // Fallbacks se archetype não setado
        float zigStep = archetype ? archetype.zigStep : 2.0f;
        float zigAmp  = archetype ? archetype.zigAmplitude : 2.0f;
        Vector2 period = archetype ? archetype.zigPeriodRange : new Vector2(0.2f, 0.45f);
        float hardTurnChance = archetype ? archetype.hardTurnChance : 0.35f;
        float hardTurnDeg    = archetype ? archetype.hardTurnDegrees : 90f;
        float burstChance    = archetype ? archetype.burstChance : 0.25f;
        float burstMul       = archetype ? archetype.burstMultiplier : 1.8f;
        Vector2 burstDur     = archetype ? archetype.burstDurationRange : new Vector2(0.25f, 0.6f);

        float sampleMax = archetype ? archetype.sampleMaxDistance : 2f;
        float radius    = archetype ? archetype.wanderRadius : 10f;

        while (true)
        {
            if (externalControl)
            {
                yield return null;
                continue;
            }

            // Direção base: usa velocidade atual ou forward
            Vector3 fwd = (agent && agent.velocity.sqrMagnitude > 0.1f)
                ? agent.velocity.normalized
                : transform.forward;

            // Lateral direita (perpendicular no plano XZ)
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

            // Alterna lado a cada decisão (zig-zag)
            zigSign = -zigSign;

            // Offset lateral com amplitude variável
            float lateral = Random.Range(0.3f * zigAmp, zigAmp) * zigSign;

            // Chance de virar "seco" (hard turn)
            if (Random.value < hardTurnChance)
            {
                float signed = (Random.value < 0.5f ? -1f : 1f) * hardTurnDeg;
                fwd = Quaternion.Euler(0f, signed, 0f) * fwd;
            }

            // Alvo curto à frente + deslocamento lateral
            Vector3 target = transform.position + (fwd * zigStep) + (right * lateral);

            // Projeta no NavMesh e define destino
            if (TryGetRandomPointOnNavmesh(target, radius, out var dest, agent.areaMask, sampleMax))
                agent.SetDestination(dest);

            // Sprint ocasional
            bool didBurst = false;
            if (Random.value < burstChance)
            {
                didBurst = true;
                float dur = Random.Range(burstDur.x, burstDur.y);
                float prev = agent.speed;
                agent.speed = baseSpeed * burstMul;
                float end = Time.time + dur;
                while (Time.time < end)
                {
                    if (externalControl) break;
                    yield return null;
                }
                if (!externalControl)
                    agent.speed = prev;
            }

            // Janela curta entre decisões (mais “nervoso”)
            float wait = Random.Range(period.x, period.y);
            if (!didBurst)
                yield return new WaitForSeconds(wait);
            else
                yield return null; // já “ocupou” tempo no burst
        }
    }

    [ServerCallback]
    private IEnumerator WanderLoop()
    {
        var idleRange = archetype ? archetype.idlePauseRange : new Vector2(0.5f, 1.5f);
        var roamRange = archetype ? archetype.roamIntervalRange : new Vector2(2f, 4f);
        var radius    = archetype ? archetype.wanderRadius      : 10f;
        var sampleMax = archetype ? archetype.sampleMaxDistance : 2f;

        while (true)
        {
            // respeita controle externo
            if (externalControl)
            {
                yield return null;
                continue;
            }

            if (TryGetRandomPointOnNavmesh(transform.position, radius, out var dest, agent.areaMask, sampleMax))
                agent.SetDestination(dest);

            float travelWindow = Random.Range(roamRange.x, roamRange.y);
            float endTime = Time.time + travelWindow;
            while (Time.time < endTime)
            {
                if (externalControl) break;
                yield return null;
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f) break;
            }

            if (externalControl) continue;

            yield return new WaitForSeconds(Random.Range(idleRange.x, idleRange.y));
        }
    }

    public static bool TryGetRandomPointOnNavmesh(Vector3 center, float radius, out Vector3 result, int areaMask, float maxSampleDistance)
    {
        for (int i = 0; i < 10; i++)
        {
            var random = Random.insideUnitSphere * radius;
            random.y = 0f;
            var candidate = center + random;
            if (NavMesh.SamplePosition(candidate, out var hit, maxSampleDistance, areaMask))
            {
                result = hit.position;
                return true;
            }
        }
        result = center;
        return false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Autoridade de movimento somente no servidor:
        if (!isServer)
        {
            var a = GetComponent<NavMeshAgent>();
            if (a) a.enabled = false;
        }
    }
}
