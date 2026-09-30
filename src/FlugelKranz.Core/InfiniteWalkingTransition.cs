using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Returns free-flight height and tilt to the infinite-walking baseline.</summary>
public sealed class InfiniteWalkingTransition : MovementTransition
{
    private readonly RigidPose original;
    private readonly Vector3 turnPivot;

    public InfiniteWalkingTransition(
        RigidPose current,
        RigidPose original,
        InfiniteWalkingManipulator? destination = null,
        Vector3 turnPivot = default)
        : base(current, CreateTarget(current, original, turnPivot),
            destination ?? new InfiniteWalkingManipulator(current), turnPivot)
    {
        this.original = original;
        this.turnPivot = turnPivot;
    }

    public override MovementMode Interrupt(RigidPose appliedOffset) =>
        new InfiniteWalkingTransition(appliedOffset, original, (InfiniteWalkingManipulator)Destination, turnPivot);

    public override string StatusMessage => "無限歩行モードへ戻しています…";

    public static RigidPose CreateTarget(RigidPose current, RigidPose original, Vector3 turnPivot = default)
    {
        if (!current.IsValid || !original.IsValid ||
            !float.IsFinite(turnPivot.X) || !float.IsFinite(turnPivot.Y) || !float.IsFinite(turnPivot.Z))
            throw new ArgumentException("Mode transition poses must be valid.");
        Quaternion difference = Quaternion.Normalize(
            current.Orientation * Quaternion.Conjugate(original.Orientation));
        Quaternion yaw = RotationMath.TwistAroundAxis(difference, Vector3.UnitY);
        Quaternion orientation = Quaternion.Normalize(yaw * original.Orientation);
        // Physical pivot -> Monado root first. Remove tilt around that pivot,
        // then return its root-space Y to the connection baseline without changing X/Z.
        Vector3 landingPivot = current.Transform(turnPivot);
        landingPivot.Y = original.Transform(turnPivot).Y;
        Vector3 position = landingPivot - Vector3.Transform(turnPivot, orientation);
        // Root-space yaw leaves Y unchanged. Preserve the exact baseline height
        // rather than retaining roundoff from the pivot compensation.
        position.Y = original.Position.Y;
        return new(orientation, position);
    }
}
