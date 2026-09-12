using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Returns one part of the active space offset to its normal flight baseline.</summary>
public sealed class SpaceResetTransition
{
    public const float DurationSeconds = 1;
    private readonly RigidPose start;
    private readonly RigidPose target;
    private readonly Vector3 pivot;
    private readonly Vector3 pivotInRoot;
    private readonly bool keepPivotFixed;
    private float elapsed;

    private SpaceResetTransition(
        FlightMode mode,
        RigidPose start,
        RigidPose target,
        Vector3 pivot = default,
        bool keepPivotFixed = false)
    {
        Mode = mode;
        this.start = start;
        this.target = target;
        this.pivot = pivot;
        this.keepPivotFixed = keepPivotFixed;
        pivotInRoot = start.Transform(pivot);
        Current = start;
    }

    public FlightMode Mode { get; }
    public bool IsComplete => elapsed >= DurationSeconds;
    public RigidPose Current { get; private set; }

    public static SpaceResetTransition CreateFreeFlight(
        RigidPose current,
        RigidPose head,
        Vector3 turnPivot)
    {
        if (!current.IsValid || !head.IsValid ||
            !float.IsFinite(turnPivot.X) ||
            !float.IsFinite(turnPivot.Y) ||
            !float.IsFinite(turnPivot.Z))
            throw new ArgumentException("Horizontal reset poses must be valid.");

        Quaternion headInRoot = Quaternion.Normalize(current.Orientation * head.Orientation);
        Quaternion levelHead = LevelHeadOrientation(headInRoot);
        Quaternion orientation = Quaternion.Normalize(
            levelHead * Quaternion.Conjugate(head.Orientation));
        Vector3 pivotInRoot = current.Transform(turnPivot);
        Vector3 position = pivotInRoot - Vector3.Transform(turnPivot, orientation);
        return new(
            FlightMode.FreeFlight,
            current,
            new(orientation, position),
            turnPivot,
            keepPivotFixed: true);
    }

    private static Quaternion LevelHeadOrientation(Quaternion headInRoot)
    {
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, headInRoot);
        forward.Y = 0;
        if (forward.LengthSquared() > 0.0000001f)
        {
            forward = Vector3.Normalize(forward);
            float yaw = MathF.Atan2(forward.X, forward.Z);
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        }

        // Looking exactly up or down has no horizontal forward direction. Preserve
        // the closest yaw component instead of choosing an arbitrary half turn.
        return FreeFlightManipulator.TwistAroundAxis(headInRoot, Vector3.UnitY);
    }

    public static SpaceResetTransition CreateInfiniteWalking(
        RigidPose current,
        RigidPose original)
    {
        if (!current.IsValid || !original.IsValid)
            throw new ArgumentException("Height reset poses must be valid.");

        Vector3 position = current.Position;
        position.Y = original.Position.Y;
        return new(
            FlightMode.InfiniteWalking,
            current,
            current with { Position = position });
    }

    public RigidPose Advance(float elapsedSeconds)
    {
        if (float.IsFinite(elapsedSeconds) && elapsedSeconds > 0)
            elapsed = MathF.Min(DurationSeconds, elapsed + elapsedSeconds);
        if (IsComplete)
            return Current = target;

        float progress = elapsed / DurationSeconds;
        float eased = progress * progress * (3 - 2 * progress);
        Quaternion orientation = Quaternion.Normalize(
            Quaternion.Slerp(start.Orientation, target.Orientation, eased));
        Vector3 position = keepPivotFixed
            ? pivotInRoot - Vector3.Transform(pivot, orientation)
            : Vector3.Lerp(start.Position, target.Position, eased);
        return Current = new(orientation, position);
    }
}
