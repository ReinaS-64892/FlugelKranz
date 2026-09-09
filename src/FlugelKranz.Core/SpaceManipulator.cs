using System.Numerics;

namespace FlugelKranz.Core;

public readonly record struct HandSample(RigidPose Pose, float Grip, bool IsTracked);
public readonly record struct InputFrame(RigidPose Head, bool HeadTracked, HandSample Left, HandSample Right);

/// <summary>Consumes poses in the unmodified physical tracking origin, never transformed XR poses.</summary>
public sealed class SpaceManipulator
{
    private bool leftArmed, rightArmed;
    private Vector3 dragAnchor;
    private Quaternion turnAnchor;
    public bool IsDragging { get; private set; }
    public bool IsTurning { get; private set; }
    public RigidPose Offset { get; private set; }

    public SpaceManipulator(RigidPose offset) => SetOffset(offset);

    public void Release()
    {
        IsDragging = IsTurning = leftArmed = rightArmed = false;
    }

    public void SetOffset(RigidPose offset)
    {
        if (!offset.IsValid) throw new ArgumentException("Invalid space offset.", nameof(offset));
        Offset = offset;
        Release();
    }

    public RigidPose Update(InputFrame frame)
    {
        if (!frame.HeadTracked || !frame.Head.IsValid)
        {
            Release();
            return Offset;
        }

        bool wasDragging = IsDragging, wasTurning = IsTurning;
        IsDragging = Held(frame.Left, ref leftArmed, wasDragging);
        IsTurning = Held(frame.Right, ref rightArmed, wasTurning);
        if (IsDragging && !wasDragging) dragAnchor = Offset.Transform(frame.Left.Pose.Position);
        if (IsTurning && !wasTurning)
            turnAnchor = Quaternion.Normalize(Offset.Orientation * frame.Right.Pose.Orientation);

        var rotation = Offset.Orientation;
        var translation = Offset.Position;
        if (IsTurning)
        {
            // Invert the physical hand's rotation, retaining all three axes. Rotate about the HMD.
            rotation = Quaternion.Normalize(turnAnchor * Quaternion.Conjugate(frame.Right.Pose.Orientation));
            translation = Offset.Transform(frame.Head.Position) - Vector3.Transform(frame.Head.Position, rotation);
        }
        if (IsDragging)
        {
            // With both grips held, the fixed left-hand anchor takes precedence over the HMD pivot.
            translation = dragAnchor - Vector3.Transform(frame.Left.Pose.Position, rotation);
        }
        Offset = new(rotation, translation);
        return Offset;
    }

    private static bool Held(HandSample hand, ref bool armed, bool held)
    {
        if (!hand.IsTracked || !hand.Pose.IsValid || !float.IsFinite(hand.Grip))
        {
            armed = false;
            return false;
        }
        if (hand.Grip <= 0.35f) { armed = true; return false; }
        return armed && hand.Grip >= (held ? 0.35f : 0.65f);
    }
}
