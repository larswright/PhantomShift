using Mirror;
using UnityEngine;

[RequireComponent(typeof(NetworkIdentity))]
public class BatteryPickupUV : NetworkBehaviour, IInteractable
{
    [Header("Mensagens")]
    public string pickedMsg = "Battery picked.";
    public string fullMsg   = "UV battery already full.";

    [Header("Validação")]
    public float maxServerDistance = 3.2f; // opcional extra

    public bool CanInteract(NetworkIdentity interactor)
    {
        // Checagem leve; validação final em ServerInteract
        return interactor != null;
    }

    public void ServerInteract(NetworkIdentity interactor)
    {
        if (!isServer || interactor == null) return;

        // Segurança de distância
        if (Vector3.Distance(interactor.transform.position, transform.position) > maxServerDistance)
            return;

        var lights = interactor.GetComponent<PlayerFlashlight>();
        if (lights == null) return;

        bool added = lights.ServerTryAddUVCharge();
        var conn   = interactor.connectionToClient;

        if (added)
        {
            if (conn != null) TargetNotify(conn, pickedMsg);
            NetworkServer.Destroy(gameObject); // some para todos
        }
        else
        {
            if (conn != null) TargetNotify(conn, fullMsg);
            // NÃO destrói se estiver cheio
        }
    }

    public string GetPrompt() => "Pick up Battery [E]";

    [TargetRpc]
    void TargetNotify(NetworkConnectionToClient target, string msg)
    {
        Debug.Log(msg);
    }
}
