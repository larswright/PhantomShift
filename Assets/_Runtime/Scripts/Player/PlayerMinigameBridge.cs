using Mirror;
using UnityEngine;

public class PlayerMinigameBridge : NetworkBehaviour
{
    [Header("Refs")]
    public MinigameMarkerWindow ui;

    // Runtime
    private uint currentGhostNetId;

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        if (ui) ui.StopMinigame();
    }

    // ===== chamado pelo servidor para este cliente específico =====
    [TargetRpc]
    public void Target_StartMinigame(NetworkConnectionToClient _, uint ghostNetId, float speed, float width, int attempts, int requiredSuccesses)
    {
        if (!isLocalPlayer || ui == null) return;
        currentGhostNetId = ghostNetId;

        ui.onAllSuccess.RemoveAllListeners();
        ui.onAllFail.RemoveAllListeners();

        ui.onAllSuccess.AddListener(() => Cmd_ReportMinigameResult(currentGhostNetId, true));
        ui.onAllFail.AddListener(() => Cmd_ReportMinigameResult(currentGhostNetId, false));

        ui.StartMinigame(new MinigameMarkerWindow.Params {
            markerSpeed = speed,
            windowWidth = width,
            attempts = attempts,
            requiredSuccesses = requiredSuccesses
        });
    }

    [Command]
    void Cmd_ReportMinigameResult(uint ghostNetId, bool success)
    {
        // Encaminha para o GhostVacuum correto no servidor
        if (!NetworkServer.spawned.TryGetValue(ghostNetId, out var id)) return;
        var gv = id.GetComponent<GhostVacuum>();
        if (!gv) return;

        // Confirma autoria: este Command vem do jogador correto?
        var senderNetId = connectionToClient?.identity ? connectionToClient.identity.netId : netIdentity.netId;
        gv.ServerOnMinigameResult(senderNetId, success);
    }
}

