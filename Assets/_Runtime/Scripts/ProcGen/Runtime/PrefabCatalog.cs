using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(menuName="PhantomShift/ProcGen/Prefab Catalog")]
public class PrefabCatalog : ScriptableObject {
    [System.Serializable]
    public class RoomEntry {
        public string RoomId;           // casa com "Bedroom", "Bathroom", etc.
        public GameObject[] Prefabs;    // variações de forma e portas
    }

    [System.Serializable]
    public class CorridorEntry {
        public GameObject[] Prefabs;    // segmentos retos, L, T, cruz, fim
    }

    public RoomEntry[] Rooms;
    public CorridorEntry Corridors;

    public GameObject PickRoom(string roomId, System.Random rng) {
        var e = Rooms.FirstOrDefault(x => x.RoomId == roomId);
        if (e == null || e.Prefabs == null || e.Prefabs.Length == 0) return null;
        return e.Prefabs[rng.Next(e.Prefabs.Length)];
    }

    public GameObject PickCorridor(System.Random rng) {
        if (Corridors?.Prefabs == null || Corridors.Prefabs.Length == 0) return null;
        return Corridors.Prefabs[rng.Next(Corridors.Prefabs.Length)];
    }
}

