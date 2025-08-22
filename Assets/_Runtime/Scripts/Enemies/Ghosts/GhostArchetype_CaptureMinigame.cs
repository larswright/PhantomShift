using UnityEngine;

public partial class GhostArchetype
{
    [Header("Capture – Vacuum/Minigame")]
    [Tooltip("Tempo contínuo de sucção para capturar (s), ignorando minigames.")]
    public float capture_vacuumSecondsToCapture = 4.0f;

    [Range(0,3), Tooltip("Qtde de minigames: 0..3. Se 1: gatilho em 33%; 2: 33% e 66%; 3: 33%/66%/99%.")]
    public int capture_minigameCount = 3;

    [Tooltip("Velocidade do handle do minigame (u/s, 0..1 por segundo). Valores mais altos = mais difícil.")]
    public float capture_minigameHandleSpeed = 1.75f;

    [Range(0.01f, 0.20f), Tooltip("Janela de acerto (+/-) em fração da barra (ex.: 0.06 = ±6%)")]
    public float capture_minigameHitWindow = 0.06f;
}

