using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

[DisallowMultipleComponent]
public class PlayerFlashlight : NetworkBehaviour
{
    [Header("Refs")]
    [Tooltip("GameObject da luz normal (ex.: um filho com Light/SpotLight).")]
    public GameObject normalLightGO;
    [Tooltip("GameObject da luz UV (pode ser outro Light com cor roxa, layer/culling específico).")]
    public GameObject uvLightGO;

    [Header("UV Config")]
    [Tooltip("Cargas máximas da UV.")]
    public int maxCharges = 3;
    [Tooltip("Duração (s) de cada carga da UV.")]
    public float secondsPerCharge = 12f;

    [Header("UV Raycast")]
    public Transform uvRayOrigin;
    public float uvRayDistance = 10f;
    public float uvRayRadius = 0.5f;
    public float uvReportInterval = 0.1f;
    public LayerMask uvLayerMask = ~0;

    // ===== Estado replicado =====
    [SyncVar(hook = nameof(OnNormalChanged))] private bool normalOn;
    [SyncVar(hook = nameof(OnUVChanged))] private bool uvOn;
    [SyncVar(hook = nameof(OnUVSecondsChanged))] private float uvSecondsRemaining; // 0 .. maxCharges*secondsPerCharge

    // ===== Input (somente no cliente local) =====
    private InputSystem_Actions actions;
    private InputAction flashlightAction; // toggle normal
    private InputAction uvAction;         // toggle UV

    // ===== Server runtime =====
    private Coroutine drainCo;

    // ===== Helpers =====
    private float MaxUVSeconds => maxCharges * secondsPerCharge;
    private float nextUVReportTime;

    void Reset()
    {
        // Tenta achar automaticamente luzes por nome
        if (!normalLightGO && transform.Find("Light_Normal"))
            normalLightGO = transform.Find("Light_Normal").gameObject;
        if (!uvLightGO && transform.Find("Light_UV"))
            uvLightGO = transform.Find("Light_UV").gameObject;
    }

    void Awake()
    {
        // Garante estado visual consistente mesmo antes do primeiro hook
        ApplyNormalVisual(normalOn);
        ApplyUVVisual(uvOn);
    }

    public override void OnStartServer()
    {
        // Estado inicial
        uvSecondsRemaining = MaxUVSeconds;
        normalOn = true;
        uvOn = false;
    }

    public override void OnStartLocalPlayer()
    {
        actions = new InputSystem_Actions();
        actions.Player.Enable();

        flashlightAction = actions.Player.Flashlight; // Button (toggle)
        uvAction         = actions.Player.HoldUV;     // Button (toggle)

        flashlightAction.performed += OnFlashlightTogglePerformed;
        uvAction.performed         += OnUVTogglePerformed;

        // Garante que o visual local reflete o estado inicial replicado
        ApplyNormalVisual(normalOn);
        ApplyUVVisual(uvOn);
    }

    void OnDisable()
    {
        if (isLocalPlayer && actions != null)
        {
            flashlightAction.performed -= OnFlashlightTogglePerformed;
            uvAction.performed         -= OnUVTogglePerformed;
            actions.Player.Disable();
        }
    }

    // ===== INPUT callbacks (cliente local) =====

    void OnFlashlightTogglePerformed(InputAction.CallbackContext _)
    {
        if (!isLocalPlayer) return;
        CmdSetNormal(!normalOn);
    }

    void OnUVTogglePerformed(InputAction.CallbackContext _)
    {
        if (!isLocalPlayer) return;
        CmdSetUV(!uvOn);
    }

    // ===== Commands =====

    [Command]
    void CmdSetNormal(bool turnOn)
    {
        // Sem restrição de recurso
        normalOn = turnOn;
    }

    [Command]
    void CmdSetUV(bool wantOn)
    {
        Debug.Log($"<color=cyan>[PF][SV] CmdSetUV({wantOn}) uvOn={uvOn} uvSeconds={uvSecondsRemaining:F2}</color>");
        if (wantOn)
        {
            // Liga se houver recurso
            if (!uvOn && uvSecondsRemaining > 0f)
            {
                uvOn = true;
                StartDrainIfNeeded();
            }
        }
        else
        {
            // Desliga
            if (uvOn)
            {
                uvOn = false;
                StopDrainIfNeeded();
            }
        }
    }

    [Command]
    void CmdUVHit(uint netId, Vector3 origin, Vector3 dir, float dt)
    {
        Debug.Log($"[PF][SV] Reportando UV no netId {netId} dt={dt:F2}");
        if (NetworkServer.spawned.TryGetValue(netId, out var identity))
        {
            var cap = identity.GetComponent<GhostCaptureable>();
            if (cap)
                cap.ServerApplyUVHit(origin, dir, dt);
        }
    }


