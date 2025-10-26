using System;
using Mirror;
using UnityEngine;
using DunGen;

/// <summary>
/// Synchronizes DunGen's generation seed across server and clients using Mirror.
/// Attach to a GameObject that also has a RuntimeDungeon and a NetworkIdentity.
/// Ensures generation happens with the synchronized seed and logs success/failure.
/// </summary>
public class NetworkedDungeonSeedSync : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private RuntimeDungeon runtimeDungeon;

    // Seed synchronized from server to all clients
    [SyncVar(hook = nameof(OnSeedChanged))]
    private int seed;

    private bool applied;

    private void Awake()
    {
        if (runtimeDungeon == null)
            runtimeDungeon = GetComponent<RuntimeDungeon>();

        // Prevent local auto-generation; generation is driven after seed sync
        if (runtimeDungeon != null)
            runtimeDungeon.GenerateOnStart = false;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (seed == 0)
        {
            // Use a positive non-zero seed; DunGen accepts any int but avoid 0 for clarity
            seed = UnityEngine.Random.Range(1, int.MaxValue);
        }

        Debug.Log($"[Server] Dungeon seed set: {seed}");

        // Apply immediately on server
        ApplySeedAndGenerate(seed);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // If the seed already arrived with spawn, apply it once on the client
        if (!isServer && seed != 0 && !applied)
        {
            Debug.Log($"[Client] Initial seed received at spawn: {seed}");
            ApplySeedAndGenerate(seed);
        }
    }

    private void OnSeedChanged(int oldSeed, int newSeed)
    {
        string side = isServer ? "Server" : "Client";
        Debug.Log($"[{side}] Seed sync update: {oldSeed} -> {newSeed}");

        if (!applied)
        {
            ApplySeedAndGenerate(newSeed);
        }
    }

    [Server]
    public void ServerSetSeed(int newSeed)
    {
        if (!isServer)
            return;

        seed = newSeed == 0 ? 1 : newSeed; // avoid zero for readability
        Debug.Log($"[Server] Seed manually set to: {seed}");
        ApplySeedAndGenerate(seed);
    }

    private void ApplySeedAndGenerate(int s)
    {
        string side = isServer ? "Server" : "Client";

        if (runtimeDungeon == null)
        {
            Debug.LogError($"[{side}] RuntimeDungeon reference missing. Cannot generate. Seed: {s}");
            return;
        }

        try
        {
            runtimeDungeon.Generator.ShouldRandomizeSeed = false;
            runtimeDungeon.Generator.Seed = s;

            // If a generation is in progress, do nothing; otherwise generate
            if (!runtimeDungeon.Generator.IsGenerating)
            {
                runtimeDungeon.Generate();
            }

            applied = true;
            Debug.Log($"[{side}] Dungeon generation succeeded with seed {s}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{side}] Dungeon generation FAILED with seed {s}. Exception: {ex}");
        }
    }
}

