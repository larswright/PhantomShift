using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Interaction;

[DisallowMultipleComponent]
public class PlayerInteractor : NetworkBehaviour, InputSystem_Actions.IPlayerActions
{
    [Header("Raycast")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float interactDistance = 3.0f;
    [Tooltip("Somente objetos nestas layers serão considerados interagíveis.")]
    [SerializeField] private LayerMask interactableLayer;
    [Tooltip("Layers a serem ignoradas (ex.: Player, Ignore Raycast).")]
    [SerializeField] private LayerMask ignoreLayers;

    private InputSystem_Actions input;
    private NetworkIdentity currentTarget;
    private Ray lastRay;

    private void Awake()
    {
        if (playerCamera == null && isLocalPlayer)
            playerCamera = Camera.main;
    }

    public override void OnStartAuthority()
    {
        input = new InputSystem_Actions();
        input.Player.SetCallbacks(this);
        input.Player.Enable();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public override void OnStopAuthority()
    {
        if (input != null)
        {
            input.Player.Disable();
            input.Dispose();
        }
    }

    private void Update()
    {
        if (!isOwned) return;

        var cam = playerCamera != null ? playerCamera : Camera.main;
        if (cam == null) return;

        lastRay = new Ray(cam.transform.position, cam.transform.forward);

        int maskInclude = ~ignoreLayers.value;
        if (Physics.Raycast(lastRay, out var hit, interactDistance, maskInclude, QueryTriggerInteraction.Ignore))
        {
            bool isOnInteractableLayer = (interactableLayer.value & (1 << hit.collider.gameObject.layer)) != 0;
            currentTarget = isOnInteractableLayer
                ? hit.collider.GetComponentInParent<NetworkIdentity>()
                : null;
        }
        else
        {
            currentTarget = null;
        }

        Debug.DrawRay(lastRay.origin, lastRay.direction * interactDistance, currentTarget ? Color.green : Color.red);
    }

    public void OnInteract(InputAction.CallbackContext ctx)
    {
        if (!isOwned) return;

        if (ctx.performed && currentTarget != null)
        {
            CmdInteract(currentTarget, lastRay.origin, lastRay.direction);
        }
    }

    [Command]
    private void CmdInteract(NetworkIdentity target, Vector3 origin, Vector3 direction)
    {
        if (target == null) return;

        int maskInclude = ~ignoreLayers.value;
        bool valid = false;

        if (Physics.Raycast(origin, direction, out var hit, interactDistance + 0.5f, maskInclude, QueryTriggerInteraction.Ignore))
        {
            var hitNI = hit.collider.GetComponentInParent<NetworkIdentity>();
            valid = (hitNI == target);
        }
        else
        {
            var dist = Vector3.Distance(origin, target.transform.position);
            valid = dist <= (interactDistance + 0.5f);
        }

        if (!valid) return;

        var interactable = target.GetComponent<IInteractable>();
        if (interactable != null && interactable.CanInteract(gameObject))
        {
            interactable.ServerInteract(gameObject);
        }
    }

    public void OnMove(InputAction.CallbackContext context) { }
    public void OnLook(InputAction.CallbackContext context) { }
    public void OnAttack(InputAction.CallbackContext context) { }
    public void OnCrouch(InputAction.CallbackContext context) { }
    public void OnJump(InputAction.CallbackContext context) { }
    public void OnPrevious(InputAction.CallbackContext context) { }
    public void OnNext(InputAction.CallbackContext context) { }
    public void OnSprint(InputAction.CallbackContext context) { }
    public void OnFlashlight(InputAction.CallbackContext context) { }
    public void OnHoldUV(InputAction.CallbackContext context) { }
}
