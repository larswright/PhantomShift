using UnityEngine;

public partial class GhostArchetype
{
    [Header("Capture – Minigame (Marker/Window)")]
    [Range(1,3)] public int mg_rounds = 3;        // quantos checkpoints (ex.: 1..3)
    [Tooltip("Velocidade do marker por round (0..1 normalizado por segundo, ping-pong).")]
    public float[] mg_markerSpeed = new float[] { 1.2f, 1.6f, 2.0f };
    [Tooltip("Largura da janela de acerto por round (0..1).")]
    public float[] mg_windowWidth = new float[] { 0.22f, 0.18f, 0.14f };
    [Tooltip("Quantas tentativas dentro do mesmo round (ex.: 1,1,2).")]
    public int[] mg_attempts = new int[] { 1, 1, 1 };
}

