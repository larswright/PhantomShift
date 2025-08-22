using Mirror;
using UnityEngine;

[RequireComponent(typeof(NetworkIdentity))]
public class Battery : NetworkBehaviour, IInteractable
{
    [Header("Feedback")]
    [Tooltip("Mensagem do log no cliente do interator.")]
    public string pickedMessage = "Battery picked.";

    [Tooltip("Raio máximo que permitimos pegar (m). Deve casar com o PlayerInteractor.")]
    public float maxServerDistance = 3.2f;

    public bool CanInteract(NetworkIdentity interactor)
    {
        if (!isServer) return true; // validação final fica em ServerInteract
        if (!interactor) return false;

        float dist = Vector3.Distance(interactor.transform.position, transform.position);
        return dist <= maxServerDistance;
    }

    public void ServerInteract(NetworkIdentity interactor)
    {
        if (!isServer || !interactor) return;

        // Segurança extra de distância
        if (Vector3.Distance(interactor.transform.position, transform.position) > maxServerDistance)
            return;

        // TargetRpc: mensagem no cliente que interagiu
        var conn = interactor.connectionToClient;
        if (conn != null) TargetOnPicked(conn, pickedMessage);

        // Destrói a bateria no servidor (replica para todos)
        NetworkServer.Destroy(gameObject);
    }

    public string GetPrompt() => "Pick up Battery [E]";

    [TargetRpc]
    void TargetOnPicked(NetworkConnectionToClient target, string msg)
    {
        Debug.Log(msg);
    }
}
