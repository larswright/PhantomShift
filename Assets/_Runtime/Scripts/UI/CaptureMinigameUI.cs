using System;
using UnityEngine;
using UnityEngine.UI;

public class CaptureMinigameUI : MonoBehaviour
{
    [Header("Prefab discovery")]
    [Tooltip("Opcional: se nulo, será carregado de Resources/UI/CaptureMinigameCanvas")]
    public GameObject prefabRoot; // raiz do prefab com Canvas

    [Header("Bindings (no prefab)")]
    public RectTransform barArea;          // área horizontal [0..1]
    public RectTransform handleRT;         // barra/“cursor” que corre
    public RectTransform markerRT;         // marcador fixo
    public Image hitWindowImage;           // região de acerto (Image)
    public Text infoText;                  // texto "Stage X • ..."

    // ---- estado do minigame
    private uint ghostNetId;
    private int stageIndex;
    private float speed;   // unidades normalizadas por segundo
    private float marker;  // 0..1
    private float window;  // raio, 0..0.25
    private float handle;  // 0..1
    private Action<bool> onFinish;
    private bool active;

    private static CaptureMinigameUI _instance;

    // Mantém compatibilidade com PlayerVacuum: cria/retorna instância única
    public static CaptureMinigameUI Spawn(PlayerVacuum owner)
    {
        if (_instance) return _instance;

        // Tenta achar já na cena
        _instance = FindObjectOfType<CaptureMinigameUI>(true);
        if (_instance) { _instance.HideImmediate(); DontDestroyOnLoad(_instance.transform.root.gameObject); return _instance; }

        // Tenta via Resources
        var prefab = Resources.Load<GameObject>("UI/CaptureMinigameCanvas");
        if (prefab)
        {
            var go = Instantiate(prefab);
            DontDestroyOnLoad(go);
            _instance = go.GetComponentInChildren<CaptureMinigameUI>(true);
            if (_instance == null) _instance = go.AddComponent<CaptureMinigameUI>();
            _instance.HideImmediate();
            return _instance;
        }

        // Fallback: cria objeto raiz com Canvas (sem visuais configurados)
        var root = new GameObject("CaptureMinigameCanvas_Fallback");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        root.AddComponent<GraphicRaycaster>();
        DontDestroyOnLoad(root);

        _instance = root.AddComponent<CaptureMinigameUI>();
        _instance.prefabRoot = root;
        Debug.LogWarning("[CaptureMinigameUI] Prefab não encontrado (Resources/UI/CaptureMinigameCanvas). O minigame funcionará, mas sem visuais até você configurar o prefab.");
        return _instance;
    }

    public void Begin(uint ghostNetId, int stageIndex, float speed, float markerPos01, float hitWindow01, Action<bool> onFinish)
    {
        this.ghostNetId = ghostNetId;
        this.stageIndex = stageIndex;
        this.speed = Mathf.Max(0.05f, speed);
        this.marker = Mathf.Clamp01(markerPos01);
        this.window = Mathf.Clamp(hitWindow01, 0.01f, 0.25f);
        this.onFinish = onFinish;

        this.handle = 0f;
        this.active = true;

        if (infoText) infoText.text = $"Stage {stageIndex} • Pressione Interact no alvo";
        UpdateStaticVisuals();
        Show(true);
    }

    public void Strike()
    {
        if (!active) return;
        bool ok = Mathf.Abs(handle - marker) <= window;
        Finish(ok);
    }

    void Update()
    {
        if (!active) return;

        handle += speed * Time.unscaledDeltaTime;
        if (handle >= 1f) handle -= 1f;

        UpdateHandle();
    }

    // ---------- helpers de UI

    void Finish(bool ok)
    {
        active = false;
        Show(false);
        onFinish?.Invoke(ok);
    }

    void Show(bool v)
    {
        if (!prefabRoot && this is { }) prefabRoot = transform.root.gameObject;
        var rootGO = prefabRoot ? prefabRoot : gameObject;

        var cg = rootGO.GetComponent<CanvasGroup>();
        if (!cg) cg = rootGO.AddComponent<CanvasGroup>();
        cg.alpha = v ? 1f : 0f;
        cg.interactable = v;
        cg.blocksRaycasts = v;

        // mantém o MonoBehaviour habilitado enquanto ativo
        enabled = v;
    }

    void HideImmediate()
    {
        if (!prefabRoot && this is { }) prefabRoot = transform.root.gameObject;
        var rootGO = prefabRoot ? prefabRoot : gameObject;
        var cg = rootGO.GetComponent<CanvasGroup>();
        if (!cg) cg = rootGO.AddComponent<CanvasGroup>();
        cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false;
        enabled = false;
        active = false;
    }

    void UpdateStaticVisuals()
    {
        if (!barArea) return;

        // Marker: ancora exatamente em X=marker
        if (markerRT)
        {
            markerRT.anchorMin = new Vector2(marker, 0.5f);
            markerRT.anchorMax = new Vector2(marker, 0.5f);
            markerRT.anchoredPosition = Vector2.zero;
        }

        // Window (Image) ocupa [marker-window, marker+window]
        if (hitWindowImage)
        {
            var rt = hitWindowImage.rectTransform;
            float a = Mathf.Clamp01(marker - window);
            float b = Mathf.Clamp01(marker + window);
            rt.anchorMin = new Vector2(a, 0.5f);
            rt.anchorMax = new Vector2(b, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            // largura controlada pelas âncoras; preserva altura do prefab
            rt.sizeDelta = new Vector2(0f, rt.sizeDelta.y);
        }

        // Handle começa no 0
        UpdateHandle();
    }

    void UpdateHandle()
    {
        if (!handleRT || !barArea) return;
        handleRT.anchorMin = new Vector2(handle, 0.5f);
        handleRT.anchorMax = new Vector2(handle, 0.5f);
        handleRT.anchoredPosition = Vector2.zero;
    }
}
