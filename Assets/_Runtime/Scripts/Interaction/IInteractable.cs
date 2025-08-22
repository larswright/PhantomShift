using Mirror;
using UnityEngine;

public interface IInteractable
{
    // Pode aplicar gating adicional (ex.: chave, cooldown, etc.)
    bool CanInteract(NetworkIdentity interactor);

    // Executa a interação no SERVIDOR.
    void ServerInteract(NetworkIdentity interactor);

    // (Opcional) texto de UI
    string GetPrompt();
}
