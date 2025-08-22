using Mirror;
using UnityEngine;
using UnityEngine.InputSystem; // Novo Input System

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
    public float gravity = -9.81f;   // Use valor negativo

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

    // estado interno
    float pitch;       // rotação X da câmera
    float yaw;         // rotação Y do corpo
    float yVelocity;   // velocidade vertical (gravidade/pulo)

    Vector2 moveInput; // W/A/S/D ou stick
    Vector2 lookInput; // delta do mouse ou stick direito
    bool sprintHeld;   // LeftShift ou equivalente
    bool jumpQueued;   // sobe 1 frame quando botão dispara

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

        // inicia yaw a partir da rotação atual
        yaw = transform.eulerAngles.y;

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

        Look();
        Move();
    }

    void Look()
    {
        // lookInput vem de Pointer.delta / RightStick, etc. (mapa "Look")
        // Ajuste de sensibilidade; manter * Time.deltaTime para suavizar.
        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;

        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // gira o corpo no Y
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        // gira a câmera no X (pitch)
        if (camRoot != null)
            camRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void Move()
    {
        // moveInput.x = A/D; moveInput.y = W/S (ou analógico)
        Vector3 inputDir = transform.right * moveInput.x + transform.forward * moveInput.y;
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        float speed = sprintHeld ? sprintSpeed : walkSpeed;

        // Gravidade e pulo
        if (controller.isGrounded && yVelocity < 0f)
            yVelocity = -2f; // "cola" no chão

        if (controller.isGrounded && jumpQueued)
        {
            yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpQueued = false; // consome o evento
        }

        yVelocity += gravity * Time.deltaTime;

        Vector3 velocity = inputDir * speed;
        velocity.y = yVelocity;

        controller.Move(velocity * Time.deltaTime);
    }
}
