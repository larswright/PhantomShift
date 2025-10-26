using Mirror;
using UnityEngine;
using UnityEngine.InputSystem; // Novo Input System

// IMPORTANTES (Prefab):
// 1) Adicione NetworkTransform ao prefab do Player e deixe AUTORIDADE DO SERVIDOR (client authority DESMARCADO).
// 2) Deixe Camera e AudioListener desabilitados no prefab; só o dono habilita em OnStartLocalPlayer.
// 3) O movimento acontece SOMENTE no servidor; o cliente envia inputs via [Command].
// 4) Opcional: reduza o rate de comandos (ex.: enviar em FixedUpdate) se precisar poupar banda.

[RequireComponent(typeof(CharacterController))]
public class PlayerFPS : NetworkBehaviour
{
    [Header("Referências")]
    public CharacterController controller;
    public Transform camRoot;        // objeto que gira no eixo X (pitch)
    public Camera cam;               // Camera filha (desabilitada no prefab)
    public AudioListener audioListener; // Desabilitado no prefab

    [Header("Movimento")]
    [Tooltip("Velocidade andando (m/s)")]
    public float walkSpeed = 6f;
    [Tooltip("Velocidade correndo (m/s)")]
    public float sprintSpeed = 9f;
    public float jumpHeight = 1.4f;
    public float gravity = -9.81f;   // Use valor NEGATIVO

    [Header("Câmera")]
    public float mouseSensitivity = 120f;
    public float minPitch = -80f;
    public float maxPitch = 80f;
    public bool lockCursor = true;

    // ---- Novo Input System ----
    InputSystem_Actions actions;
    InputAction moveAction;
    InputAction lookAction;
    InputAction sprintAction;
    InputAction jumpAction;

    // ======== ESTADO LOCAL (apenas cliente dono) ========
    float localPitch;   // rotação X da câmera (não sincroniza)
    float localYaw;     // yaw visual imediato; servidor recebe via Cmd e domina o transform

    Vector2 moveInput; // input bruto local (WASD / analógico)
    Vector2 lookInput; // delta mouse / stick direito
    bool sprintHeld;   // Shift
    bool jumpQueued;   // sobe 1 frame no performed

    // ======== ESTADO AUTORITATIVO (servidor) ========
    Vector2 s_moveInput;
    bool s_sprintHeld;
    bool s_jumpQueued;
    float s_yaw;        // yaw autoritativo do corpo
    float s_yVelocity;  // gravidade/pulo no servidor

    void Reset()
    {
        controller = GetComponent<CharacterController>();
        if (transform.Find("CamRoot") != null)
        {
            camRoot = transform.Find("CamRoot");
            var camT = camRoot.Find("Cam");
            if (camT != null) cam = camT.GetComponent<Camera>();
            if (camT != null) audioListener = camT.GetComponent<AudioListener>();
        }
    }

    void Awake()
    {
        // Instancia e referencia as actions do mapa Player
        actions = new InputSystem_Actions(); // wrapper gerado
        moveAction = actions.Player.Move;
        lookAction = actions.Player.Look;
        sprintAction = actions.Player.Sprint;
        jumpAction = actions.Player.Jump;

        // Callbacks (eventos). Evita polling manual e dá suporte a teclado/gamepad/mouse.
        moveAction.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        moveAction.canceled += ctx => moveInput = Vector2.zero;

        lookAction.performed += ctx => lookInput = ctx.ReadValue<Vector2>();
        lookAction.canceled += ctx => lookInput = Vector2.zero;

        sprintAction.performed += ctx => sprintHeld = ctx.ReadValueAsButton();
        sprintAction.canceled += ctx => sprintHeld = false;

        // Jump: fila um pulo no momento do "performed" (borda de subida)
        jumpAction.performed += ctx => jumpQueued = true;
    }

    public override void OnStartLocalPlayer()
    {
        // Ativa câmera e áudio só no dono
        if (cam != null) cam.enabled = true;
        if (audioListener != null) audioListener.enabled = true;

        // Cursor travado para FPS
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // Inicializa yaw local a partir da rotação atual
        localYaw = transform.eulerAngles.y;

        // Habilita ações somente no local player
        actions.Player.Enable();
    }

    void OnEnable()
    {
        if (isLocalPlayer)
            actions?.Player.Enable();
    }

    void OnDisable()
    {
        if (isLocalPlayer)
        {
            actions?.Player.Disable();

            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }

    void OnDestroy()
    {
        actions?.Dispose();
    }

    void Update()
    {
        if (!isLocalPlayer) return;

        // 1) Look local (responsivo)
        LookLocal();

        // 2) Envia inputs ao servidor
        CmdSetInputs(moveInput, sprintHeld, jumpQueued, localYaw);
        jumpQueued = false; // consome o evento local
    }

    void LookLocal()
    {
        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;

        localYaw += mouseX;
        localPitch -= mouseY;
        localPitch = Mathf.Clamp(localPitch, minPitch, maxPitch);

        // Yaw visual imediato (cliente). O servidor sobrescreve via NetworkTransform.
        transform.rotation = Quaternion.Euler(0f, localYaw, 0f);

        // Pitch só na câmera local
        if (camRoot != null)
            camRoot.localRotation = Quaternion.Euler(localPitch, 0f, 0f);
    }

    // ======== LOOP DE MOVIMENTO NO SERVIDOR ========
    [ServerCallback]
    void FixedUpdate()
    {
        if (!isServer) return;

        // Aplica yaw autoritativo
        transform.rotation = Quaternion.Euler(0f, s_yaw, 0f);

        // Direção no plano XZ baseada no yaw do servidor
        Vector3 inputDir = transform.right * s_moveInput.x + transform.forward * s_moveInput.y;
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        float speed = s_sprintHeld ? sprintSpeed : walkSpeed;

        // Gravidade/pulo
        if (controller.isGrounded && s_yVelocity < 0f)
            s_yVelocity = -2f; // "cola" no chão

        if (controller.isGrounded && s_jumpQueued)
        {
            s_yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            s_jumpQueued = false; // consome no servidor
        }

        s_yVelocity += gravity * Time.fixedDeltaTime;

        Vector3 velocity = inputDir * speed;
        velocity.y = s_yVelocity;

        controller.Move(velocity * Time.fixedDeltaTime);
        // NetworkTransform replicará posição/rotação para todos os clientes
    }

    // ======== RECEBIMENTO DE INPUT DO DONO ========
    [Command]
    void CmdSetInputs(Vector2 move, bool sprint, bool jump, float newYaw)
    {
        s_moveInput = move;
        s_sprintHeld = sprint;
        if (jump) s_jumpQueued = true; // borda de subida
        s_yaw = newYaw;
    }
}
