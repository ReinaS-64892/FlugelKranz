using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Returns free-flight height and tilt to the infinite-walking baseline.</summary>
public sealed class InfiniteWalkingTransition
{
    public const float DurationSeconds = 1;
    private readonly RigidPose start;
    private readonly RigidPose target;
    private float elapsed;

    public InfiniteWalkingTransition(RigidPose current, RigidPose original)
    {
        if (!current.IsValid || !original.IsValid)
            throw new ArgumentException("Mode transition poses must be valid.");

        start = current;
        target = CreateTarget(current, original);
        Current = current;
    }

    public bool IsComplete => elapsed >= DurationSeconds;
    public RigidPose Current { get; private set; }

    public RigidPose Advance(float elapsedSeconds)
    {
        if (float.IsFinite(elapsedSeconds) && elapsedSeconds > 0)
            elapsed = MathF.Min(DurationSeconds, elapsed + elapsedSeconds);
        float progress = elapsed / DurationSeconds;
        float eased = progress * progress * (3 - 2 * progress);
        Current = new(
            Quaternion.Normalize(Quaternion.Slerp(start.Orientation, target.Orientation, eased)),
            Vector3.Lerp(start.Position, target.Position, eased));
        return Current;
    }

    public static RigidPose CreateTarget(RigidPose current, RigidPose original)
    {
        Quaternion difference = Quaternion.Normalize(
            current.Orientation * Quaternion.Conjugate(original.Orientation));
        Quaternion yaw = FreeFlightManipulator.TwistAroundAxis(difference, Vector3.UnitY);
        Quaternion orientation = Quaternion.Normalize(yaw * original.Orientation);
        Vector3 position = current.Position;
        position.Y = original.Position.Y;
        return new(orientation, position);
    }
}
