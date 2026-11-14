using FlugelKranz.Core;

namespace FlugelKranz.OpenXR;

/// <summary>Flight transforms in Monado root coordinates, retaining the runtime's device calibration.</summary>
internal sealed record FlightSpaceTransform(RigidPose InitialStage, RigidPose Offset)
{
    // (D^-1 * S)^-1 * pose = S^-1 * D * pose: apply flight once to every device.
    public RigidPose StageOffset => Offset.Inverse() * InitialStage;

    // Remove STAGE and our flight transform; retain all driver-space calibration.
    public RigidPose ToPhysical(RigidPose stagePose) => StageOffset * stagePose;

    public InputFrame ToPhysical(InputFrame frame) => frame with
    {
        FloorPose = InitialStage,
        Head = ToPhysical(frame.Head),
        Left = frame.Left with { Pose = ToPhysical(frame.Left.Pose) },
        Right = frame.Right with { Pose = ToPhysical(frame.Right.Pose) },
        Trackers = frame.Trackers?.Select(tracker => tracker with
        {
            Position = StageOffset.Transform(tracker.Position)
        }).ToArray()
    };
}
