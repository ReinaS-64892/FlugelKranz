using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Common lifecycle and logical hand input for user-controlled movement modes.</summary>
public abstract class ManipulationMode : MovementMode
{
    protected const byte LeftHand = 1;
    protected const byte RightHand = 2;
    protected const byte BothHands = LeftHand | RightHand;
    private readonly LogicalHandInputs inputs = new();
    protected byte dragHands, turnHands;
    protected RigidPose targetOffset;
    private RigidPose offset;

    public override RigidPose Offset => offset;
    public override bool IsDragging => dragHands != 0;
    public override bool IsTurning => turnHands != 0;
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
        dragHands = turnHands = 0;
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

    protected readonly record struct HandChange(byte Previous, byte Current)
    {
        public bool Changed => Previous != Current;
        public bool Began => Previous == 0 && Current != 0;
        public bool Ended => Previous != 0 && Current == 0;
        public bool BecameTwoHanded => Previous != BothHands && Current == BothHands;
        public bool NeedsRebase => Changed && Current != 0;
    }

    protected readonly record struct HandChanges(HandChange Drag, HandChange Turn)
    {
        public bool NeedsRebase => Drag.NeedsRebase || Turn.NeedsRebase;
    }

    protected HandChanges ReadHands(InputFrame frame, bool endTwoHandOnRelease = false)
    {
        byte previousDrag = dragHands;
        byte previousTurn = turnHands;
        dragHands = inputs.ActiveHands(frame, true, previousDrag);
        turnHands = inputs.ActiveHands(frame, false, previousTurn);
        if (endTwoHandOnRelease && previousDrag == BothHands && dragHands is LeftHand or RightHand)
        {
            inputs.DisarmDrag(dragHands);
            dragHands = 0;
        }
        if (endTwoHandOnRelease && previousTurn == BothHands && turnHands is LeftHand or RightHand)
        {
            inputs.DisarmTurn(turnHands);
            turnHands = 0;
        }
        return new(new(previousDrag, dragHands), new(previousTurn, turnHands));
    }

    protected static Vector3 HandPosition(InputFrame frame, byte hands) => hands switch
    {
        LeftHand => frame.Left.Pose.Position,
        RightHand => frame.Right.Pose.Position,
        BothHands => (frame.Left.Pose.Position + frame.Right.Pose.Position) * 0.5f,
        _ => Vector3.Zero
    };

    protected static Quaternion HandOrientation(InputFrame frame, byte hands) => hands switch
    {
        LeftHand => frame.Left.Pose.Orientation,
        RightHand => frame.Right.Pose.Orientation,
        BothHands => Average(frame.Left.Pose.Orientation, frame.Right.Pose.Orientation),
        _ => Quaternion.Identity
    };

    private static Quaternion Average(Quaternion left, Quaternion right)
    {
        if (Quaternion.Dot(left, right) < 0)
            right = -right;
        return Quaternion.Normalize(Quaternion.Slerp(left, right, 0.5f));
    }
}
