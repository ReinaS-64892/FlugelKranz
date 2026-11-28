using System.Numerics;
using static FlugelKranz.Core.RotationMath;

namespace FlugelKranz.Core;

/// <summary>Consumes calibrated Monado root poses with the flight transform removed.</summary>
public sealed class FreeFlightManipulator : ManipulationMode
{
    private static readonly FlightMotionSettings DirectManipulationSettings =
        FlightMotionSettings.Default with
        {
            InertiaCutoffEnabled = false,
            InertiaAccelerationBoostEnabled = false,
            DragAccelerationMultiplier = 0,
            TurnAccelerationMultiplier = 0,
            DragSmoothSeconds = 0,
            TurnSmoothSeconds = 0
        };
    private readonly InertiaMotion linearMotion = new(), angularMotion = new();
    private Vector3 dragVelocity;
    private Quaternion dragReferenceOrientation;
    private Vector3 dragAnchorInRoot, previousDragPosition;
    private bool dragAccelerationBoostActive;
    private Quaternion turnAnchor, turnStartOrientation, turnStartOffsetOrientation;
    private Vector3 twoHandTurnAxis;
    private Vector3 twoHandTurnPivot;
    private Quaternion previousTwoHandOrientation;
    private bool useTwoHandTurnPivotForInertia;
    private Vector3 turnVelocity;
    private bool trackingRebaseRequired;
    private RigidPose previousTurnTarget;
    public bool HasLinearInertia => linearMotion.Velocity.LengthSquared() > 0;
    public bool HasAngularInertia => angularMotion.Velocity.LengthSquared() > 0;

    public FreeFlightManipulator(RigidPose offset) => SetOffset(offset);

    protected override void ReleaseMotion()
    {
        trackingRebaseRequired = false;
        linearMotion.Clear();
        angularMotion.Clear();
        dragVelocity = turnVelocity = Vector3.Zero;
        dragReferenceOrientation = turnStartOrientation = turnStartOffsetOrientation = Quaternion.Identity;
        dragAnchorInRoot = previousDragPosition = twoHandTurnAxis = twoHandTurnPivot = Vector3.Zero;
        previousTwoHandOrientation = Quaternion.Identity;
        useTwoHandTurnPivotForInertia = false;
        dragAccelerationBoostActive = false;
    }

    public override string ResetMessage => "自由飛行の水平へ戻しています…";
    public override ManipulationMode CreateAlternate(RigidPose current) => new InfiniteWalkingManipulator(current);
    public override RigidPose Update(InputFrame frame, float elapsedSeconds, FlugelKranzSettings settings) =>
        Update(frame, elapsedSeconds, settings.FreeFlight);
    public override bool CanReset(InputFrame frame) => frame.HeadTracked && frame.Head.IsValid;
    public override MovementMode CreateReset(InputFrame frame, RigidPose original, FlugelKranzSettings settings) =>
        SpaceResetTransition.CreateFreeFlight(Offset, frame.Head, GetCurrentTurnPivot(frame, settings.FreeFlight), this);

    public RigidPose Update(InputFrame frame) => Update(frame, 0.01f, DirectManipulationSettings);

    public RigidPose Update(InputFrame frame, float elapsedSeconds, FlightMotionSettings settings)
    {
        settings = settings.Normalized();
        float sampleSeconds = float.IsFinite(elapsedSeconds) ? MathF.Max(0, elapsedSeconds) : 0;
        float dt = sampleSeconds;
        if (!frame.HeadTracked || !frame.Head.IsValid)
        {
            trackingRebaseRequired = true;
            // These samples predate the pause and must never become release inertia.
            dragVelocity = turnVelocity = Vector3.Zero;
            return Offset;
        }

        var hands = ReadHands(frame, endTwoHandOnRelease: true);
        HandleGripChanges(frame, hands, settings);

        var orientationBefore = Offset.Orientation;
        Vector3 inertiaStep = AdvanceMotion(frame, dt, sampleSeconds, settings);

        var target = GrabTarget(frame, settings);
        UpdateRawVelocities(
            frame,
            target,
            IsDragging && !hands.Drag.Changed,
            IsTurning && !hands.Turn.Changed,
            sampleSeconds);
        targetOffset = target;
        FollowTarget(frame, inertiaStep, dt, settings);
        RotateLinearInertia(
            orientationBefore,
            Offset.Orientation,
            settings.VectorRotationMultiplier);
        return Offset;
    }

