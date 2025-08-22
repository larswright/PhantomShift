using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Raycast de Vacuum do jogador: segura botão p/ sugar; reporta ao servidor em ticks (dt).
/// NÃO depende do InputSystem_Actions gerado: use InputActionReference no Inspector.
/// </summary>
[DisallowMultipleComponent]
public class PlayerVacuum : NetworkBehaviour
{
    [Header("Raycast")]
    public Transform vacuumRayOrigin;
    public float vacuumRayDistance = 10f;
    public float vacuumRayRadius = 0.5f;
    public LayerMask vacuumLayerMask = ~0;
    public float reportInterval = 0.1f;

    [Header("Inputs (Input System)")]
    [Tooltip("Ação de segurar o Vacuum (ex.: LMB). Type=Button, Interaction=Press&Hold")]
    public InputActionReference holdVacuumAction;

    // ---- runtime local
    private bool holdingVacuum;
    private float nextReportTime;
    private Camera _cam;

    public override void OnStartLocalPlayer()
    {
        if (holdVacuumAction && holdVacuumAction.action != null)
        {
            holdVacuumAction.action.performed += OnHoldPerformed;
            holdVacuumAction.action.canceled  += OnHoldCanceled;
            holdVacuumAction.action.Enable();
        }

        _cam = Camera.main;
    }

    private void OnDisable()
    {
        if (isLocalPlayer)
        {
            if (holdVacuumAction && holdVacuumAction.action != null)
            {
                holdVacuumAction.action.performed -= OnHoldPerformed;
                holdVacuumAction.action.canceled  -= OnHoldCanceled;
            }
        }
    }

    void OnHoldPerformed(InputAction.CallbackContext _)
    {
        if (!isLocalPlayer) return;
        holdingVacuum = true;
    }

    void OnHoldCanceled(InputAction.CallbackContext _)
    {
        if (!isLocalPlayer) return;
        holdingVacuum = false;
    }

    void Update()
    {
        if (!isLocalPlayer) return;
        if (!holdingVacuum) return;
        if (!vacuumRayOrigin) return;

        // Ray visual
        var ray = new Ray(vacuumRayOrigin.position, vacuumRayOrigin.forward);
        Debug.DrawRay(ray.origin, ray.direction * vacuumRayDistance, Color.cyan);

        if (Time.time < nextReportTime) return;
        nextReportTime = Time.time + reportInterval;

        if (Physics.SphereCast(ray, vacuumRayRadius, out var hit, vacuumRayDistance, vacuumLayerMask, QueryTriggerInteraction.Collide))
        {
            var cap = hit.collider.GetComponentInParent<GhostCaptureable>();
            if (cap && cap.netIdentity)
            {
                // Reporta tick de sucção
                CmdVacuumHit(cap.netIdentity.netId, vacuumRayOrigin.position, vacuumRayOrigin.forward, reportInterval);
            }
        }
    }

    // ---------- RPC/Commands ----------

    [Command(requiresAuthority = false)]
    void CmdVacuumHit(uint ghostNetId, Vector3 origin, Vector3 dir, float dt, NetworkConnectionToClient sender = null)
    {
        if (!NetworkServer.spawned.TryGetValue(ghostNetId, out var id)) return;
        var vac = id.GetComponent<GhostVacuum>();
        if (!vac) return;

        // netId do jogador que está sugando
        uint playerNetId = sender != null && sender.identity ? sender.identity.netId : netIdentity.netId;
        vac.ServerApplyVacuumHit(playerNetId, origin, dir, dt);
    }
}

