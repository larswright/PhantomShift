using Mirror;
using UnityEngine;
using System.Collections.Generic;
using Game.Interaction;

/// <summary>
/// Central manager for a set of light nodes, controlling power state and flicker.
/// Implements <see cref="IInteractable"/> allowing players to toggle the generator.
/// </summary>
[DisallowMultipleComponent]
public class GeneratorController : NetworkBehaviour, IInteractable
{
    [Header("Config")]
    [SerializeField] private int capacityUnits = 20;     // total capacity cost the generator can handle
    [SerializeField, Range(0f,1f)] private float warnPct = 0.75f;
    [SerializeField] private float flickerHz = 8f;

    [Header("Refs")]
    [SerializeField] private List<LightNode> nodes = new List<LightNode>();

    // Networked state
    [SyncVar(hook = nameof(OnOnChanged))] private bool isOn;
    [SyncVar] private int currentLoad;
    [SyncVar] private bool flickerActive;
    [SyncVar] private int flickerSeed;

    float nextTick;   // client-side timing
    int tickCount;    // client-side

    void Awake()
    {
        for (int i = 0; i < nodes.Count; i++)
            nodes[i]?.Init(i);
    }

    // === IInteractable ===
    public string GetInteractText() => isOn ? "Desligar Gerador" : "Ligar Gerador";
    public bool CanInteract(GameObject interactor) => true;

    [Server]
    public void ServerInteract(GameObject interactor)
    {
        SetGenerator(!isOn);
    }

    // === Server logic ===
    [Server]
    void SetGenerator(bool turnOn)
    {
        isOn = turnOn;

        if (!isOn)
        {
            currentLoad = 0;
            flickerActive = false;
            RpcApplyAll(false);
            return;
        }

        currentLoad = 0;
        foreach (var n in nodes)
            currentLoad += (n ? n.Cost : 0);

        float pct = capacityUnits > 0 ? (float)currentLoad / capacityUnits : 0f;

        if (pct >= 1f)
        {
            isOn = false;
            currentLoad = 0;
            flickerActive = false;
            RpcApplyAll(false);
            return;
        }

        RpcApplyAll(true);

        bool willFlicker = pct >= warnPct;
        flickerActive = willFlicker;
        if (willFlicker)
        {
            flickerSeed = Random.Range(int.MinValue, int.MaxValue);
            RpcStartFlicker(flickerSeed, (float)NetworkTime.time);
        }
        else
        {
            RpcStopFlicker();
        }
    }

    // === Hooks/RPCs ===
    void OnOnChanged(bool _, bool now) { }

    [ClientRpc]
    void RpcApplyAll(bool on)
    {
        foreach (var n in nodes)
            if (n) n.SetOn(on, immediate:true);
    }

    [ClientRpc]
    void RpcStartFlicker(int seed, float startTime)
    {
        flickerActive = true;
        flickerSeed = seed;
        tickCount = 0;
        nextTick = Time.unscaledTime;
    }

    [ClientRpc]
    void RpcStopFlicker()
    {
        flickerActive = false;
        bool on = isOn;
        foreach (var n in nodes)
            if (n) n.SetOn(on, immediate:true);
    }

    // === Client-side flicker tick ===
    void Update()
    {
        if (!flickerActive) return;

        if (Time.unscaledTime >= nextTick)
        {
            nextTick = Time.unscaledTime + (1f / Mathf.Max(1f, flickerHz));
            tickCount++;

            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (!n) continue;

                uint h = (uint)(flickerSeed) ^ ((uint)i * 374761393u) ^ ((uint)tickCount * 668265263u);
                h = (h ^ (h >> 13)) * 1274126177u;
                bool on = (h & 1u) == 1u;
                n.SetFlickerState(on);
            }
        }
    }
}
