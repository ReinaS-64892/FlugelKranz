namespace FlugelKranz.Core;

/// <summary>Flight transforms in runtime root coordinates, retaining the runtime's device calibration.</summary>
public sealed record ReferenceSpaceTransform(RigidPose InitialReference, RigidPose Offset)
{
    // (D^-1 * S)^-1 * pose = S^-1 * D * pose: apply flight once to every device.
    public RigidPose ReferenceOffset => Offset.Inverse() * InitialReference;

    // Remove reference space and our flight transform; retain all driver-space calibration.
    public RigidPose ToPhysical(RigidPose referencePose) => ReferenceOffset * referencePose;

    public InputFrame ToPhysical(InputFrame frame) => frame with
    {
        FloorPose = InitialReference,
        Head = ToPhysical(frame.Head),
        Left = frame.Left with { Pose = ToPhysical(frame.Left.Pose) },
        Right = frame.Right with { Pose = ToPhysical(frame.Right.Pose) },
        Trackers = frame.Trackers?.Select(tracker => tracker with
        {
            Position = ReferenceOffset.Transform(tracker.Position)
        }).ToArray()
    };
}
