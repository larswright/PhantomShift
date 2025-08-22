using System;
using UnityEngine;

/// <summary>
/// UI extremamente simples com OnGUI para o minigame:
/// - Barra [0..1]
/// - Handle que corre da esquerda p/ direita com velocidade configurável
/// - Marker fixo; jogador deve "bater" (Strike) próximo o suficiente
/// Integração:
///   PlayerVacuum chama Begin(...) e depois usa Interact para chamar Strike().
/// </summary>
public class CaptureMinigameUI : MonoBehaviour
{
    private uint ghostNetId;
    private int stageIndex;
    private float speed;
    private float marker;
    private float window;

    private float handle; // 0..1
    private Action<bool> onFinish;
    private bool active;

    private Rect rect;
    private const float W = 420f;
    private const float H = 96f;

    public static CaptureMinigameUI Spawn(PlayerVacuum owner)
    {
        var go = new GameObject("CaptureMinigameUI");
        var ui = go.AddComponent<CaptureMinigameUI>();
        DontDestroyOnLoad(go);
        return ui;
    }

    public void Begin(uint ghostNetId, int stageIndex, float speed, float markerPos01, float hitWindow01, Action<bool> onFinish)
    {
        this.ghostNetId = ghostNetId;
        this.stageIndex = stageIndex;
        this.speed      = Mathf.Max(0.05f, speed);
        this.marker     = Mathf.Clamp01(markerPos01);
        this.window     = Mathf.Clamp(hitWindow01, 0.01f, 0.25f);
        this.onFinish   = onFinish;

        this.handle = 0f;
        this.active = true;

        var sw = Screen.width; var sh = Screen.height;
        rect = new Rect((sw - W)/2f, sh*0.75f - H/2f, W, H); // parte baixa da tela
    }

    public void Strike()
    {
        if (!active) return;
        bool ok = Mathf.Abs(handle - marker) <= window;
        Finish(ok);
    }

    void Finish(bool ok)
    {
        active = false;
        onFinish?.Invoke(ok);
    }

    void Update()
    {
        if (!active) return;
        handle += speed * Time.deltaTime;
        if (handle >= 1f) handle -= 1f; // volta ao início
    }

    void OnGUI()
    {
        if (!active) return;

        GUI.depth = 0;
        GUI.Box(rect, GUIContent.none);

        // margem interna
        var inner = new Rect(rect.x + 12, rect.y + 24, rect.width - 24, 18);

        // fundo
        GUI.Box(inner, GUIContent.none);

        // marker
        float mx = inner.x + inner.width * marker;
        var markRect = new Rect(mx - 2, inner.y - 6, 4, inner.height + 12);
        GUI.Box(markRect, GUIContent.none);

        // janela de acerto (visual)
        float wx = inner.width * window;
        var winRect = new Rect(mx - wx, inner.y, wx*2f, inner.height);
        Color prev = GUI.color; GUI.color = new Color(0f,1f,0f,0.25f);
        GUI.DrawTexture(winRect, Texture2D.whiteTexture);
        GUI.color = prev;

        // handle
        float hx = inner.x + inner.width * handle;
        var hRect = new Rect(hx - 6, inner.y - 4, 12, inner.height + 8);
        GUI.Box(hRect, GUIContent.none);

        // texto
        var tRect = new Rect(rect.x, rect.y + 54, rect.width, 24);
        GUI.Label(tRect, $"Stage {stageIndex} • Pressione Interact no alvo");
    }
}

