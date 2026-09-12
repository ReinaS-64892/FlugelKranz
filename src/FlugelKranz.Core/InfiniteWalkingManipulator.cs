using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Direct Y translation and yaw-only rotation for room-scale recentering.</summary>
public sealed class InfiniteWalkingManipulator
{
    private const byte LeftHand = 1;
    private const byte RightHand = 2;
    private const byte BothHands = LeftHand | RightHand;
    private bool leftDragArmed, rightDragArmed, leftTurnArmed, rightTurnArmed;
    private byte dragHands, turnHands;
    private RigidPose targetOffset;
    private float dragStartY, dragStartOffsetY;
    private Quaternion turnStartInputOrientation, turnStartOffsetOrientation;
    private Vector3 previousTurnHeadPosition;
    private bool hasPreviousTurnHeadPosition;
    private bool turnFollowerUsesHeadSmoothing;

    public InfiniteWalkingManipulator(RigidPose offset) => SetOffset(offset);

    public bool IsDragging => dragHands != 0;
    public bool IsTurning => turnHands != 0;
    public RigidPose Offset { get; private set; }

    public void Release()
    {
        dragHands = turnHands = 0;
        leftDragArmed = rightDragArmed = leftTurnArmed = rightTurnArmed = false;
        targetOffset = Offset;
        turnStartInputOrientation = turnStartOffsetOrientation = Quaternion.Identity;
        dragStartY = dragStartOffsetY = 0;
        previousTurnHeadPosition = Vector3.Zero;
        hasPreviousTurnHeadPosition = false;
        turnFollowerUsesHeadSmoothing = false;
    }

    public void SetOffset(RigidPose offset)
    {
        if (!offset.IsValid)
            throw new ArgumentException("Invalid space offset.", nameof(offset));
        Offset = targetOffset = offset;
        Release();
    }

    public RigidPose Update(InputFrame frame, float elapsedSeconds, InfiniteWalkingSettings settings)
    {
        settings = settings.Normalized();
        float dt = float.IsFinite(elapsedSeconds) ? MathF.Max(0, elapsedSeconds) : 0;
        if (!frame.HeadTracked || !frame.Head.IsValid)
        {
            hasPreviousTurnHeadPosition = false;
            return Offset;
        }

        byte previousDragHands = dragHands;
        byte previousTurnHands = turnHands;
        dragHands = ActiveHands(frame, true, previousDragHands);
        turnHands = ActiveHands(frame, false, previousTurnHands);
        bool dragHandsChanged = dragHands != previousDragHands;
        bool turnHandsChanged = turnHands != previousTurnHands;
        bool activeHandsChanged =
            (dragHandsChanged && dragHands != 0) ||
            (turnHandsChanged && turnHands != 0);
        if (activeHandsChanged)
        {
            targetOffset = Offset;
            if (IsDragging)
                RebaseDrag(frame);
            if (IsTurning)
                RebaseTurn(frame);
        }

        if (IsTurning)
            ApplyTurn(frame, settings);
        if (IsDragging)
            ApplyDrag(frame);
        FollowTarget(dt, settings);
        return Offset;
    }

    private void RebaseDrag(InputFrame frame)
    {
        dragStartY = HandPosition(frame, dragHands).Y;
        dragStartOffsetY = targetOffset.Position.Y;
    }

    private void RebaseTurn(InputFrame frame)
    {
        turnStartInputOrientation = TurnInputOrientation(frame);
        turnStartOffsetOrientation = targetOffset.Orientation;
        previousTurnHeadPosition = frame.Head.Position;
        hasPreviousTurnHeadPosition = true;
        turnFollowerUsesHeadSmoothing = turnHands == BothHands;
    }

    private void ApplyDrag(InputFrame frame)
    {
        float amount = dragHands == BothHands ? 2 : 1;
        float targetY = dragStartOffsetY -
            (HandPosition(frame, dragHands).Y - dragStartY) * amount;
        var position = targetOffset.Position;
        position.Y = targetY;
        targetOffset = targetOffset with { Position = position };
    }