    private void HandleGripChanges(InputFrame frame, HandChanges hands, FlightMotionSettings settings)
    {
        if (hands.Drag.Ended)
        {
            if (HandsUsable(frame, hands.Drag.Previous))
                FinishDrag(frame.Head, hands.Drag.Previous, settings);
            else
                CancelDrag();
        }
        if (hands.Turn.Ended)
        {
            useTwoHandTurnPivotForInertia = hands.Turn.Previous == ManipulationHands.Both;
            if (HandsUsable(frame, hands.Turn.Previous))
                FinishTurn(settings);
            else
                CancelTurn();
            // Keep the presented rotation when Turn ends, including while Drag
            // remains held. Released angular inertia starts from this rotation.
            targetOffset = targetOffset with { Orientation = Offset.Orientation };
        }

        if (hands.Drag.Began)
        {
            linearMotion.StartBrake();
            dragAccelerationBoostActive = false;
        }
        if (hands.Turn.Began || hands.Drag.BecameTwoHanded)
        {
            angularMotion.StartBrake();
        }

        if (!IsDragging && !IsTurning)
        {
            // Smoothing may leave the presented offset behind targetOffset when
            // a grip is released. Start inertia from what is actually visible
            // instead of snapping to the unapplied target first.
            targetOffset = Offset;
        }

        if (hands.NeedsRebase || trackingRebaseRequired)
        {
            // A new grip starts from the pose currently presented to the runtime,
            // discarding any unapplied smoothing remainder without a jump.
            targetOffset = Offset;
            if (IsDragging)
                RebaseDrag(frame);
            if (IsTurning)
                RebaseTurn(frame);
        }
        trackingRebaseRequired = false;
    }

    private Vector3 AdvanceMotion(InputFrame frame, float dt, float sampleSeconds, FlightMotionSettings settings)
    {
        bool brakingAngularInertia = IsTurning || dragHands == ManipulationHands.Both;
        Vector3 inertiaStep = AdvanceFreeInertia(frame, dt, !IsDragging, !brakingAngularInertia, settings);
        if (IsDragging)
        {
            float controllerSpeed = ControllerMovementSpeed(frame, sampleSeconds);
            dragAccelerationBoostActive =
                settings.InertiaAccelerationBoostEnabled &&
                controllerSpeed > settings.DragCutoffMetresPerSecond;
            inertiaStep = linearMotion.Advance(dt, settings, braking: !dragAccelerationBoostActive);
            dragAnchorInRoot += inertiaStep;
        }
        if (IsTurning)
        {
            Vector3 rotationStep = angularMotion.Advance(dt, settings, braking: true);
            if (turnHands == ManipulationHands.Both)
                turnStartOffsetOrientation = IntegrateRotation(turnStartOffsetOrientation, rotationStep, 1);
            else
                turnAnchor = IntegrateRotation(turnAnchor, rotationStep, 1);
        }
        else if (dragHands == ManipulationHands.Both)
        {
            Vector3 rotationStep = angularMotion.Advance(dt, settings, braking: true);
            AdvanceTargetRotation(frame, rotationStep, 1, settings);
        }
        return inertiaStep;
    }

    private void RebaseDrag(InputFrame frame)
    {
        dragReferenceOrientation = targetOffset.Orientation;
        previousDragPosition = HandPosition(frame, dragHands);
        dragAnchorInRoot = targetOffset.Transform(previousDragPosition);
        dragVelocity = Vector3.Zero;
    }

