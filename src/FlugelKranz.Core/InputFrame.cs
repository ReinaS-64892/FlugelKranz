namespace FlugelKranz.Core;

public readonly record struct HandSample(
    RigidPose Pose,
    float Drag,
    float Turn,
    float DpadDown,
    bool IsTracked)
{
    public bool TrackpadForceActive { get; init; }
    public float TrackpadForce { get; init; }
}

public readonly record struct InputFrame(
    RigidPose Head,
    bool HeadTracked,
    HandSample Left,
    HandSample Right);
