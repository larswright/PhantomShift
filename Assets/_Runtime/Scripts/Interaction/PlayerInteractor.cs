using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerInteractor : NetworkBehaviour
{
    [Header("Refs")]
    [Tooltip("Câmera do jogador (usada para raycast de interação).")]
    public Camera cam;
    [Tooltip("Raiz de rotação vertical (pitch) do jogador, se houver.")]
    public Transform camRoot;

    [Header("Interação")]
    [Tooltip("Distância máxima de interação (m).")]
    public float interactRange = 3.0f;
    [Tooltip("Ângulo máximo entre o forward da câmera e o alvo (graus).")]
    public float maxAimAngle = 8f;
    [Tooltip("Camadas a IGNORAR (ex.: Player, Ghost). Se vazio, preenche automático.")]
    public LayerMask ignoreLayers;

    // Novo Input System (reutiliza o wrapper já usado no PlayerFPS)
    InputSystem_Actions actions;
    InputAction interactAction;

    // Cache de máscara efetiva para raycast (inverso das ignoradas)
    int raycastMask;

    void Reset()
    {
        if (!cam) cam = GetComponentInChildren<Camera>(true);
        if (!camRoot && transform.Find("CamRoot")) camRoot = transform.Find("CamRoot");
    }

    void Awake()
    {
        actions = new InputSystem_Actions();
        interactAction = actions.Player.Interact; // Adicione a action "Interact" no mapa Player

        if (ignoreLayers == 0)
        {
            int ig = 0;
            ig |= LayerMask.NameToLayer("Player") >= 0 ? (1 << LayerMask.NameToLayer("Player")) : 0;
            ig |= LayerMask.NameToLayer("Ghost")  >= 0 ? (1 << LayerMask.NameToLayer("Ghost"))  : 0;
            ignoreLayers = ig;
        }
        raycastMask = ~ignoreLayers;
    }

    public override void OnStartLocalPlayer()
    {
        actions.Player.Enable();
        interactAction.performed += OnInteractPerformed;

        if (!cam) cam = GetComponentInChildren<Camera>(true);
        if (!camRoot && transform.Find("CamRoot")) camRoot = transform.Find("CamRoot");
    }

    void OnDisable()
    {
        if (isLocalPlayer)
        {
            actions?.Player.Disable();
            interactAction.performed -= OnInteractPerformed;
        }
    }

    void OnInteractPerformed(InputAction.CallbackContext _)
    {
        if (!isLocalPlayer || !cam) return;

        // Raycast do centro da câmera
        Vector3 origin = cam.transform.position;
        Vector3 dir    = cam.transform.forward;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, interactRange, raycastMask, QueryTriggerInteraction.Ignore))
        {
            // Garante que estamos realmente "olhando" (cone estreito)
            float angle = Vector3.Angle(dir, (hit.point - origin));
            if (angle > maxAimAngle) return;

            var ni = hit.collider.GetComponentInParent<NetworkIdentity>();
            if (!ni) return;

            // Verifica se o objeto implementa IInteractable
            var interactable = ni.GetComponent<IInteractable>();
            if (interactable == null) return;

            // Envia pedido ao servidor
            CmdTryInteract(ni);
        }
    }

    [Command]
    void CmdTryInteract(NetworkIdentity targetNi)
    {
        if (!targetNi) return;

        // Validações básicas no servidor (anti-lag/anti-cheat leve)
        Transform head = camRoot ? camRoot : transform;
        Vector3 origin = head.position;
        Vector3 toTarget = targetNi.transform.position - origin;

        if (toTarget.sqrMagnitude > (interactRange * interactRange) + 0.01f) return;

        // LOS simples no servidor (ignora Player/Ghost)
        int mask = ~ignoreLayers.value;
        if (Physics.Raycast(origin, toTarget.normalized, out RaycastHit hit, interactRange, mask, QueryTriggerInteraction.Ignore))
        {
            var hitNi = hit.collider.GetComponentInParent<NetworkIdentity>();
            if (hitNi != targetNi) return;
        }
        else return;

        var interactable = targetNi.GetComponent<IInteractable>();
        if (interactable == null) return;

        // Checagem de permissão
        if (!interactable.CanInteract(netIdentity)) return;

        // Executa no servidor
        interactable.ServerInteract(netIdentity);
    }
}
