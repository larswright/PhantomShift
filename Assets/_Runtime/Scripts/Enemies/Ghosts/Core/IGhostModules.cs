using UnityEngine;

public interface IGhostModule {
    void ServerInit(GhostCore core);
    void ServerTick(float dt);
}

public interface IMotion : IGhostModule {
    void SetGoal(Vector3 p);
}

public interface ICaptureable : IGhostModule {
    void ApplyUV(Vector3 origin, Vector3 dir, float dt);
    bool IsStunned { get; }
}

public interface IVacuumCapture : IGhostModule {
    void ApplyVacuum(uint playerNetId, Vector3 origin, Vector3 dir, float dt);
}

public interface IAbility : IGhostModule {
    void OnPulseStart();
    void OnStunStart();
    void OnStunEnd();
    void OnCaptureStart();
    void OnCaptureEnd(bool success);
}

