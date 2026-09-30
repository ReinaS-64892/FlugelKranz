using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Returns free-flight height and tilt to the infinite-walking baseline.</summary>
public sealed class InfiniteWalkingTransition : MovementTransition
{
    private readonly RigidPose original;

    public InfiniteWalkingTransition(RigidPose current, RigidPose original, InfiniteWalkingManipulator? destination = null)
        : base(current, CreateTarget(current, original), destination ?? new InfiniteWalkingManipulator(current))
    {
        this.original = original;
    }

    public override MovementMode Interrupt(RigidPose appliedOffset) =>
        new InfiniteWalkingTransition(appliedOffset, original, (InfiniteWalkingManipulator)Destination);

    public override string StatusMessage => "無限歩行モードへ戻しています…";

    public static RigidPose CreateTarget(RigidPose current, RigidPose original)
    {
        if (!current.IsValid || !original.IsValid)
            throw new ArgumentException("Mode transition poses must be valid.");
        Quaternion difference = Quaternion.Normalize(
            current.Orientation * Quaternion.Conjugate(original.Orientation));
        Quaternion yaw = RotationMath.TwistAroundAxis(difference, Vector3.UnitY);
        Quaternion orientation = Quaternion.Normalize(yaw * original.Orientation);
        Vector3 position = current.Position;
        position.Y = original.Position.Y;
        return new(orientation, position);
    }
}
