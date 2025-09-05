using UnityEngine;
using UnityEngine.UI;
using Mirror;
using System.Collections;

/// <summary>
/// Indicador de bateria ultra enxuto:
/// - Sem Update por frame.
/// - Reage apenas quando cargas inteiras mudam (0..N).
/// - Mantém hierarquia estática; troca apenas alpha das imagens.
/// </summary>
[DisallowMultipleComponent]
public class UIBatteryIndicator : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("3 imagens (da esquerda p/ direita).")]
    [SerializeField] private Image[] bars = new Image[3];

    [Header("Visual")]
    [Tooltip("Alpha quando a barra está 'apagada' (0..1).")]
    [Range(0f,1f)] public float offAlpha = 0.25f;

    [Header("Binding")]
    [Tooltip("Vincular automaticamente ao PlayerFlashlight local (Mirror).")]
    public bool autoBindLocalFlashlight = true;

    private PlayerFlashlight pf;        // alvo local
    private int lastCharges = -1;       // cache p/ evitar trabalho redundante
    private readonly WaitForSecondsRealtime pollShort = new WaitForSecondsRealtime(0.25f);
    private readonly WaitForSecondsRealtime pollLong  = new WaitForSecondsRealtime(1.00f);

    void Awake()
    {
        // UI mais barata: sem raycast
        if (bars != null)
            for (int i = 0; i < bars.Length; i++)
                if (bars[i]) bars[i].raycastTarget = false;
    }

    void OnEnable()
    {
        TryAttach();
        StartCoroutine(PollLoop());
    }

    void OnDisable()
    {
        StopAllCoroutines();
        pf = null;
        lastCharges = -1;
    }

    void TryAttach()
    {
        if (pf && pf.isLocalPlayer) return;
        if (!autoBindLocalFlashlight) return;

        // Mirror: pega o jogador local de forma segura
        var localId = NetworkClient.localPlayer;
        if (localId)
        {
            var candidate = localId.GetComponent<PlayerFlashlight>();
            if (candidate) { pf = candidate; ForceRefresh(); return; }
        }

        // Fallback defensivo: procura qualquer PlayerFlashlight local na cena
        var all = FindObjectsOfType<PlayerFlashlight>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].isLocalPlayer) { pf = all[i]; ForceRefresh(); break; }
        }
    }

    IEnumerator PollLoop()
    {
        while (true)
        {
            if (!pf) { TryAttach(); yield return pollLong; continue; }

            // Lê somente o inteiro de cargas; nada de floats por tick.
            int charges = pf.GetUVCharges(); // 0..maxCharges
            if (charges != lastCharges)
            {
                ApplyCharges(charges);
                lastCharges = charges;
            }

            // Poll mais lento quando cheio/zerado (menos mudanças esperadas)
            yield return (charges == 0 || charges >= pf.maxCharges) ? pollLong : pollShort;
        }
    }

    void ForceRefresh()
    {
        lastCharges = -1;
        if (pf) ApplyCharges(pf.GetUVCharges());
    }

    void ApplyCharges(int charges)
    {
        if (bars == null) return;
        int max = bars.Length;
        if (charges < 0) charges = 0;
        if (charges > max) charges = max;

        for (int i = 0; i < max; i++)
        {
            var img = bars[i];
            if (!img) continue;

            // Alpha swap evita layout rebuilds (hierarquia fica estática).
            float a = (i < charges) ? 1f : offAlpha;
            var c = img.color;
            if (Mathf.Abs(c.a - a) > 0.001f)
            {
                c.a = a;
                img.color = c;
            }
        }
    }
}

