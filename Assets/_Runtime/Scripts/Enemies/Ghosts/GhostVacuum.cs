using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Lógica servidor da captura por Vacuum (progresso, travamento do fantasma, minigames).
/// Requer GhostCaptureable (para stun/flee) e Ghost (para externalControl).
/// </summary>
[RequireComponent(typeof(GhostCaptureable))]
[RequireComponent(typeof(Ghost))]
[RequireComponent(typeof(NavMeshAgent))]
public class GhostVacuum : NetworkBehaviour
{
    [Header("Config (override opcional; se 0, lê do Archetype)")]
    public float secondsToCapture = 0f;     // 0 = pega do archetype
    [Range(0,3)] public int minigameCount = -1; // -1 = pega do archetype
    public float minigameHandleSpeed = 0f;  // 0 = archetype
    [Range(0.01f,0.2f)] public float minigameHitWindow = 0f; // 0 = archetype

    [Header("Eventos/UI (opcional)")]
    public UnityEngine.Events.UnityEvent<float> onProgress; // 0..1

    // ---- Estado replicado
    [SyncVar(hook = nameof(OnCapturingChanged))] private bool capturing;
    [SyncVar(hook = nameof(OnProgressChanged))]  private float progress01; // 0..1
    [SyncVar] private int awaitingStage; // 0=nenhum; 1..3 = aguardando minigame
    [SyncVar] private uint capturingPlayerNetId;

    // ---- refs
    private GhostCaptureable cap;
    private Ghost ghost;
    private NavMeshAgent agent;
    private GhostArchetype arch;

    // ---- auxiliares servidor
    private float[] stageThresholds = new float[] { 0.33f, 0.66f, 0.99f };
    private int completedMinigameStage = 0;

    public override void OnStartServer()
    {
        cap   = GetComponent<GhostCaptureable>();
        ghost = GetComponent<Ghost>();
        agent = GetComponent<NavMeshAgent>();

        arch = GetArchetypeSafely();
        if (arch)
        {
            if (secondsToCapture <= 0f) secondsToCapture = Mathf.Max(0.1f, arch.capture_vacuumSecondsToCapture);
            if (minigameCount    <  0)  minigameCount    = Mathf.Clamp(arch.capture_minigameCount, 0, 3);
            if (minigameHandleSpeed <= 0f) minigameHandleSpeed = Mathf.Max(0.1f, arch.capture_minigameHandleSpeed);
            if (minigameHitWindow    <= 0f) minigameHitWindow    = Mathf.Clamp(arch.capture_minigameHitWindow, 0.01f, 0.20f);
        }
        else
        {
            if (secondsToCapture <= 0f) secondsToCapture = 4f;
            if (minigameCount    <  0)  minigameCount    = 3;
            if (minigameHandleSpeed <= 0f) minigameHandleSpeed = 1.75f;
            if (minigameHitWindow    <= 0f) minigameHitWindow = 0.06f;
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

        // Enquanto houver minigame pendente, não avança a barra
        if (awaitingStage > 0) return;

        // Avança progresso
        progress01 = Mathf.Clamp01(progress01 + (dt / Mathf.Max(0.01f, secondsToCapture)));

        // Gatilhos dos minigames
        TriggerMinigamesIfNeeded();

        // Captura completa
        if (progress01 >= 1f && awaitingStage == 0)
        {
            CompleteCapture();
        }
    }

    [Server]
    public void ServerResolveMinigameResult(uint playerNetId, int stageIndex, bool success)
    {
        if (!capturing || awaitingStage == 0) return;
        if (playerNetId != capturingPlayerNetId) return;
        if (stageIndex != awaitingStage) return;

        if (!success)
        {
            // Falha: cancela tudo, fantasma volta "ao normal" (sem stun, andando)
            FailAndRelease();
            return;
        }

        // Sucesso: limpa o "await" e segue sugando
        awaitingStage = 0;
        completedMinigameStage = stageIndex;
    }

    // ============== Internals ==============

    [Server]
    void StartCapturing(uint playerNetId)
    {
        capturing = true;
        capturingPlayerNetId = playerNetId;
        completedMinigameStage = 0;

        // congela movimento e zera velocidade instantaneamente
        ghost.ServerSetExternalControl(true);
        agent.ResetPath();
        agent.velocity = Vector3.zero;
    }

    [Server]
    void TriggerMinigamesIfNeeded()
    {
        if (minigameCount <= 0) return;

        // Checa thresholds na ordem; dispara o próximo ainda não cumprido
        for (int i = 1; i <= minigameCount; i++)
        {
            if (awaitingStage != 0) break; // já aguardando
            float thr = stageThresholds[i - 1];
            // Dispara quando cruzar o threshold (>=), sem repetir
            if (progress01 >= thr && i > completedMinigameStage)
            {
                awaitingStage = i;
                BeginMinigame(i);
            }
        }
    }

    [Server]
    void BeginMinigame(int stageIndex)
    {
        // parâmetros
        float speed   = minigameHandleSpeed;
        float marker  = Random.Range(0.15f, 0.85f);
        float window  = minigameHitWindow;

        if (!NetworkServer.spawned.TryGetValue(capturingPlayerNetId, out var playerId) || playerId.connectionToClient == null)
        {
            // jogador sumiu? falha dura
            FailAndRelease();
            return;
        }

        TargetBeginMinigame(playerId.connectionToClient, netIdentity.netId, stageIndex, speed, marker, window);
    }

    [TargetRpc]
    void TargetBeginMinigame(NetworkConnectionToClient conn, uint ghostNetId, int stageIndex, float speed, float markerPos01, float hitWindow01)
    {
        // Cliente: dispara UI pelo PlayerVacuum local
        if (PlayerVacuum.Local)
            PlayerVacuum.Local.ClientStartMinigame(ghostNetId, stageIndex, speed, markerPos01, hitWindow01);
    }

    [Server]
    void CompleteCapture()
    {
        capturing = false;
        awaitingStage = 0;

        // Aqui você pode tocar VFX/SFX, soltar loot etc.
        NetworkServer.Destroy(gameObject); // remove o fantasma capturado
    }

    [Server]
    void FailAndRelease()
    {
        capturing = false;
        awaitingStage = 0;
        progress01 = 0f;
        capturingPlayerNetId = 0;
        completedMinigameStage = 0;

        // Libera controle para IA voltar a andar
        ghost.ServerSetExternalControl(false);
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

