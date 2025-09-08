using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Central toggle + helpers for ghost AI debugging logs.
/// Drop a GhostAIDebugConfig asset in a Resources folder if you want inspector control.
/// </summary>
public static class GhostAIDebug {
    // Global toggle. You can flip this via code or via optional config asset.
    public static bool Enabled = true;

    // Optional: Resources/ path-based config for inspector control.
    private static GhostAIDebugConfig _config;
    private static bool _configSearched;

    private static bool IsOn {
        get {
            if (!_configSearched) {
                _config = Resources.Load<GhostAIDebugConfig>("GhostAIDebugConfig");
                _configSearched = true;
            }
            return _config ? _config.enableLogs : Enabled;
        }
    }

    public static void Log(Object ctx, string msg) {
        if (IsOn) Debug.Log($"[GhostAI] {msg}", ctx);
    }

    public static void Warn(Object ctx, string msg) {
        if (IsOn) Debug.LogWarning($"[GhostAI] {msg}", ctx);
    }

    public static void Error(Object ctx, string msg) {
        Debug.LogError($"[GhostAI] {msg}", ctx);
    }

    public static string AgentSnapshot(NavMeshAgent a) {
        if (!a) return "<no agent>";
        var pos = a.transform.position;
        var vel = a.velocity;
        return $"pos={pos:F2} enabled={a.enabled} onNav={a.isOnNavMesh} speed={a.speed:F2} acc={a.acceleration:F1} ang={a.angularSpeed:F1} stop={a.stoppingDistance:F2} hasPath={a.hasPath} pending={a.pathPending} remDist={a.remainingDistance:F2} pathStat={a.pathStatus} vel={vel.x:F2},{vel.y:F2},{vel.z:F2}";
    }
}

/// <summary>
/// Optional config to control logs from Inspector.
/// Create at: Assets/Resources/GhostAIDebugConfig.asset
/// </summary>
[CreateAssetMenu(menuName = "PhantomShift/Debug/GhostAIDebugConfig", fileName = "GhostAIDebugConfig")]
public class GhostAIDebugConfig : ScriptableObject {
    public bool enableLogs = true;
}

