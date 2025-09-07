// Assets/Editor/SampleHouseSetupWizard.cs
// Gera ScriptableObjects em Assets/_Runtime/ProcGenProfiles/SampleHouse
// e configura um HouseBuilder na cena apontando para eles.

using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class SampleHouseSetupWizard : EditorWindow
{
    // -------- Caminho base onde os assets serão criados --------
    [Header("Output Folder")]
    public string basePath = "Assets/_Runtime/ProcGenProfiles/SampleHouse";

    // -------- Nomes dos prefabs (procura por nome no projeto) --------
    [Header("Prefab Names (exact, adjustable)")]
    public string foyer1Name = "Foyer1";
    public string foyer2Name = "Foyer2";
    public string living1Name = "Living1";
    public string living2Name = "Living2";
    public string kitchenName = "Kitchen";
    public string bathroomName = "Bathroom";
    public string hallName = "Hall";   // usado como segmento de corredor

    // -------- Parâmetros do HouseProgram --------
    [Header("Program Parameters")]
    [Range(4, 32)] public int targetRooms = 10;
    [Range(0f, 1f)] public float corridorShare = 0.20f;
    [Range(0, 4)] public int maxLoops = 1;
    public Vector2 cellSizeMeters = new Vector2(0.5f, 0.5f);

    // -------- Geração --------
    [Header("Generation")]
    public int seed = 12345;

    // -------- Refs em runtime --------
    private GameObject foyer1, foyer2, living1, living2, kitchen, bathroom, hall;

    // -------- Assets criados/atualizados --------
    private SocketProfile door80;
    private RoomArchetype aFoyer, aLiving, aKitchen, aBathroom;
    private PrefabCatalog catalog;
    private HouseProgram program;

    [MenuItem("Tools/ProcGen/Sample House Setup")]
    public static void Open()
    {
        var w = GetWindow<SampleHouseSetupWizard>("Sample House Setup");
        w.minSize = new Vector2(460, 520);
        w.Show();
    }

    void OnEnable()
    {
        if (seed == 0) seed = UnityEngine.Random.Range(1, int.MaxValue);
    }

    void OnGUI()
    {
        GUILayout.Label("Scriptable Objects + Scene Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        basePath = EditorGUILayout.TextField("Base Path", basePath);

        EditorGUILayout.Space();
        GUILayout.Label("Prefab Names", EditorStyles.miniBoldLabel);
        foyer1Name = EditorGUILayout.TextField("Foyer 1", foyer1Name);
        foyer2Name = EditorGUILayout.TextField("Foyer 2", foyer2Name);
        living1Name = EditorGUILayout.TextField("Living 1", living1Name);
        living2Name = EditorGUILayout.TextField("Living 2", living2Name);
        kitchenName = EditorGUILayout.TextField("Kitchen", kitchenName);
        bathroomName = EditorGUILayout.TextField("Bathroom", bathroomName);
        hallName = EditorGUILayout.TextField("Hall (Corridor Segment)", hallName);

        EditorGUILayout.Space();
        GUILayout.Label("Program", EditorStyles.miniBoldLabel);
        targetRooms = EditorGUILayout.IntSlider("Target Rooms", targetRooms, 4, 32);
        corridorShare = EditorGUILayout.Slider("Corridor Area Share", corridorShare, 0f, 1f);
        maxLoops = EditorGUILayout.IntSlider("Max Loops", maxLoops, 0, 4);
        cellSizeMeters = EditorGUILayout.Vector2Field("Cell Size (m)", cellSizeMeters);
        seed = EditorGUILayout.IntField("Seed", seed);

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate & Build (One-Click)", GUILayout.Height(40)))
        {
            try
            {
                RunAll();
                Debug.Log("[SampleHouseSetup] Pronto: assets criados/atualizados e casa gerada na cena.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[SampleHouseSetup] " + ex);
            }
        }
    }

    // ================== PIPELINE ==================
    void RunAll()
    {
        EditorUtility.DisplayProgressBar("SampleHouse", "Ensuring folders…", 0.10f);
        EnsureFolder(basePath);

        EditorUtility.DisplayProgressBar("SampleHouse", "Finding prefabs…", 0.20f);
        FindPrefabsByName(); // lança exceção se faltar algo crítico

        EditorUtility.DisplayProgressBar("SampleHouse", "Creating ScriptableObjects…", 0.50f);
        door80 = EnsureSocketProfile($"{basePath}/Door_80cm.asset", "Door_80cm");

        aFoyer = EnsureArchetype($"{basePath}/Foyer.asset", "Foyer", 1, 1, new Vector2Int(3, 3), new Vector2Int(4, 4));
        aLiving = EnsureArchetype($"{basePath}/Living.asset", "Living", 1, 2, new Vector2Int(5, 5), new Vector2Int(7, 7));
        aKitchen = EnsureArchetype($"{basePath}/Kitchen.asset", "Kitchen", 1, 2, new Vector2Int(4, 4), new Vector2Int(6, 6));
        aBathroom = EnsureArchetype($"{basePath}/Bathroom.asset", "Bathroom", 1, 3, new Vector2Int(3, 3), new Vector2Int(4, 4));

        catalog = EnsureCatalog($"{basePath}/PrefabCatalog.asset");
        program = EnsureProgram($"{basePath}/HouseProgram.asset");

        EditorUtility.DisplayProgressBar("SampleHouse", "Setting up HouseBuilder…", 0.75f);
        var builder = EnsureBuilderInScene(program, catalog, seed);

        EditorUtility.DisplayProgressBar("SampleHouse", "Saving…", 0.90f);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Gera
        builder.Build();

        EditorUtility.ClearProgressBar();
        Selection.activeObject = builder.gameObject;
    }

    // ================== HELPERS: Assets ==================
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{cur}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    void FindPrefabsByName()
    {
        foyer1 = FindPrefab(foyer1Name);
        foyer2 = FindPrefab(foyer2Name);
        living1 = FindPrefab(living1Name);
        living2 = FindPrefab(living2Name);
        kitchen = FindPrefab(kitchenName);
        bathroom = FindPrefab(bathroomName);
        hall = FindPrefab(hallName);

        var missing = new List<string>();
        if (!foyer1) missing.Add(foyer1Name);
        if (!foyer2) missing.Add(foyer2Name);
        if (!living1) missing.Add(living1Name);
        if (!living2) missing.Add(living2Name);
        if (!kitchen) missing.Add(kitchenName);
        if (!bathroom) missing.Add(bathroomName);
        if (!hall) missing.Add(hallName);

        if (missing.Count > 0)
            throw new Exception("Prefabs não encontrados (ajuste os nomes na janela): " + string.Join(", ", missing));
    }

    static GameObject FindPrefab(string name)
    {
        var guids = AssetDatabase.FindAssets($"t:prefab {name}");
        // Preferência por nome exato
        foreach (var g in guids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go != null && string.Equals(go.name, name, StringComparison.OrdinalIgnoreCase))
                return go;
        }
        // Fallback: primeiro que bater
        foreach (var g in guids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go != null) return go;
        }
        return null;
    }

    SocketProfile EnsureSocketProfile(string path, string tag)
    {
        var so = AssetDatabase.LoadAssetAtPath<SocketProfile>(path);
        if (so == null)
        {
            so = ScriptableObject.CreateInstance<SocketProfile>();
            AssetDatabase.CreateAsset(so, path);
        }
        so.Tag = tag;
        so.CompatibleTags = new[] { tag };
        EditorUtility.SetDirty(so);
        return so;
    }

    static RoomArchetype EnsureArchetype(
        string path, string id, int min, int max, Vector2Int sizeMin, Vector2Int sizeMax,
        string[] mustAdj = null, string[] preferAdj = null, float weight = 0.5f)
    {
        var a = AssetDatabase.LoadAssetAtPath<RoomArchetype>(path);
        if (a == null)
        {
            a = ScriptableObject.CreateInstance<RoomArchetype>();
            AssetDatabase.CreateAsset(a, path);
        }
        a.Id = id;
        a.DisplayName = id;
        a.MinCount = min;
        a.MaxCount = max;
        a.SizeMin = sizeMin;
        a.SizeMax = sizeMax;
        a.MustBeAdjacentTo = mustAdj;
        a.PreferAdjacentTo = preferAdj;
        a.Weight = weight;
        EditorUtility.SetDirty(a);
        return a;
    }

    PrefabCatalog EnsureCatalog(string path)
    {
        var c = AssetDatabase.LoadAssetAtPath<PrefabCatalog>(path);
        if (c == null)
        {
            c = ScriptableObject.CreateInstance<PrefabCatalog>();
            AssetDatabase.CreateAsset(c, path);
        }

        c.Rooms = new[] {
            new PrefabCatalog.RoomEntry { RoomId = "Foyer",    Prefabs = FilterNull(new[]{ foyer1, foyer2 }) },
            new PrefabCatalog.RoomEntry { RoomId = "Living",   Prefabs = FilterNull(new[]{ living1, living2 }) },
            new PrefabCatalog.RoomEntry { RoomId = "Kitchen",  Prefabs = FilterNull(new[]{ kitchen }) },
            new PrefabCatalog.RoomEntry { RoomId = "Bathroom", Prefabs = FilterNull(new[]{ bathroom }) },
        };

        c.Corridors = new PrefabCatalog.CorridorEntry
        {
            Prefabs = FilterNull(new[] { hall })
        };

        EditorUtility.SetDirty(c);
        return c;
    }

    HouseProgram EnsureProgram(string path)
    {
        var p = AssetDatabase.LoadAssetAtPath<HouseProgram>(path);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<HouseProgram>();
            AssetDatabase.CreateAsset(p, path);
        }
        // ordem não importa
        p.Archetypes = new[] { aFoyer, aLiving, aKitchen, aBathroom };
        p.CorridorAreaShare = corridorShare;
        p.MaxLoops = maxLoops;
        p.TargetRooms = targetRooms;
        p.CellSizeMeters = cellSizeMeters;
        EditorUtility.SetDirty(p);
        return p;
    }

    // ================== HELPERS: Cena / Builder ==================
    HouseBuilder EnsureBuilderInScene(HouseProgram p, PrefabCatalog c, int seedValue)
    {
        if (!SceneManager.GetActiveScene().isLoaded)
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }

        var builder = GameObject.FindObjectOfType<HouseBuilder>();
        if (builder == null)
        {
            var go = new GameObject("HouseBuilder");
            builder = go.AddComponent<HouseBuilder>();
        }

        // Root
        var root = builder.transform.Find("GeneratedRoot");
        if (root == null)
        {
            var r = new GameObject("GeneratedRoot");
            r.transform.SetParent(builder.transform, false);
            root = r.transform;
        }

        // Atribuições
        builder.Program = p;
        builder.Catalog = c;
        builder.Root = root;
        builder.Seed = seedValue;

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return builder;
    }

    static GameObject[] FilterNull(IEnumerable<GameObject> list) => list.Where(x => x != null).ToArray();
}
