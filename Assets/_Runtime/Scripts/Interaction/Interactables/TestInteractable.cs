using Mirror;
using UnityEngine;
using Game.Interaction;

[RequireComponent(typeof(NetworkIdentity))]
[DisallowMultipleComponent]
public class TestInteractable : NetworkBehaviour, IInteractable
{
    [SerializeField] private string displayName = "Alavanca de Teste";

    public string GetInteractText() => $"Interagir ({displayName})";
    public bool CanInteract(GameObject interactor) => true;

    [Server]
    public void ServerInteract(GameObject interactor)
    {
        var ni = interactor.GetComponent<NetworkIdentity>();
        Debug.Log($"[SERVER] {interactor.name} (netId {ni?.netId}) interagiu com {name}");

        // Feedback só para o cliente que interagiu (exemplo simples)
        if (ni != null && ni.connectionToClient != null)
        {
            TargetShowMessage(ni.connectionToClient, $"Você interagiu com: {displayName}");
        }
    }

    [TargetRpc]
    private void TargetShowMessage(NetworkConnectionToClient conn, string msg)
    {
        Debug.Log($"[CLIENT] {msg}");
    }
}
