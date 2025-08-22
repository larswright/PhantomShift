using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class MinigameMarkerWindow : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Root para ligar/desligar a UI do minigame.")]
    public GameObject root;
    [Tooltip("0..1 no eixo X: posição atual do marker.")]
    public RectTransform marker;   // anchor/pivot central
    [Tooltip("0..1 faixa de acerto (janela).")]
    public RectTransform window;   // dimensionado em runtime

    [Header("Input")]
    public InputActionReference interactAction; // mesmo botão 'Interact'

    [Header("Callbacks")]
    public UnityEvent onRoundStart;
    public UnityEvent onRoundSuccess;
    public UnityEvent onRoundFail;
    public UnityEvent onAllSuccess;
    public UnityEvent onAllFail;

    // runtime
    private bool active;
    private float speed;      // unidades normalizadas por segundo
    private float winCenter;  // 0..1
    private float winWidth;   // 0..1
    private int targetAttempts;
    private int attemptsDone;
    private int successes;
    private int successesNeeded;

    private float x;          // 0..1, posição marker
    private int dir = +1;     // +1 indo pra direita, -1 pra esquerda

    void OnEnable()
    {
        if (interactAction && interactAction.action != null)
        {
            interactAction.action.performed += OnInteract;
            interactAction.action.Enable();
        }
    }
    void OnDisable()
    {
        if (interactAction && interactAction.action != null)
            interactAction.action.performed -= OnInteract;
    }

    public struct Params
    {
        public float markerSpeed;
        public float windowWidth;
        public int attempts;
        public int requiredSuccesses; // ex.: 1 (acertou = passou)
    }

    public void StartMinigame(Params p)
    {
        speed = Mathf.Max(0.01f, p.markerSpeed);
        winWidth = Mathf.Clamp01(p.windowWidth);
        targetAttempts = Mathf.Max(1, p.attempts);
        successesNeeded = Mathf.Max(1, p.requiredSuccesses);

        attemptsDone = 0;
        successes = 0;
        x = 0f;
        dir = +1;
        winCenter = 0.5f; // pode randomizar se quiser

        active = true;
        if (root) root.SetActive(true);
        LayoutWindow();
        onRoundStart?.Invoke();
    }

    public void StopMinigame()
    {
        active = false;
        if (root) root.SetActive(false);
    }

    void Update()
    {
        if (!active) return;

        // Move o marker ping-pong entre 0..1
        x += dir * speed * Time.deltaTime;
        if (x >= 1f) { x = 1f; dir = -1; }
        if (x <= 0f) { x = 0f; dir = +1; }

        if (marker)
        {
            var a = marker.anchorMin; var b = marker.anchorMax;
            a.x = b.x = x;
            marker.anchorMin = a; marker.anchorMax = b;
        }
    }

    void LayoutWindow()
    {
        if (!window) return;
        float half = winWidth * 0.5f;
        float min = Mathf.Clamp01(winCenter - half);
        float max = Mathf.Clamp01(winCenter + half);
        var a = window.anchorMin; var b = window.anchorMax;
        a.x = min; b.x = max;
        window.anchorMin = a; window.anchorMax = b;
    }

    void OnInteract(InputAction.CallbackContext _)
    {
        if (!active) return;

        attemptsDone++;
        bool hit = Mathf.Abs(x - winCenter) <= (winWidth * 0.5f);

        if (hit)
        {
            successes++;
            onRoundSuccess?.Invoke();
            if (successes >= successesNeeded)
            {
                onAllSuccess?.Invoke();
                StopMinigame();
                return;
            }
        }
        else
        {
            onRoundFail?.Invoke();
        }

        if (attemptsDone >= targetAttempts)
        {
            // encerrou sem atingir requiredSuccesses
            onAllFail?.Invoke();
            StopMinigame();
        }
    }
}

