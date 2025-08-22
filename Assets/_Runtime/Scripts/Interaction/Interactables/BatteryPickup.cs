using Mirror;
using UnityEngine;
using Game.Interaction;

/// <summary>
/// Pickup de Bateria: adiciona +1 carga UV ao PlayerFlashlight.
/// Requer que o objeto tenha NetworkIdentity e esteja na layer interag�vel usada pelo PlayerInteractor.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(Collider))]
public class BatteryPickup : NetworkBehaviour, IInteractable
{
    [Header("Config")]
    [Tooltip("Quantas cargas esta bateria concede ao pegar.")]
    [Min(1)][SerializeField] private int chargesToAdd = 1;

    [Tooltip("Se true, destr�i a bateria mesmo se o jogador j� estiver com UV cheia.")]
    [SerializeField] private bool consumeEvenIfFull = false;

    [Header("UI")]
    [SerializeField] private string interactText = "Pegar Bateria";

    // === IInteractable ===
    public string GetInteractText() => interactText;

    public bool CanInteract(GameObject interactor)
    {
        // Permite intera��o apenas se houver PlayerFlashlight no interator.
        var pf = interactor.GetComponent<PlayerFlashlight>();
        if (!pf) return false;

        // Se for para consumir mesmo cheio, sempre permite.
        if (consumeEvenIfFull) return true;

        // Caso contr�rio, s� permite se ainda houver espa�o para mais cargas.
        return pf.GetUVCharges() < pf.maxCharges;
    }

    [Server]
    public void ServerInteract(GameObject interactor)
    {
        var pf = interactor.GetComponent<PlayerFlashlight>();
        if (!pf)
        {
            Debug.LogWarning($"[BatteryPickup] {interactor.name} n�o possui PlayerFlashlight.");
            return;
        }

        int added = 0;
        for (int i = 0; i < chargesToAdd; i++)
        {
            // Usa API do servidor para adicionar carga.
            if (pf.ServerTryAddUVCharge())
                added++;
            else
                break;
        }

        if (added > 0 || consumeEvenIfFull)
        {
            Debug.Log($"[BatteryPickup] {interactor.name} pegou bateria: +{added} carga(s) UV. Agora ~{pf.GetUVCharges()}/{pf.maxCharges}.");
            // Destr�i no servidor; Mirror replica para todos.
            NetworkServer.Destroy(gameObject);
        }
        else
        {
            Debug.Log($"[BatteryPickup] {interactor.name} tentou pegar bateria, mas a UV j� est� cheia. Pickup preservado.");
        }
    }

#if UNITY_EDITOR
    void Reset()
    {
        // Sugere collider como trigger para facilitar sele��o por raycast se voc� usar triggers.
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = false; // ajuste conforme seu setup de raycast
    }
#endif
}