    void Update()
    {
        if (!isLocalPlayer) { Debug.Log("[PF] !isLocalPlayer"); return; }
        if (!uvOn)          { Debug.Log("[PF] UV OFF no cliente"); return; }
        if (!uvRayOrigin)   { Debug.Log("[PF] uvRayOrigin nulo"); return; }

        var ray = new Ray(uvRayOrigin.position, uvRayOrigin.forward);
        Debug.DrawRay(ray.origin, ray.direction * uvRayDistance, Color.magenta);

        if (Physics.SphereCast(ray, uvRayRadius, out var hit, uvRayDistance, uvLayerMask, QueryTriggerInteraction.Collide))
        {
            Debug.Log($"[PF] UV colidindo com {hit.collider.name}");
            var cap = hit.collider.GetComponentInParent<GhostCaptureable>();
            if (cap)
            {
                Debug.Log($"[PF] UV atingiu fantasma {cap.name}");
                var id = cap.netIdentity;
                if (id)
                {
                    if (Time.time >= nextUVReportTime)
                    {
                        CmdUVHit(id.netId, uvRayOrigin.position, uvRayOrigin.forward, uvReportInterval);
                        nextUVReportTime = Time.time + uvReportInterval;
                    }
                }
                else Debug.LogWarning("[PF] Ghost sem NetworkIdentity");
            }
            else if (Time.time >= nextUVReportTime)
            {
                Debug.LogWarning("[PF] Collider sem GhostCaptureable no parent");
                nextUVReportTime = Time.time + uvReportInterval;
            }
        }
        else if (Time.time >= nextUVReportTime)
        {
            Debug.Log("[PF] UV não colidiu com nada");
            nextUVReportTime = Time.time + uvReportInterval;
        }
    }

    // ===== Consumo da UV (servidor) =====

    void StartDrainIfNeeded()
    {
        if (!isServer) return;
        if (drainCo == null && uvOn && uvSecondsRemaining > 0f)
            drainCo = StartCoroutine(ServerDrainLoop());
    }

    void StopDrainIfNeeded()
    {
        if (!isServer) return;
        if (drainCo != null)
        {
            StopCoroutine(drainCo);
            drainCo = null;
        }
    }

    IEnumerator ServerDrainLoop()
    {
        var waitForInterval = new WaitForSeconds(uvReportInterval);
        bool ghostHit = false; // Flag para controlar se um fantasma foi atingido no intervalo

        // Consome tempo enquanto UV estiver ligada e houver tempo
        while (uvOn && uvSecondsRemaining > 0f)
        {
            // Raycast para detectar fantasmas
            var ray = new Ray(uvRayOrigin.position, uvRayOrigin.forward);
            if (Physics.SphereCast(ray, uvRayRadius, out var hit, uvRayDistance, uvLayerMask, QueryTriggerInteraction.Collide))
            {
                var cap = hit.collider.GetComponentInParent<GhostCaptureable>();
                if (cap)
                {
                    ghostHit = true;
                    Debug.Log($"<color=yellow>[PF][SV] UV is hitting a ghost. Draining charge.</color>");
                }
            }

            // Se um fantasma foi atingido, drena a carga
            if (ghostHit)
            {
                uvSecondsRemaining -= uvReportInterval;
                if (uvSecondsRemaining <= 0f)
                {
                    uvSecondsRemaining = 0f;
                    uvOn = false; // Auto-off quando acabar
                    break;
                }
                ghostHit = false; // Reseta o flag
            }

            yield return waitForInterval;
        }
        drainCo = null;
    }

    // ===== API do servidor para receber baterias =====

    /// <summary> Adiciona +1 carga (12s). Retorna true se adicionou; false se já estava no máximo. </summary>
    [Server]
    public bool ServerTryAddUVCharge()
    {
        if (uvSecondsRemaining >= MaxUVSeconds - 0.001f)
            return false;

        uvSecondsRemaining = Mathf.Min(uvSecondsRemaining + secondsPerCharge, MaxUVSeconds);
        return true;
    }

    // ===== Hooks de SyncVar =====

    void OnNormalChanged(bool _, bool newVal)
    {
        Debug.Log($"[PF] Normal {(newVal ? "ON" : "OFF")}");
        ApplyNormalVisual(newVal);
    }
    void OnUVChanged(bool _, bool newVal)
    {
        Debug.Log($"[PF] UV {(newVal ? "ON" : "OFF")}");
        ApplyUVVisual(newVal);
    }
    void OnUVSecondsChanged(float _, float __)
    {
        Debug.Log($"[PF] uvSecondsRemaining={uvSecondsRemaining:F2}");
        // Ponto para UI/HUD: atualizar barra/ícone se desejar.
        // Ex.: dispatch de evento, UnityEvent, etc.
    }

    // ===== Visual local (todos os clientes) =====

    void ApplyNormalVisual(bool on)
    {
        if (normalLightGO) normalLightGO.SetActive(on);
    }

    void ApplyUVVisual(bool on)
    {
        if (uvLightGO) uvLightGO.SetActive(on);
    }

    // ===== Utilidades públicas (cliente) =====

    /// <summary> Retorna [0..1] de reserva relativa (para UI). </summary>
    public float GetUVFill01() => MaxUVSeconds <= 0f ? 0f : Mathf.Clamp01(uvSecondsRemaining / MaxUVSeconds);

    /// <summary> Retorna cargas inteiras aproximadas (para exibir ícones). </summary>
    public int GetUVCharges() => Mathf.CeilToInt(uvSecondsRemaining / Mathf.Max(0.001f, secondsPerCharge));
}
