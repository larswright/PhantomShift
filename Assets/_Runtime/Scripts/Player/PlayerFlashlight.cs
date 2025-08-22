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
    private InputAction holdUVAction;     // hold UV

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
        normalOn = false;
        uvOn = false;
    }

    public override void OnStartLocalPlayer()
    {
        actions = new InputSystem_Actions();
        actions.Player.Enable();

        flashlightAction = actions.Player.Flashlight; // Button (toggle)
        holdUVAction     = actions.Player.HoldUV;     // Button (press/hold)

        flashlightAction.performed += OnFlashlightTogglePerformed;
        holdUVAction.performed     += OnHoldUVPressed;
        holdUVAction.canceled      += OnHoldUVReleased;

        // Garante que o visual local reflete o estado inicial replicado
        ApplyNormalVisual(normalOn);
        ApplyUVVisual(uvOn);
    }

    void OnDisable()
    {
        if (isLocalPlayer && actions != null)
        {
            flashlightAction.performed -= OnFlashlightTogglePerformed;
            holdUVAction.performed     -= OnHoldUVPressed;
            holdUVAction.canceled      -= OnHoldUVReleased;
            actions.Player.Disable();
        }
    }

    // ===== INPUT callbacks (cliente local) =====

    void OnFlashlightTogglePerformed(InputAction.CallbackContext _)
    {
        if (!isLocalPlayer) return;
        CmdSetNormal(!normalOn);
    }

    void OnHoldUVPressed(InputAction.CallbackContext _)
    {
        if (!isLocalPlayer) return;
        Debug.Log("[PF] HoldUV pressed (cliente)");
        CmdRequestUV(true);
    }

    void OnHoldUVReleased(InputAction.CallbackContext _)
    {
        if (!isLocalPlayer) return;
        Debug.Log("[PF] HoldUV released (cliente)");
        CmdRequestUV(false);
    }

    // ===== Commands =====

    [Command]
    void CmdSetNormal(bool turnOn)
    {
        // Sem restrição de recurso
        normalOn = turnOn;
    }

    [Command]
    void CmdRequestUV(bool wantOn)
    {
        Debug.Log($"[PF][SV] CmdRequestUV({wantOn}) uvOn={uvOn} uvSeconds={uvSecondsRemaining:F2}");
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
            // Desliga sob comando do cliente (soltou o botão)
            if (uvOn)
            {
                uvOn = false;
                StopDrainIfNeeded();
            }
        }
    }

    [Command]
    void CmdUVHit(uint netId, Vector3 origin, float dt)
    {
        Debug.Log($"[PF][SV] Reportando UV no netId {netId} dt={dt:F2}");
        if (NetworkServer.spawned.TryGetValue(netId, out var identity))
        {
            var cap = identity.GetComponent<GhostCaptureable>();
            if (cap)
                cap.ServerApplyUVHit(origin, dt);
        }
    }


    void Update()
    {
        if (!isLocalPlayer) { Debug.Log("[PF] !isLocalPlayer"); return; }
        if (!uvOn)          { Debug.Log("[PF] UV OFF no cliente"); return; }
        if (!uvRayOrigin)   { Debug.Log("[PF] uvRayOrigin nulo"); return; }
        if (Time.time < nextUVReportTime) return;
        nextUVReportTime = Time.time + uvReportInterval;

        var ray = new Ray(uvRayOrigin.position, uvRayOrigin.forward);
        Debug.DrawRay(ray.origin, ray.direction * uvRayDistance, Color.magenta, 0.1f);
        if (Physics.SphereCast(ray, uvRayRadius, out var hit, uvRayDistance, uvLayerMask, QueryTriggerInteraction.Collide))
        {
            Debug.Log($"[PF] UV apontando para {hit.collider.name}");
            var cap = hit.collider.GetComponentInParent<GhostCaptureable>();
            if (cap)
            {
                Debug.Log($"[PF] UV atingiu fantasma {cap.name}");
                var id = cap.netIdentity;
                if (id) CmdUVHit(id.netId, uvRayOrigin.position, uvReportInterval);
                else     Debug.LogWarning("[PF] Ghost sem NetworkIdentity");
            }
            else Debug.LogWarning("[PF] Collider sem GhostCaptureable no parent");
        }
        else
        {
            Debug.Log("[PF] SphereCast não encontrou nada");
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
        // Consome tempo enquanto UV estiver ligada e houver tempo
        while (uvOn && uvSecondsRemaining > 0f)
        {
            uvSecondsRemaining -= Time.deltaTime;
            if (uvSecondsRemaining <= 0f)
            {
                uvSecondsRemaining = 0f;
                uvOn = false; // Auto-off quando acabar
                break;
            }
            yield return null;
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
