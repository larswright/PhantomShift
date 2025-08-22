using UnityEngine;

[CreateAssetMenu(menuName = "PhantomShift/Ghost Archetype")]
public partial class GhostArchetype : ScriptableObject
{
    public enum Class { Weak, Possessor, Heavy }
    public Class ghostClass = Class.Weak;

    [Header("Stats")]
    public int maxHP = 100;
    public float moveSpeed = 3.0f;
    public float acceleration = 8f;
    public float angularSpeed = 240f;
    public float stoppingDistance = 0.2f;

    [Header("Wander")]
    public float wanderRadius = 12f;
    public float pathRecalcDistance = 0.6f;
    public Vector2 idlePauseRange = new Vector2(0.5f, 1.5f);
    public Vector2 roamIntervalRange = new Vector2(2f, 4f);

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
    public AnimationCurve capture_escapeCurve = AnimationCurve.EaseInOut(0,0,1,1);
    public AnimationCurve capture_stunCurve   = AnimationCurve.EaseInOut(0,1,1,0);
}
