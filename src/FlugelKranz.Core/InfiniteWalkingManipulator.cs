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
    private float dragStartY, dragStartOffsetY;
    private Quaternion turnStartInputOrientation, turnStartOffsetOrientation;

    public InfiniteWalkingManipulator(RigidPose offset) => SetOffset(offset);

    public bool IsDragging => dragHands != 0;
    public bool IsTurning => turnHands != 0;
    public RigidPose Offset { get; private set; }

    public void Release()
    {
        dragHands = turnHands = 0;
        leftDragArmed = rightDragArmed = leftTurnArmed = rightTurnArmed = false;
        turnStartInputOrientation = turnStartOffsetOrientation = Quaternion.Identity;
        dragStartY = dragStartOffsetY = 0;
    }

    public void SetOffset(RigidPose offset)
    {
        if (!offset.IsValid)
            throw new ArgumentException("Invalid space offset.", nameof(offset));
        Offset = offset;
        Release();
    }

    public RigidPose Update(InputFrame frame, float elapsedSeconds, InfiniteWalkingSettings settings)
    {
        settings = settings.Normalized();
        float dt = float.IsFinite(elapsedSeconds) ? MathF.Max(0, elapsedSeconds) : 0;
        if (!frame.HeadTracked || !frame.Head.IsValid)
            return Offset;

        byte previousDragHands = dragHands;
        byte previousTurnHands = turnHands;
        dragHands = ActiveHands(frame, true, previousDragHands);
        turnHands = ActiveHands(frame, false, previousTurnHands);
        if (dragHands != previousDragHands && dragHands != 0)
            RebaseDrag(frame);
        if (turnHands != previousTurnHands && turnHands != 0)
            RebaseTurn(frame);

        if (IsTurning)
            ApplyTurn(frame, dt, settings);
        if (IsDragging)
            ApplyDrag(frame, dt, settings);
        return Offset;
    }

    private void RebaseDrag(InputFrame frame)
    {
        dragStartY = HandPosition(frame, dragHands).Y;
        dragStartOffsetY = Offset.Position.Y;
    }

    private void RebaseTurn(InputFrame frame)
    {
        turnStartInputOrientation = TurnInputOrientation(frame);
        turnStartOffsetOrientation = Offset.Orientation;
    }

    private void ApplyDrag(InputFrame frame, float elapsedSeconds, InfiniteWalkingSettings settings)
    {
        float amount = dragHands == BothHands ? 2 : 1;
        float targetY = dragStartOffsetY -
            (HandPosition(frame, dragHands).Y - dragStartY) * amount;
        var position = Offset.Position;
        position.Y = settings.DragSmoothSeconds <= 0
            ? targetY
            : float.Lerp(
                position.Y,
                targetY,
                FreeFlightManipulator.SmoothingAlpha(settings.DragSmoothSeconds, elapsedSeconds));
        Offset = Offset with { Position = position };
    }

    private void ApplyTurn(InputFrame frame, float elapsedSeconds, InfiniteWalkingSettings settings)
    {
        Quaternion currentInput = TurnInputOrientation(frame);
        Quaternion inputDelta = Quaternion.Normalize(
            currentInput * Quaternion.Conjugate(turnStartInputOrientation));
        Quaternion yaw = FreeFlightManipulator.TwistAroundAxis(inputDelta, Vector3.UnitY);
        Quaternion target = Quaternion.Normalize(
            turnStartOffsetOrientation * Quaternion.Conjugate(yaw));
        float smoothSeconds = turnHands == BothHands
            ? settings.TurnHeadSmoothSeconds
            : settings.TurnSmoothSeconds;
        Quaternion rotation = smoothSeconds <= 0
            ? target
            : Quaternion.Normalize(Quaternion.Slerp(
                Offset.Orientation,
                target,
                FreeFlightManipulator.SmoothingAlpha(smoothSeconds, elapsedSeconds)));
        Vector3 pivot = frame.Head.Position;
        Vector3 pivotInRoot = Offset.Transform(pivot);
        Vector3 position = pivotInRoot - Vector3.Transform(pivot, rotation);
        Offset = new(rotation, position);
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
