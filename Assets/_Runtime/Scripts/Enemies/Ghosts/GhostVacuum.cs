using Mirror;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Lógica servidor da captura por Vacuum (progresso, travamento do fantasma).
/// Requer GhostCaptureable (para stun/flee) e Ghost (para externalControl).
/// </summary>
[RequireComponent(typeof(GhostCaptureable))]
[RequireComponent(typeof(Ghost))]
[RequireComponent(typeof(NavMeshAgent))]
public class GhostVacuum : NetworkBehaviour
{
    [Header("Config (override opcional; se 0, lê do Archetype)")]
    public float secondsToCapture = 0f;     // 0 = pega do archetype

    [Header("Eventos/UI (opcional)")]
    public UnityEngine.Events.UnityEvent<float> onProgress; // 0..1

    // ---- Estado replicado
    [SyncVar(hook = nameof(OnCapturingChanged))] private bool capturing;
    [SyncVar(hook = nameof(OnProgressChanged))]  private float progress01; // 0..1
    [SyncVar] private uint capturingPlayerNetId;

    // ---- refs
    private GhostCaptureable cap;
    private Ghost ghost;
    private NavMeshAgent agent;
    private GhostArchetype arch;

    // ---- auxiliares servidor

    public override void OnStartServer()
    {
        cap   = GetComponent<GhostCaptureable>();
        ghost = GetComponent<Ghost>();
        agent = GetComponent<NavMeshAgent>();

        arch = GetArchetypeSafely();
        if (arch)
        {
            if (secondsToCapture <= 0f) secondsToCapture = Mathf.Max(0.1f, arch.capture_vacuumSecondsToCapture);
        }
        else
        {
            if (secondsToCapture <= 0f) secondsToCapture = 4f;
        }
    }

    GhostArchetype GetArchetypeSafely()
    {
        // GhostCaptureable e Ghost possuem referência de archetype no seu serialized
        // Aqui opto por acessar via campo privado por SerializeField; se não existir, retorna null.
        var f = typeof(GhostCaptureable).GetField("archetype", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return f != null ? (GhostArchetype)f.GetValue(cap) : null;
    }

    // ============== API (servidor) chamada pelo PlayerVacuum ==============

    [Server]
    public void ServerApplyVacuumHit(uint playerNetId, Vector3 origin, Vector3 dir, float dt)
    {
        if (!cap || !ghost) return;

        // Começa somente se estava stunnado
        if (!capturing)
        {
            if (!cap.IsStunned()) return;                // precisa estar stunnado
            StartCapturing(playerNetId);                 // 1. Ao começar a sugar...
            cap.ServerForceExitStunKeepFrozen();         // 1.5 zera o stun, porém mantém parado
        }

        // Uma vez capturando, trava o "dono"
        if (capturingPlayerNetId != playerNetId) return;

        // Avança progresso
        progress01 = Mathf.Clamp01(progress01 + (dt / Mathf.Max(0.01f, secondsToCapture)));

        // Captura completa
        if (progress01 >= 1f)
        {
            CompleteCapture();
        }
    }

    // ============== Internals ==============

    [Server]
    void StartCapturing(uint playerNetId)
    {
        capturing = true;
        capturingPlayerNetId = playerNetId;

        // congela movimento e zera velocidade instantaneamente
        ghost.ServerSetExternalControl(true);
        agent.ResetPath();
        agent.velocity = Vector3.zero;
    }

    [Server]
    void CompleteCapture()
    {
        capturing = false;

        // Aqui você pode tocar VFX/SFX, soltar loot etc.
        NetworkServer.Destroy(gameObject); // remove o fantasma capturado
    }

    // ============== Hooks replicação (UI local etc.) ==============

    void OnCapturingChanged(bool _, bool now)
    {
        // placeholder; você pode ligar/desligar VFX de "sucção"
    }
    void OnProgressChanged(float _, float now)
    {
        onProgress?.Invoke(now);
    }
}

