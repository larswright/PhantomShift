using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine.AI;

public class GhostSpawner : NetworkBehaviour
{
    [System.Serializable]
    public class Batch
    {
        public GameObject ghostPrefab;
        [Min(1)] public int count = 1;
    }

    [Header("Plan")]
    public List<Batch> plan = new List<Batch>();

    [Header("Spawn Points")]
    public List<Transform> spawnPoints = new List<Transform>();
    public bool randomizeSpawnPoints = true;
    public bool avoidImmediateRepeats = true; // percorre todos antes de reshuffle

    [Header("Spawn Options")]
    public bool spawnOnStart = true;
    public float delayBetweenSpawns = 0.15f;
    public float spawnJitterRadius = 0.6f;

    [Header("NavMesh Projection")]
    public bool clampToNavMesh = true;
    public float maxProjectionDistance = 2.0f;
    public int areaMask = ~0;

    [Header("Determinism / Seed (opcional)")]
    public bool useFixedSeed = false;
    public int seed = 12345;

    private System.Random prng;

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (useFixedSeed) prng = new System.Random(seed);
        if (spawnOnStart) StartCoroutine(SpawnAll());
    }

    [Server]
    public IEnumerator SpawnAll()
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            Debug.LogWarning($"[{nameof(GhostSpawner)}] No spawn points configured.");
            yield break;
        }

        var indexOrder = BuildIndexOrder(spawnPoints.Count);
        int indexPtr = 0;

        foreach (var b in plan)
        {
            if (!b.ghostPrefab)
            {
                Debug.LogWarning("Ghost prefab missing in plan.");
                continue;
            }

            for (int i = 0; i < b.count; i++)
            {
                var sp = spawnPoints[indexOrder[indexPtr]];
                indexPtr = (indexPtr + 1) % indexOrder.Count;

                Vector3 pos = GetSpawnPosition(sp.position);
                Quaternion rot = sp.rotation;

                GameObject ghost = Instantiate(b.ghostPrefab, pos, rot);
                NetworkServer.Spawn(ghost);

                if (delayBetweenSpawns > 0)
                    yield return new WaitForSeconds(delayBetweenSpawns);
            }

            if (randomizeSpawnPoints && avoidImmediateRepeats)
            {
                indexOrder = BuildIndexOrder(spawnPoints.Count);
                indexPtr = 0;
            }
        }
    }

    private List<int> BuildIndexOrder(int count)
    {
        var list = new List<int>(count);
        for (int i = 0; i < count; i++) list.Add(i);

        if (randomizeSpawnPoints)
        {
            if (useFixedSeed)
            {
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = prng.Next(i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }
            else
            {
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }
        }
        return list;
    }

    private Vector3 GetSpawnPosition(Vector3 basePos)
    {
        Vector2 jitter = useFixedSeed
            ? new Vector2((float)prng.NextDouble() - 0.5f, (float)prng.NextDouble() - 0.5f)
            : new Vector2(Random.value - 0.5f, Random.value - 0.5f);

        var pos = basePos + new Vector3(jitter.x, 0f, jitter.y) * spawnJitterRadius;

        if (clampToNavMesh && NavMesh.SamplePosition(pos, out var hit, maxProjectionDistance, areaMask))
            pos = hit.position;

        return pos;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        foreach (var t in spawnPoints)
        {
            if (!t) continue;
            Gizmos.DrawWireSphere(t.position, 0.3f);
        }
    }
#endif
}