    private void ApplyTurn(InputFrame frame, InfiniteWalkingSettings settings)
    {
        Vector3 headMovement = hasPreviousTurnHeadPosition
            ? frame.Head.Position - previousTurnHeadPosition
            : Vector3.Zero;
        previousTurnHeadPosition = frame.Head.Position;
        hasPreviousTurnHeadPosition = true;

        Quaternion currentInput = TurnInputOrientation(frame);
        Quaternion inputDelta = Quaternion.Normalize(
            currentInput * Quaternion.Conjugate(turnStartInputOrientation));
        Quaternion yaw = FreeFlightManipulator.TwistAroundAxis(inputDelta, Vector3.UnitY);
        Quaternion target = Quaternion.Normalize(
            turnStartOffsetOrientation * Quaternion.Conjugate(yaw));
        bool rotated = 1 - MathF.Abs(Quaternion.Dot(targetOffset.Orientation, target)) > 0.0000001f;
        Vector3 pivot = frame.Head.Position;
        Vector3 pivotInRoot = targetOffset.Transform(pivot);
        Vector3 position = pivotInRoot - Vector3.Transform(pivot, target);
        if (rotated && settings.TurnMovementBoostMultiplier > 0)
        {
            Vector3 boost = Vector3.Transform(headMovement, targetOffset.Orientation);
            boost.Y = 0;
            position += boost * settings.TurnMovementBoostMultiplier;
        }
        targetOffset = new(target, position);
        turnFollowerUsesHeadSmoothing = turnHands == BothHands;
    }

    private void FollowTarget(float elapsedSeconds, InfiniteWalkingSettings settings)
    {
        float turnSmoothSeconds = turnFollowerUsesHeadSmoothing
            ? settings.TurnHeadSmoothSeconds
            : settings.TurnSmoothSeconds;
        var position = new Vector3(
            MotionSmoothing.Follow(
                Offset.Position.X,
                targetOffset.Position.X,
                turnSmoothSeconds,
                elapsedSeconds),
            MotionSmoothing.Follow(
                Offset.Position.Y,
                targetOffset.Position.Y,
                settings.DragSmoothSeconds,
                elapsedSeconds),
            MotionSmoothing.Follow(
                Offset.Position.Z,
                targetOffset.Position.Z,
                turnSmoothSeconds,
                elapsedSeconds));
        Offset = new(
            MotionSmoothing.Follow(
                Offset.Orientation,
                targetOffset.Orientation,
                turnSmoothSeconds,
                elapsedSeconds),
            position);
    }

    private byte ActiveHands(InputFrame frame, bool drag, byte previous)
    {
        byte result = 0;
        bool leftWasHeld = (previous & LeftHand) != 0;
        bool rightWasHeld = (previous & RightHand) != 0;
        bool left = drag
            ? Held(frame.Left, frame.Left.Drag, ref leftDragArmed, leftWasHeld)
            : Held(frame.Left, frame.Left.Turn, ref leftTurnArmed, leftWasHeld);
        bool right = drag
            ? Held(frame.Right, frame.Right.Drag, ref rightDragArmed, rightWasHeld)
            : Held(frame.Right, frame.Right.Turn, ref rightTurnArmed, rightWasHeld);
        if (left)
            result |= LeftHand;
        if (right)
            result |= RightHand;
        return result;
    }

    private static Quaternion HandOrientation(InputFrame frame, byte hands) => hands switch
    {
        LeftHand => frame.Left.Pose.Orientation,
        RightHand => frame.Right.Pose.Orientation,
        _ => Quaternion.Identity
    };

    private Quaternion TurnInputOrientation(InputFrame frame) => turnHands == BothHands
        ? frame.Head.Orientation
        : HandOrientation(frame, turnHands);

    private static Vector3 HandPosition(InputFrame frame, byte hands) => hands switch
    {
        LeftHand => frame.Left.Pose.Position,
        RightHand => frame.Right.Pose.Position,
        BothHands => (frame.Left.Pose.Position + frame.Right.Pose.Position) * 0.5f,
        _ => Vector3.Zero
    };

    private static bool Held(HandSample hand, float value, ref bool armed, bool held)
    {
        if (!hand.IsTracked || !hand.Pose.IsValid || !float.IsFinite(value))
        {
            armed = false;
            return false;
        }
        if (value <= 0.35f)
        {
            armed = true;
            return false;
        }
        return armed && value >= (held ? 0.35f : 0.65f);
    }
}