    private void RebaseTurn(InputFrame frame)
    {
        turnStartOrientation = HandOrientation(frame, turnHands);
        turnStartOffsetOrientation = targetOffset.Orientation;
        turnAnchor = Quaternion.Normalize(targetOffset.Orientation * turnStartOrientation);
        twoHandTurnAxis = turnHands == ManipulationHands.Both
            ? SafeDirection(frame.Right.Pose.Position - frame.Left.Pose.Position)
            : Vector3.Zero;
        twoHandTurnPivot = turnHands == ManipulationHands.Both
            ? HandPosition(frame, ManipulationHands.Both)
            : Vector3.Zero;
        previousTwoHandOrientation = turnHands == ManipulationHands.Both
            ? HandOrientation(frame, ManipulationHands.Both)
            : Quaternion.Identity;
        useTwoHandTurnPivotForInertia = turnHands == ManipulationHands.Both;
        turnVelocity = Vector3.Zero;
        previousTurnTarget = targetOffset;
    }

    private void FollowTarget(
        InputFrame frame,
        Vector3 inertiaStep,
        float elapsedSeconds,
        FlightMotionSettings settings)
    {
        if (!IsDragging && !IsTurning)
        {
            ApplyOffset(targetOffset);
            return;
        }

        Quaternion orientation = MotionSmoothing.Follow(
            Offset.Orientation,
            targetOffset.Orientation,
            IsTurning ? settings.TurnSmoothSeconds : 0,
            elapsedSeconds);
        Vector3 position;
        if (IsDragging)
        {
            Vector3 dragPoint = HandPosition(frame, dragHands);
            // Advect both ends of the follower by the same inertia step. Only
            // the controller's displacement relative to its anchor is smoothed.
            Vector3 dragPointInRoot = MotionSmoothing.Follow(
                Offset.Transform(dragPoint) + inertiaStep,
                dragAnchorInRoot,
                settings.DragSmoothSeconds,
                elapsedSeconds);
            position = dragPointInRoot - Vector3.Transform(dragPoint, orientation);
        }
        else
        {
            // Physical pivot and flight-transformed position are both in Monado
            // root coordinates. Apply inertia directly, then compensate using
            // the presented rotation, never the unsmoothed target rotation.
            Vector3 pivot = TurnPivot(frame, settings);
            Vector3 pivotInRoot = Offset.Transform(pivot) + inertiaStep;
            position = pivotInRoot - Vector3.Transform(pivot, orientation);
        }
        ApplyOffset(new(orientation, position));
    }

    private void UpdateRawVelocities(
        InputFrame frame,
        RigidPose target,
        bool dragging,
        bool turning,
        float sampleSeconds)
    {
        if (dragging && sampleSeconds > 0)
        {
            Vector3 current = HandPosition(frame, dragHands);
            var controllerDelta = current - previousDragPosition;
            dragVelocity = -Vector3.Transform(controllerDelta, dragReferenceOrientation) / sampleSeconds;
            previousDragPosition = current;
        }

        if (turning && sampleSeconds > 0)
        {
            turnVelocity = RotationVelocity(
                previousTurnTarget.Orientation,
                target.Orientation,
                sampleSeconds);
            previousTurnTarget = target;
        }
    }

    private float ControllerMovementSpeed(InputFrame frame, float sampleSeconds)
    {
        if (sampleSeconds <= 0)
            return dragVelocity.Length();

        return Vector3.Distance(HandPosition(frame, dragHands), previousDragPosition) / sampleSeconds;
    }

