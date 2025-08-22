using Mirror;
using UnityEngine;

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

    // estado interno
    float pitch;       // rotação X da câmera
    float yaw;         // rotação Y do corpo
    float yVelocity;   // velocidade vertical (gravidade/pulo)

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
    }

    void OnDisable()
    {
        if (isLocalPlayer && lockCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void Update()
    {
        // Apenas o dono processa entrada e move.
        if (!isLocalPlayer) return;

        Look();
        Move();
    }

    void Look()
    {
        // Usando Input Manager antigo (Axes padrão do Unity).
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        yaw   += mouseX;
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
        float h = Input.GetAxisRaw("Horizontal"); // A/D
        float v = Input.GetAxisRaw("Vertical");   // W/S
        bool isSprinting = Input.GetKey(KeyCode.LeftShift);

        Vector3 inputDir = (transform.right * h + transform.forward * v);
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        float speed = isSprinting ? sprintSpeed : walkSpeed;

        // Gravidade e pulo
        if (controller.isGrounded && yVelocity < 0f)
            yVelocity = -2f; // "cola" no chão

        if (controller.isGrounded && Input.GetButtonDown("Jump"))
            yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        yVelocity += gravity * Time.deltaTime;

        Vector3 velocity = inputDir * speed;
        velocity.y = yVelocity;

        controller.Move(velocity * Time.deltaTime);
    }
}
