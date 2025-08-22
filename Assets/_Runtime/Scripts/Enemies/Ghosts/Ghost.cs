using UnityEngine;
using UnityEngine.AI;
using Mirror;
using System.Collections;

[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NetworkTransform))]
public class Ghost : NetworkBehaviour
{
    [SerializeField] private GhostArchetype archetype;

    private NavMeshAgent agent;

    public override void OnStartServer()
    {
        base.OnStartServer();
        agent = GetComponent<NavMeshAgent>();
        ApplyConfig();
        StartCoroutine(WanderLoop());
    }

    void ApplyConfig()
    {
        if (!agent || archetype == null) return;
        agent.speed = archetype.moveSpeed;
        agent.acceleration = archetype.acceleration;
        agent.angularSpeed = archetype.angularSpeed;
        agent.stoppingDistance = archetype.stoppingDistance;
        agent.areaMask = archetype.areaMask;
        agent.autoRepath = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
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
            if (TryGetRandomPointOnNavmesh(transform.position, radius, out var dest, agent.areaMask, sampleMax))
                agent.SetDestination(dest);

            // Janela de movimento
            float travelWindow = Random.Range(roamRange.x, roamRange.y);
            float endTime = Time.time + travelWindow;
            while (Time.time < endTime)
            {
                yield return null;
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f) break;
            }

            // Pausa curta
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