    private void RotateLinearInertia(Quaternion from, Quaternion to, float multiplier)
    {
        if (linearMotion.Velocity.LengthSquared() <= 0 || multiplier <= 0)
            return;

        var delta = Quaternion.Normalize(to * Quaternion.Conjugate(from));
        if (delta.W < 0)
            delta = -delta;

        var applied = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, delta, multiplier));
        linearMotion.Velocity = Vector3.Transform(linearMotion.Velocity, applied);
    }

    private RigidPose GrabTarget(InputFrame frame, FlightMotionSettings settings)
    {
        var rotation = targetOffset.Orientation;
        var translation = targetOffset.Position;
        if (IsTurning)
        {
            rotation = TurnTargetOrientation(frame);
            Vector3 pivot = TurnPivot(frame, settings);
            translation = targetOffset.Transform(pivot) - Vector3.Transform(pivot, rotation);
        }
        if (IsDragging)
            translation = dragAnchorInRoot -
                Vector3.Transform(HandPosition(frame, dragHands), rotation);

        return new(rotation, translation);
    }

    private Quaternion TurnTargetOrientation(InputFrame frame)
    {
        if (turnHands != ManipulationHands.Both)
        {
            Quaternion singleHandOrientation = HandOrientation(frame, turnHands);
            return Quaternion.Normalize(turnAnchor * Quaternion.Conjugate(singleHandOrientation));
        }

        Quaternion current = HandOrientation(frame, ManipulationHands.Both);
        if (Quaternion.Dot(current, previousTwoHandOrientation) < 0)
            current = -current;

        // Keep the pole axis captured at the moment both hands start turning.
        // Reprojecting the accumulated pose onto a moving hand line can turn
        // tracking noise or a large pose change into an unintended spin.
        Vector3 axis = twoHandTurnAxis;
        if (axis.LengthSquared() <= 0)
        {
            previousTwoHandOrientation = current;
            return turnStartOffsetOrientation;
        }

        Quaternion controllerDelta = Quaternion.Normalize(current * Quaternion.Conjugate(previousTwoHandOrientation));
        float twistAngle = RotationAngleAroundAxis(controllerDelta, axis);
        previousTwoHandOrientation = current;
        if (MathF.Abs(twistAngle) > 0)
        {
            Quaternion inverseTwist = Quaternion.CreateFromAxisAngle(axis, -twistAngle);
            turnStartOffsetOrientation = Quaternion.Normalize(
                turnStartOffsetOrientation * inverseTwist);
        }
        return turnStartOffsetOrientation;
    }

    private Vector3 TurnPivot(InputFrame frame, FlightMotionSettings settings)
    {
        if (IsDragging)
            return HandPosition(frame, dragHands);
        if (turnHands == ManipulationHands.Both ||
            (turnHands == ManipulationHands.None && useTwoHandTurnPivotForInertia && angularMotion.Velocity.LengthSquared() > 0))
            return twoHandTurnPivot;
        return GetSingleHandTurnPivot(frame, settings);
    }

    internal Vector3 GetSingleHandTurnPivot(InputFrame frame, FlightMotionSettings settings)
    {
        if (IsDragging)
            return HandPosition(frame, dragHands);
        if (settings.TurnOrigin == TurnOrigin.Head)
            return frame.Head.Position;

        Vector3 weightedPosition = frame.Head.Position * settings.TurnOriginHeadWeightMultiplier;
        float totalWeight = settings.TurnOriginHeadWeightMultiplier;
        if (Usable(frame.Left))
        {
            weightedPosition += frame.Left.Pose.Position;
            totalWeight++;
        }
        if (Usable(frame.Right))
        {
            weightedPosition += frame.Right.Pose.Position;
            totalWeight++;
        }
        if (frame.Trackers is { } trackers)
        {
            foreach (var tracker in trackers)
            {
                if (!tracker.IsUsable)
                    continue;
                weightedPosition += tracker.Position;
                totalWeight++;
            }
        }
        return weightedPosition / totalWeight;
    }

    internal Vector3 GetCurrentTurnPivot(InputFrame frame, FlightMotionSettings settings) =>
        TurnPivot(frame, settings.Normalized());

    private void AdvanceTargetRotation(
        InputFrame frame,
        Vector3 velocity,
        float elapsedSeconds,
        FlightMotionSettings settings)
    {
        if (velocity.LengthSquared() <= 0 || elapsedSeconds <= 0)
            return;

        Vector3 pivot = TurnPivot(frame, settings);
        Vector3 pivotInRoot = targetOffset.Transform(pivot);
        Quaternion orientation = IntegrateRotation(
            targetOffset.Orientation,
            velocity,
            elapsedSeconds);
        targetOffset = new(
            orientation,
            pivotInRoot - Vector3.Transform(pivot, orientation));
    }

    private Vector3 AdvanceFreeInertia(
        InputFrame frame,
        float dt,
        bool move,
        bool turn,
        FlightMotionSettings settings)
    {
        if (dt <= 0)
            return Vector3.Zero;

        var rotation = targetOffset.Orientation;
        var translation = targetOffset.Position;
        if (turn && angularMotion.Velocity.LengthSquared() > 0)
        {
            Vector3 pivot = TurnPivot(frame, settings);
            var pivotInRoot = targetOffset.Transform(pivot);
            rotation = IntegrateRotation(rotation, angularMotion.Advance(dt, settings), 1);
            translation = pivotInRoot - Vector3.Transform(pivot, rotation);
        }

        Vector3 inertiaStep = move ? linearMotion.Advance(dt, settings) : Vector3.Zero;
        translation += inertiaStep;

        targetOffset = new(rotation, translation);
        return inertiaStep;
    }

    private void FinishDrag(RigidPose head, ManipulationHands releasedHands, FlightMotionSettings settings)
    {
        bool belowCutoff = settings.InertiaCutoffEnabled &&
            BelowCutoff(dragVelocity.Length(), settings.DragCutoffMetresPerSecond);
        var releaseVelocity = SuppressReleaseVelocity(dragVelocity, settings.DragCutoffMetresPerSecond, settings);
        bool hasUsableAcceleration = releaseVelocity.LengthSquared() > 0.0000000001f;
        if (hasUsableAcceleration)
        {
            // dragVelocity is in root space. Apply the reference-space rotation
            // to the physical HMD orientation before comparing their directions.
            var headInRoot = Quaternion.Normalize(Offset.Orientation * head.Orientation);
            var acceleration = ApplyDragAcceleration(releaseVelocity, headInRoot, settings);
            if (releasedHands == ManipulationHands.Both)
                acceleration *= settings.TwoHandDragAccelerationMultiplier;
            float speedBeforeBoost = linearMotion.Velocity.Length();
            if (belowCutoff)
                linearMotion.Velocity += acceleration;
            else if (dragAccelerationBoostActive && speedBeforeBoost > 0.0000000001f)
            {
                var boosted = linearMotion.Velocity + acceleration;
                float boostReferenceSpeed = MathF.Max(speedBeforeBoost, acceleration.Length());
                float maximumSpeed = boostReferenceSpeed * settings.InertiaAccelerationBoostMaximumMultiplier;
                if (boosted.Length() > maximumSpeed && maximumSpeed > 0)
                    boosted = Vector3.Normalize(boosted) * maximumSpeed;
                linearMotion.Velocity = boosted;
            }
            else
                linearMotion.Velocity = acceleration;

            linearMotion.ExemptionSeconds = belowCutoff ? 0 : ExemptionDuration(
                linearMotion.Velocity.Length(),
                0.001f,
                settings.DragDecelerationExemptionDurationRatio,
                settings);
        }
        else if (linearMotion.Velocity.LengthSquared() <= 0.0000000001f)
        {
            linearMotion.Velocity = Vector3.Zero;
            linearMotion.ExemptionSeconds = 0;
        }

        dragAccelerationBoostActive = false;
        dragVelocity = Vector3.Zero;
    }

    private static Vector3 ApplyDragAcceleration(
        Vector3 velocity,
        Quaternion playerOrientation,
        FlightMotionSettings settings)
    {
        var baseVelocity = velocity * settings.DragAccelerationMultiplier;
        if (baseVelocity.LengthSquared() <= 0 || settings.ZAccelerationMultiplier <= 1)
            return baseVelocity;

        var forward = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, playerOrientation));
        var direction = Vector3.Normalize(velocity);
        float alignment = MathF.Abs(Math.Clamp(Vector3.Dot(forward, direction), -1, 1));
        float zMultiplier = 1 + (settings.ZAccelerationMultiplier - 1) * alignment;
        var forwardVelocity = forward * Vector3.Dot(baseVelocity, forward);
        var lateralVelocity = baseVelocity - forwardVelocity;
        return lateralVelocity + forwardVelocity * zMultiplier;
    }

    private void FinishTurn(FlightMotionSettings settings)
    {
        bool belowCutoff = settings.InertiaCutoffEnabled &&
            BelowCutoff(turnVelocity.Length(), settings.TurnCutoffRadiansPerSecond);
        angularMotion.Velocity = SuppressReleaseVelocity(turnVelocity, settings.TurnCutoffRadiansPerSecond, settings)
            * settings.TurnAccelerationMultiplier;

        angularMotion.ExemptionSeconds = belowCutoff ? 0 : ExemptionDuration(
            angularMotion.Velocity.Length(),
            MathF.PI / 1800,
            settings.TurnDecelerationExemptionDurationRatio,
            settings);
    }

    // Quaternion-derived speeds can round just below an exact threshold.
    private static bool BelowCutoff(float speed, float cutoff) => speed < cutoff * (1 - 0.000001f);

    private static Vector3 SuppressReleaseVelocity(Vector3 velocity, float cutoff, FlightMotionSettings settings)
    {
        float speed = velocity.Length();
        if (!settings.InertiaCutoffEnabled || !BelowCutoff(speed, cutoff))
            return velocity;
        if (settings.InertiaFullCutEnabled || settings.InertiaSuppressionStrength >= 1)
            return Vector3.Zero;

        // Multiplying the original velocity preserves its root-space direction.
        // The resulting speed is cutoff * (speed / cutoff)^(1 / (1 - strength)).
        float exponent = settings.InertiaSuppressionStrength / (1 - settings.InertiaSuppressionStrength);
        return velocity * MathF.Pow(speed / cutoff, exponent);
    }

    private static float ExemptionDuration(
        float speed,
        float terminalSpeed,
        float durationRatio,
        FlightMotionSettings settings)
    {
        if (!settings.DecelerationExemptionEnabled ||
            durationRatio <= 0 ||
            settings.InertiaDecelerationPerSecond <= 0 ||
            speed <= terminalSpeed)
            return 0;

        float positiveTerminalSpeed = MathF.Max(terminalSpeed, 0.00001f);
        float expectedSeconds = MathF.Log(speed / positiveTerminalSpeed) / settings.InertiaDecelerationPerSecond;
        return expectedSeconds * durationRatio;
    }

    private void CancelDrag()
    {
        linearMotion.Clear();
        dragVelocity = Vector3.Zero;
        dragAnchorInRoot = previousDragPosition = Vector3.Zero;
        dragAccelerationBoostActive = false;
    }

    private void CancelTurn()
    {
        angularMotion.Clear();
        turnVelocity = Vector3.Zero;
        useTwoHandTurnPivotForInertia = false;
    }

    private static float RotationAngleAroundAxis(Quaternion rotation, Vector3 axis)
    {
        axis = SafeDirection(axis);
        if (axis.LengthSquared() <= 0)
            return 0;

        rotation = Quaternion.Normalize(rotation);
        if (rotation.W < 0)
            rotation = -rotation;

        var imaginary = new Vector3(rotation.X, rotation.Y, rotation.Z);
        float imaginaryLength = imaginary.Length();
        if (imaginaryLength <= 0.0000001f)
            return 0;

        float angle = 2 * MathF.Atan2(imaginaryLength, MathF.Max(0, rotation.W));
        return Vector3.Dot(imaginary / imaginaryLength * angle, axis);
    }

    private static Vector3 SafeDirection(Vector3 value) =>
        value.LengthSquared() <= 0.0000000001f ? Vector3.Zero : Vector3.Normalize(value);

    private static bool HandsUsable(InputFrame frame, ManipulationHands hands) =>
        (!hands.HasFlag(ManipulationHands.Left) || Usable(frame.Left)) &&
        (!hands.HasFlag(ManipulationHands.Right) || Usable(frame.Right));


    private static bool Usable(HandSample hand) => hand.IsTracked && hand.Pose.IsValid;
}
