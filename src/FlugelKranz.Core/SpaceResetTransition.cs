using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Returns one part of the active space offset to its normal flight baseline.</summary>
public sealed class SpaceResetTransition : MovementTransition
{
    private SpaceResetTransition(RigidPose start, RigidPose target, ManipulationMode destination, Vector3? pivot = null)
        : base(start, target, destination, pivot)
    {
    }

    public override string StatusMessage => Destination.ResetMessage;

    public static SpaceResetTransition CreateFreeFlight(
        RigidPose current,
        RigidPose head,
        Vector3 turnPivot,
        FreeFlightManipulator? destination = null)
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
            current,
            new(orientation, position),
            destination ?? new FreeFlightManipulator(current),
            turnPivot);
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
        return RotationMath.TwistAroundAxis(headInRoot, Vector3.UnitY);
    }

    public static SpaceResetTransition CreateInfiniteWalking(
        RigidPose current,
        RigidPose original,
        InfiniteWalkingManipulator? destination = null)
    {
        if (!current.IsValid || !original.IsValid)
            throw new ArgumentException("Height reset poses must be valid.");

        Vector3 position = current.Position;
        position.Y = original.Position.Y;
        return new(
            current,
            current with { Position = position },
            destination ?? new InfiniteWalkingManipulator(current));
    }
}
