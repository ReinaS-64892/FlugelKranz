using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Direct Y translation and yaw-only rotation for room-scale recentering.</summary>
public sealed class InfiniteWalkingManipulator : ManipulationMode
{
    private float dragStartY, dragStartOffsetY;
    private Quaternion turnStartInputOrientation, turnStartOffsetOrientation;
    private Vector3 previousTurnHeadPosition;
    private bool hasPreviousTurnHeadPosition;
    private bool turnFollowerUsesHeadSmoothing;

    public InfiniteWalkingManipulator(RigidPose offset) => SetOffset(offset);


    protected override void ReleaseMotion()
    {
        turnStartInputOrientation = turnStartOffsetOrientation = Quaternion.Identity;
        dragStartY = dragStartOffsetY = 0;
        previousTurnHeadPosition = Vector3.Zero;
        hasPreviousTurnHeadPosition = false;
        turnFollowerUsesHeadSmoothing = false;
    }

    public override string ResetMessage => "無限歩行の高さを戻しています…";
    public override ManipulationMode CreateAlternate(RigidPose current) => new FreeFlightManipulator(current);
    public override RigidPose Update(InputFrame frame, float elapsedSeconds, FlugelKranzSettings settings) =>
        Update(frame, elapsedSeconds, settings.InfiniteWalking);
    public override MovementMode EnterFrom(ManipulationMode previous, RigidPose current, RigidPose original) =>
        previous is FreeFlightManipulator ? new InfiniteWalkingTransition(current, original, this)
            : base.EnterFrom(previous, current, original);
    public override MovementMode CreateReset(InputFrame frame, RigidPose original, FlugelKranzSettings settings) =>
        SpaceResetTransition.CreateInfiniteWalking(Offset, original, this);

    public RigidPose Update(InputFrame frame, float elapsedSeconds, InfiniteWalkingSettings settings)
    {
        settings = settings.Normalized();
        float dt = float.IsFinite(elapsedSeconds) ? MathF.Max(0, elapsedSeconds) : 0;
        if (!frame.HeadTracked || !frame.Head.IsValid)
        {
            hasPreviousTurnHeadPosition = false;
            return Offset;
        }

        var hands = ReadHands(frame);
        if (hands.NeedsRebase)
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
        Quaternion yaw = RotationMath.TwistAroundAxis(inputDelta, Vector3.UnitY);
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
        if (!IsDragging && !IsTurning)
        {
            ApplyOffset(targetOffset);
            return;
        }

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
        ApplyOffset(new(
            MotionSmoothing.Follow(
                Offset.Orientation,
                targetOffset.Orientation,
                turnSmoothSeconds,
                elapsedSeconds),
            position));
    }

    private Quaternion TurnInputOrientation(InputFrame frame) => turnHands == BothHands
        ? frame.Head.Orientation
        : HandOrientation(frame, turnHands);

}
