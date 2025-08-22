using Mirror;
using UnityEngine;
using UnityEngine.AI;
using System.Linq;
using System.Collections.Generic;

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
    [SyncVar] private bool waitingMinigame;

    // ---- refs
    private GhostCaptureable cap;
    private Ghost ghost;
    private NavMeshAgent agent;
    private GhostArchetype arch;

    // ---- auxiliares servidor

    // ---- Novo: controle de minigame/checkpoints
    private int nextRoundIndex;               // 0..mg_rounds-1
    private float[] checkpoints;              // frações 0..1 (ex.: 0.33, 0.66, 0.99)
    private float[] roundSpeed;
    private float[] roundWidth;
    private int[]   roundAttempts;

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

        SetupMinigameFromArchetype();
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

        // precisa estar em modo de captura (já previsto no código original):
        if (!capturing)
        {
            if (!cap.IsStunned()) return; // requer stun prévio
            StartCapturing(playerNetId);
            cap.ServerForceExitStunKeepFrozen();
        }

        // somente o "dono" avança
        if (capturingPlayerNetId != playerNetId) return;

        // Se aguardando minigame, ignora progresso
        if (waitingMinigame) return;

        // Avança progresso
        progress01 = Mathf.Clamp01(progress01 + (dt / Mathf.Max(0.01f, secondsToCapture)));

        // Ao cruzar próximo checkpoint, pausa e dispara minigame
        TryEnterMinigameIfNeeded();

        if (progress01 >= 1f && !waitingMinigame)
            CompleteCapture();
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

    void SetupMinigameFromArchetype()
    {
        // defaults robustos
        int rounds = arch ? Mathf.Clamp(arch.mg_rounds, 1, 3) : 3;
        checkpoints = new float[rounds];
        for (int i = 0; i < rounds; i++)
            checkpoints[i] = (i + 1) / (float)(rounds + 0);

        // arrays por round (fallback em caso de tamanhos incompatíveis)
        roundSpeed    = BuildRoundArray(arch?.mg_markerSpeed, rounds, 1.6f);
        roundWidth    = BuildRoundArray(arch?.mg_windowWidth, rounds, 0.18f);
        roundAttempts = BuildRoundArray(arch?.mg_attempts, rounds, 1);
        nextRoundIndex = 0;
    }

    static float[] BuildRoundArray(float[] src, int count, float defVal)
    {
        var a = new float[count];
        for (int i = 0; i < count; i++)
            a[i] = (src != null && i < src.Length) ? src[i] : defVal;
        return a;
    }
    static int[] BuildRoundArray(int[] src, int count, int defVal)
    {
        var a = new int[count];
        for (int i = 0; i < count; i++)
            a[i] = (src != null && i < src.Length) ? Mathf.Max(1, src[i]) : defVal;
        return a;
    }

    [Server]
    void TryEnterMinigameIfNeeded()
    {
        if (nextRoundIndex >= (checkpoints?.Length ?? 0)) return;
        float target = checkpoints[nextRoundIndex];
        if (progress01 + 1e-4f >= target) // margem
        {
            waitingMinigame = true;

            // congela novamente para segurança
            ghost.ServerSetExternalControl(true);
            agent.ResetPath();
            agent.velocity = Vector3.zero;

            // TargetRPC para o jogador dono
            var ownerConn = GetOwnerConnection();
            if (ownerConn != null)
            {
                var bridge = ownerConn.identity.GetComponent<PlayerMinigameBridge>();
                if (bridge)
                {
                    bridge.Target_StartMinigame(ownerConn, netIdentity.netId,
                        roundSpeed[nextRoundIndex],
                        roundWidth[nextRoundIndex],
                        roundAttempts[nextRoundIndex],
                        1 // requiredSuccesses
                    );
                }
            }
        }
    }

    NetworkConnectionToClient GetOwnerConnection()
    {
        if (!NetworkServer.spawned.TryGetValue(capturingPlayerNetId, out var id)) return null;
        return id.connectionToClient;
    }

    // ===== resultado vindo do cliente =====
    [Server]
    public void ServerOnMinigameResult(uint senderPlayerNetId, bool success)
    {
        // só aceita do dono
        if (senderPlayerNetId != capturingPlayerNetId) return;
        if (!waitingMinigame) return;

        if (!success)
        {
            progress01 = 0f;
            nextRoundIndex = 0;
            waitingMinigame = false;
            capturing = false;
            capturingPlayerNetId = 0;
            ghost.ServerSetExternalControl(false);
            return;
        }

        waitingMinigame = false;
        nextRoundIndex = Mathf.Min(nextRoundIndex + 1, (checkpoints?.Length ?? 0));

        // mantém o fantasma travado enquanto a captura continua
        ghost.ServerSetExternalControl(true);

        // Se já tinha atingido 100%, finalize agora
        if (progress01 >= 1f)
            CompleteCapture();
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

