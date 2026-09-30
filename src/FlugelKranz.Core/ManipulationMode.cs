using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Common lifecycle and logical hand input for user-controlled movement modes.</summary>
public abstract class ManipulationMode : MovementMode
{
    private readonly LogicalHandInputs inputs = new();
    protected ManipulationHands dragHands, turnHands;
    protected RigidPose targetOffset;
    private RigidPose offset;

    public override RigidPose Offset => offset;
    public override bool IsDragging => dragHands != ManipulationHands.None;
    public override bool IsTurning => turnHands != ManipulationHands.None;
    public abstract string ResetMessage { get; }
    public abstract ManipulationMode CreateAlternate(RigidPose current);

    protected void ApplyOffset(RigidPose value) => offset = value;

    public void SetOffset(RigidPose value)
    {
        if (!value.IsValid)
            throw new ArgumentException("Invalid space offset.", nameof(value));
        offset = targetOffset = value;
        Release();
    }

    public sealed override void Release()
    {
        dragHands = turnHands = ManipulationHands.None;
        inputs.Release();
        targetOffset = Offset;
        ReleaseMotion();
    }

    protected abstract void ReleaseMotion();

    public virtual MovementMode EnterFrom(ManipulationMode previous, RigidPose current, RigidPose original)
    {
        SetOffset(current);
        return this;
    }

    public virtual bool CanReset(InputFrame frame) => true;

    public abstract MovementMode CreateReset(InputFrame frame, RigidPose original, FlugelKranzSettings settings);

    protected readonly record struct HandChange(ManipulationHands Previous, ManipulationHands Current)
    {
        public bool Changed => Previous != Current;
        public bool Began => Previous == ManipulationHands.None && Current != ManipulationHands.None;
        public bool Ended => Previous != ManipulationHands.None && Current == ManipulationHands.None;
        public bool BecameTwoHanded => Previous != ManipulationHands.Both && Current == ManipulationHands.Both;
        public bool NeedsRebase => Changed && Current != ManipulationHands.None;
    }

    protected readonly record struct HandChanges(HandChange Drag, HandChange Turn)
    {
        public bool NeedsRebase => Drag.NeedsRebase || Turn.NeedsRebase;
    }

    protected HandChanges ReadHands(InputFrame frame, bool endTwoHandOnRelease = false)
    {
        ManipulationHands previousDrag = dragHands;
        ManipulationHands previousTurn = turnHands;
        dragHands = inputs.ActiveHands(frame, true, previousDrag);
        turnHands = inputs.ActiveHands(frame, false, previousTurn);
        if (endTwoHandOnRelease && previousDrag == ManipulationHands.Both &&
            dragHands is ManipulationHands.Left or ManipulationHands.Right)
        {
            inputs.DisarmDrag(dragHands);
            dragHands = ManipulationHands.None;
        }
        if (endTwoHandOnRelease && previousTurn == ManipulationHands.Both &&
            turnHands is ManipulationHands.Left or ManipulationHands.Right)
        {
            inputs.DisarmTurn(turnHands);
            turnHands = ManipulationHands.None;
        }
        return new(new(previousDrag, dragHands), new(previousTurn, turnHands));
    }

    protected static Vector3 HandPosition(InputFrame frame, ManipulationHands hands) => hands switch
    {
        ManipulationHands.Left => frame.Left.Pose.Position,
        ManipulationHands.Right => frame.Right.Pose.Position,
        ManipulationHands.Both => (frame.Left.Pose.Position + frame.Right.Pose.Position) * 0.5f,
        _ => Vector3.Zero
    };

    protected static Quaternion HandOrientation(InputFrame frame, ManipulationHands hands) => hands switch
    {
        ManipulationHands.Left => frame.Left.Pose.Orientation,
        ManipulationHands.Right => frame.Right.Pose.Orientation,
        ManipulationHands.Both => Average(frame.Left.Pose.Orientation, frame.Right.Pose.Orientation),
        _ => Quaternion.Identity
    };

    private static Quaternion Average(Quaternion left, Quaternion right)
    {
        if (Quaternion.Dot(left, right) < 0)
            right = -right;
        return Quaternion.Normalize(Quaternion.Slerp(left, right, 0.5f));
    }
}
