using UnityEngine;

[CreateAssetMenu(menuName="PhantomShift/ProcGen/House Program")]
public class HouseProgram : ScriptableObject {
    public RoomArchetype[] Archetypes;

    [Header("Limites globais")]
    [Range(0f, 1f)] public float CorridorAreaShare = 0.2f; // 15–25%
    public int MaxLoops = 1;                               // 0–2
    public int TargetRooms = 12;                           // casa média
    public Vector2 CellSizeMeters = new Vector2(0.5f, 0.5f);

    [Header("Regras de van / zona segura (do GDD)")]
    public float MinSpawnDistanceFromVan = 25f; // 25–35 m
    public float MaxSpawnDistanceClamp = 60f;   // controle superior
}

