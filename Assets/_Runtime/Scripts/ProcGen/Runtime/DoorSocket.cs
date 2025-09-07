using UnityEngine;

public class DoorSocket : MonoBehaviour {
    public SocketProfile Profile;
    public Vector3 NormalLocal = Vector3.forward; // direção de saída
    public Vector3Int GridAnchor;                 // âncora para snapping
}

