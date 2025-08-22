using UnityEngine;

namespace Game.Interaction
{
    /// <summary>
    /// Contrato que qualquer objeto interagível deve cumprir.
    /// A execução de ServerInteract SEMPRE ocorre no servidor.
    /// </summary>
    public interface IInteractable
    {
        string GetInteractText();               // opcional: texto para UI ("Abrir", "Pegar", etc.)
        bool CanInteract(GameObject interactor); // validação extra de permissão/estado
        void ServerInteract(GameObject interactor); // lógica final da interação (no servidor)
    }
}
