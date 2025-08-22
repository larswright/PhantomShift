using UnityEngine;

[CreateAssetMenu(menuName = "PhantomShift/Ghost Archetype")]
public partial class GhostArchetype : ScriptableObject
{
    public enum Class { Weak, Possessor, Heavy }
    public Class ghostClass = Class.Weak;

    [System.Serializable]
    public struct MotionStats
    {
        public float moveSpeed;
        public float acceleration;
        public float angularSpeed;
        public float stoppingDistance;
    }

    [Header("Stats")]
    public int maxHP = 100;
    public MotionStats defaultStats = new MotionStats
    {
        moveSpeed = 3.6f,
        acceleration = 40f,
        angularSpeed = 900f,
        stoppingDistance = 0.2f
    };
    public MotionStats fleeStats = new MotionStats
    {
        moveSpeed = 7.5f,
        acceleration = 50f,
        angularSpeed = 1000f,
        stoppingDistance = 0.1f
    };

    [Header("Wander")]
    public float wanderRadius = 12f;
    public float pathRecalcDistance = 0.6f;
    public Vector2 idlePauseRange = new Vector2(0.5f, 1.5f);
    public Vector2 roamIntervalRange = new Vector2(2f, 4f);

    [Header("Erratic Motion")]
    public bool erratic = true;
    [Min(0.5f)] public float zigStep = 2.0f;                 // avanço por decisão
    [Min(0.0f)] public float zigAmplitude = 2.0f;            // deslocamento lateral
    public Vector2 zigPeriodRange = new Vector2(0.20f, 0.45f); // intervalo entre decisões
    [Range(0f,1f)] public float hardTurnChance = 0.35f;      // chance de virar "seco" 90°
    public float hardTurnDegrees = 90f;                      // ângulo da virada dura

    [Range(0f,1f)] public float burstChance = 0.25f;         // chance de "sprint"
    public float burstMultiplier = 1.8f;                      // multiplicador do sprint
    public Vector2 burstDurationRange = new Vector2(0.25f, 0.6f); // duração do sprint

    [Header("NavMesh")]
    public int areaMask = ~0; // todas as áreas
    public float sampleMaxDistance = 2.0f;

    [Header("Capture (UV/Stun)")]
    public float capture_uvSecondsToStun = 2.0f;
    public float capture_stunSeconds = 3.0f;
    public float capture_exposureGraceWindow = 0.6f;
    public float capture_fleeDecisionInterval = 0.25f;
    public Vector2 capture_fleeStepRange = new Vector2(1.5f, 3.5f);
    public float capture_fleeRadius = 10f;

    [Header("Capture – Aggressive Flee")]
    public float capture_fleeSpeedMultiplier = 2.2f;
    public Vector2 capture_fleeSegmentDuration = new Vector2(0.8f, 1.4f); // “corrida” contínua
    [Range(0,1)] public float capture_hardTurnChance = 0.40f;
    public Vector2 capture_hardTurnDegrees = new Vector2(60f, 140f); // viradas secas aleatórias
}
