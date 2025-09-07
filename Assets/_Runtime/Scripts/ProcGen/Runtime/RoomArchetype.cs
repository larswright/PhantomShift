using UnityEngine;

[CreateAssetMenu(menuName="PhantomShift/ProcGen/Room Archetype")]
public class RoomArchetype : ScriptableObject {
    [Header("Identidade")]
    public string Id;                          // "Bedroom", "Bathroom", "Kitchen"...
    public string DisplayName;

    [Header("Contagem")]
    public int MinCount = 0;
    public int MaxCount = 10;

    [Header("Tamanho (em células de grade)")]
    public Vector2Int SizeMin = new Vector2Int(3, 3);
    public Vector2Int SizeMax = new Vector2Int(6, 5);

    [Header("Adjacências")]
    public string[] MustBeAdjacentTo;          // Hard constraint (ex.: Bathroom ~ Bedroom)
    public string[] PreferAdjacentTo;          // Soft (peso)

    [Header("Sorteio")]
    [Range(0f, 1f)] public float Weight = 0.5f;
}

