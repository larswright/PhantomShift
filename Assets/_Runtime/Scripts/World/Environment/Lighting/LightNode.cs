using UnityEngine;

/// <summary>
/// Lightweight node representing a controllable light in the scene.
/// </summary>
[DisallowMultipleComponent]
public class LightNode : MonoBehaviour
{
    [SerializeField] private Light lightComp;
    [SerializeField] private Renderer emissiveRenderer;
    [SerializeField, ColorUsage(true,true)] private Color emissiveOn = Color.white;
    [SerializeField] private float baseIntensity = 1f;
    [SerializeField] private int costUnits = 1; // cost per light for generator capacity
    [SerializeField] private bool startsOn = false;

    static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock mpb;
    int index;
    bool isOn;

    public int Index => index;
    public int Cost => costUnits;

    public void Init(int idx)
    {
        index = idx;
        if (emissiveRenderer) mpb = new MaterialPropertyBlock();
        SetOn(startsOn, immediate:true);
    }

    public void SetOn(bool on, bool immediate=false)
    {
        isOn = on;
        if (lightComp) lightComp.enabled = on;
        if (emissiveRenderer)
        {
            emissiveRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionColorID, on ? emissiveOn : Color.black);
            emissiveRenderer.SetPropertyBlock(mpb);
        }
        if (lightComp && immediate) lightComp.intensity = baseIntensity;
    }

    // Used during flicker, toggling without material allocations
    public void SetFlickerState(bool on)
    {
        if (lightComp) lightComp.enabled = on;
        if (emissiveRenderer)
        {
            emissiveRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionColorID, on ? emissiveOn : Color.black);
            emissiveRenderer.SetPropertyBlock(mpb);
        }
    }
}
