using UnityEngine;

[CreateAssetMenu(menuName="PhantomShift/ProcGen/Socket Profile")]
public class SocketProfile : ScriptableObject {
    public string Tag;                  // "Door_80cm", "StairsUp", "StairsDown"
    public string[] CompatibleTags;     // conexões válidas
}

