using System.Numerics;

namespace FlugelKranz.Core;

public readonly record struct TrackerSample(string Serial, Vector3 Position, bool IsTracked)
{
    public bool IsUsable => IsTracked && float.IsFinite(Position.X)
        && float.IsFinite(Position.Y) && float.IsFinite(Position.Z);
}

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
    HandSample Right)
{
    // Unmodified reference floor in physical runtime root coordinates.
    public RigidPose FloorPose { get; init; } = RigidPose.Identity;
    public IReadOnlyList<TrackerSample>? Trackers { get; init; }
}
