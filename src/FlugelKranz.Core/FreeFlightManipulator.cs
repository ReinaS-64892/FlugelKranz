using System.Numerics;
using static FlugelKranz.Core.RotationMath;

namespace FlugelKranz.Core;

/// <summary>Consumes poses in the unmodified physical tracking origin, never transformed XR poses.</summary>
public sealed class FreeFlightManipulator : ManipulationMode
{
    // Stop feeding imperceptibly small per-update changes to the runtime. The
    // threshold is applied to displacement (or radians), so it follows the
    // actual update interval rather than an arbitrary velocity value.
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
    private const float MaximumStepSeconds = 0.1f;
    private Vector3 linearInertia, dragVelocity;
    private Quaternion dragReferenceOrientation;
    private Vector3 dragAnchorInRoot, previousDragPosition;
    private bool positionTargetUsesDragSmoothing;
    private bool dragAccelerationBoostActive;
    private float dragBrakeElapsed, turnBrakeElapsed;
    private float dragBrakeFactor = 1, turnBrakeFactor = 1;
    private Quaternion turnAnchor, turnStartOrientation, turnStartOffsetOrientation;
    private Vector3 twoHandTurnAxis;
    private Vector3 twoHandTurnPivot;
    private Quaternion previousTwoHandOrientation;
    private bool useTwoHandTurnPivotForInertia;
    private Vector3 angularInertia, turnVelocity;
    private RigidPose previousTurnTarget;
    private float linearExemptionSeconds, angularExemptionSeconds;
    public bool HasLinearInertia => linearInertia.LengthSquared() > 0;
    public bool HasAngularInertia => angularInertia.LengthSquared() > 0;

    public FreeFlightManipulator(RigidPose offset) => SetOffset(offset);

    protected override void ReleaseMotion()
    {
        linearInertia = dragVelocity = angularInertia = turnVelocity = Vector3.Zero;
        dragReferenceOrientation = turnStartOrientation = turnStartOffsetOrientation = Quaternion.Identity;
        dragAnchorInRoot = previousDragPosition = twoHandTurnAxis = twoHandTurnPivot = Vector3.Zero;
        previousTwoHandOrientation = Quaternion.Identity;
        useTwoHandTurnPivotForInertia = false;
        positionTargetUsesDragSmoothing = false;
        dragAccelerationBoostActive = false;
        dragBrakeElapsed = turnBrakeElapsed = 0;
        dragBrakeFactor = turnBrakeFactor = 1;
        linearExemptionSeconds = angularExemptionSeconds = 0;
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
        float dt = Math.Min(sampleSeconds, MaximumStepSeconds);
        if (!frame.HeadTracked || !frame.Head.IsValid)
            return Offset;

        var hands = ReadHands(frame, endTwoHandOnRelease: true);
        HandleGripChanges(frame, hands, settings);

        bool linearMotionActive = linearInertia.LengthSquared() > 0;
        bool angularMotionActive = angularInertia.LengthSquared() > 0;
        var orientationBefore = targetOffset.Orientation;
        AdvanceMotion(frame, dt, sampleSeconds, settings);

        var target = GrabTarget(frame, settings);
        UpdateRawVelocities(
            frame,
            target,
            IsDragging && !hands.Drag.Changed,
            IsTurning && !hands.Turn.Changed,
            sampleSeconds);
        targetOffset = target;
        if (IsDragging || linearMotionActive)
            positionTargetUsesDragSmoothing = true;
        else if (IsTurning || angularMotionActive)
            positionTargetUsesDragSmoothing = false;
        RotateLinearInertia(
            orientationBefore,
            targetOffset.Orientation,
            settings.VectorRotationMultiplier);
        FollowTarget(frame, dt, settings);
        return Offset;
    }

    private void HandleGripChanges(InputFrame frame, HandChanges hands, FlightMotionSettings settings)
    {
        if (hands.Drag.Ended)
        {
            if (HandsUsable(frame, hands.Drag.Previous))
                FinishDrag(frame.Head, settings);
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
        }

        if (hands.Drag.Began)
        {
            linearExemptionSeconds = 0;
            dragBrakeElapsed = 0;
            dragBrakeFactor = 1;
            dragAccelerationBoostActive = false;
        }
        if (hands.Turn.Began || hands.Drag.BecameTwoHanded)
        {
            angularExemptionSeconds = 0;
            turnBrakeElapsed = 0;
            turnBrakeFactor = 1;
        }

        if (!IsDragging && !IsTurning)
        {
            // Smoothing may leave the presented offset behind targetOffset when
            // a grip is released. Start inertia from what is actually visible
            // instead of snapping to the unapplied target first.
            targetOffset = Offset;
        }

        if (hands.NeedsRebase)
        {
            // A new grip starts from the pose currently presented to the runtime,
            // discarding any unapplied smoothing remainder without a jump.
            targetOffset = Offset;
            if (IsDragging)
                RebaseDrag(frame);
            if (IsTurning)
                RebaseTurn(frame);
        }
    }

    private void AdvanceMotion(InputFrame frame, float dt, float sampleSeconds, FlightMotionSettings settings)
    {
        bool brakingAngularInertia = IsTurning || dragHands == ManipulationHands.Both;
        AdvanceFreeInertia(frame, dt, !IsDragging, !brakingAngularInertia, settings);
        if (IsDragging)
        {
            float controllerSpeed = ControllerMovementSpeed(frame, sampleSeconds);
            dragAccelerationBoostActive =
                settings.InertiaAccelerationBoostEnabled &&
                controllerSpeed > settings.DragCutoffMetresPerSecond;
            if (!dragAccelerationBoostActive)
                ApplyGrabBrake(
                    ref linearInertia,
                    ref dragBrakeElapsed,
                    ref dragBrakeFactor,
                    dt,
                    settings.BrakeRampSeconds);
            Vector3 dragInertiaStep = linearInertia * dt;
            dragAnchorInRoot += dragInertiaStep;
            ApplyDeceleration(
                ref linearInertia,
                ref linearExemptionSeconds,
                dt,
                settings,
                settings.InertiaStopDisplacementMetres);
        }
        if (IsTurning)
        {
            ApplyGrabBrake(
                ref angularInertia,
                ref turnBrakeElapsed,
                ref turnBrakeFactor,
                dt,
                settings.BrakeRampSeconds);
            if (turnHands == ManipulationHands.Both)
                turnStartOffsetOrientation = IntegrateRotation(turnStartOffsetOrientation, angularInertia, dt);
            else
                turnAnchor = IntegrateRotation(turnAnchor, angularInertia, dt);
            ApplyDeceleration(
                ref angularInertia,
                ref angularExemptionSeconds,
                dt,
                settings,
                settings.InertiaStopDisplacementMetres);
        }
        else if (dragHands == ManipulationHands.Both)
        {
            ApplyGrabBrake(
                ref angularInertia,
                ref turnBrakeElapsed,
                ref turnBrakeFactor,
                dt,
                settings.BrakeRampSeconds);
            AdvanceTargetRotation(frame, angularInertia, dt, settings);
            ApplyDeceleration(
                ref angularInertia,
                ref angularExemptionSeconds,
                dt,
                settings,
                settings.InertiaStopDisplacementMetres);
        }
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
        float elapsedSeconds,
        FlightMotionSettings settings)
    {
        if (!IsDragging && !IsTurning)
        {
            ApplyOffset(targetOffset);
            return;
        }

        float positionSmoothSeconds = positionTargetUsesDragSmoothing
            ? settings.DragSmoothSeconds
            : settings.TurnSmoothSeconds;
        Quaternion orientation = MotionSmoothing.Follow(
            Offset.Orientation,
            targetOffset.Orientation,
            IsTurning ? settings.TurnSmoothSeconds : 0,
            elapsedSeconds);
        Vector3 position;
        if (IsDragging)
        {
            Vector3 dragPoint = HandPosition(frame, dragHands);
            Vector3 dragPointInRoot = MotionSmoothing.Follow(
                Offset.Transform(dragPoint),
                dragAnchorInRoot,
                settings.DragSmoothSeconds,
                elapsedSeconds);
            position = dragPointInRoot - Vector3.Transform(dragPoint, orientation);
        }
        else if (positionTargetUsesDragSmoothing)
        {
            position = MotionSmoothing.Follow(
                Offset.Position,
                targetOffset.Position,
                positionSmoothSeconds,
                elapsedSeconds);
        }
        else
        {
            Vector3 pivot = TurnPivot(frame, settings);
            Vector3 pivotInRoot = MotionSmoothing.Follow(
                Offset.Transform(pivot),
                targetOffset.Transform(pivot),
                positionSmoothSeconds,
                elapsedSeconds);
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

    private static void ApplyGrabBrake(
        ref Vector3 velocity,
        ref float elapsed,
        ref float previousFactor,
        float deltaSeconds,
        float rampSeconds)
    {
        if (deltaSeconds <= 0 || velocity.LengthSquared() <= 0)
            return;

        if (rampSeconds <= 0)
        {
            velocity = Vector3.Zero;
            previousFactor = 0;
            elapsed = 0;
            return;
        }

        elapsed = MathF.Min(rampSeconds, elapsed + deltaSeconds);
        float progress = elapsed / rampSeconds;
        float easedProgress = progress * progress * (3 - 2 * progress);
        float targetFactor = MathF.Min(previousFactor, MathF.Max(0, 1 - easedProgress));
        if (previousFactor > 0)
            velocity *= targetFactor / previousFactor;
        previousFactor = targetFactor;
    }

    private void RotateLinearInertia(Quaternion from, Quaternion to, float multiplier)
    {
        if (linearInertia.LengthSquared() <= 0 || multiplier <= 0)
            return;

        var delta = Quaternion.Normalize(to * Quaternion.Conjugate(from));
        if (delta.W < 0)
            delta = -delta;

        var applied = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, delta, multiplier));
        linearInertia = Vector3.Transform(linearInertia, applied);
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
            (turnHands == ManipulationHands.None && useTwoHandTurnPivotForInertia && angularInertia.LengthSquared() > 0))
            return twoHandTurnPivot;
        return GetSingleHandTurnPivot(frame, settings);
    }

    internal Vector3 GetSingleHandTurnPivot(InputFrame frame, FlightMotionSettings settings)
    {
        if (IsDragging)
            return HandPosition(frame, dragHands);
        if (settings.TurnOrigin == TurnOrigin.Head)
            return frame.Head.Position;

        Vector3 sum = frame.Head.Position;
        int count = 1;
        if (Usable(frame.Left))
        {
            sum += frame.Left.Pose.Position;
            count++;
        }
        if (Usable(frame.Right))
        {
            sum += frame.Right.Pose.Position;
            count++;
        }
        return sum / count;
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

    private void AdvanceFreeInertia(
        InputFrame frame,
        float dt,
        bool move,
        bool turn,
        FlightMotionSettings settings)
    {
        if (dt <= 0)
            return;

        var rotation = targetOffset.Orientation;
        var translation = targetOffset.Position;
        if (turn && angularInertia.LengthSquared() > 0)
        {
            Vector3 pivot = TurnPivot(frame, settings);
            var pivotInRoot = targetOffset.Transform(pivot);
            rotation = IntegrateRotation(rotation, angularInertia, dt);
            translation = pivotInRoot - Vector3.Transform(pivot, rotation);
        }

        if (move)
            translation += linearInertia * dt;

        targetOffset = new(rotation, translation);
        if (move)
            ApplyDeceleration(
                ref linearInertia,
                ref linearExemptionSeconds,
                dt,
                settings,
                settings.InertiaStopDisplacementMetres);
        if (turn)
            ApplyDeceleration(
                ref angularInertia,
                ref angularExemptionSeconds,
                dt,
                settings,
                settings.InertiaStopDisplacementMetres);
    }

    private void FinishDrag(RigidPose head, FlightMotionSettings settings)
    {
        bool hasUsableAcceleration = dragVelocity.LengthSquared() > 0.0000000001f &&
            (!settings.InertiaCutoffEnabled || dragVelocity.Length() >= settings.DragCutoffMetresPerSecond);
        if (hasUsableAcceleration)
        {
            // dragVelocity is in root space. Apply the reference-space rotation
            // to the physical HMD orientation before comparing their directions.
            var headInRoot = Quaternion.Normalize(Offset.Orientation * head.Orientation);
            var acceleration = ApplyDragAcceleration(dragVelocity, headInRoot, settings);
            float speedBeforeBoost = linearInertia.Length();
            if (dragAccelerationBoostActive && speedBeforeBoost > 0.0000000001f)
            {
                var boosted = linearInertia + acceleration;
                float boostReferenceSpeed = MathF.Max(speedBeforeBoost, acceleration.Length());
                float maximumSpeed = boostReferenceSpeed * settings.InertiaAccelerationBoostMaximumMultiplier;
                if (boosted.Length() > maximumSpeed && maximumSpeed > 0)
                    boosted = Vector3.Normalize(boosted) * maximumSpeed;
                linearInertia = boosted;
            }
            else
                linearInertia = acceleration;

            linearExemptionSeconds = ExemptionDuration(
                linearInertia.Length(),
                0.001f,
                settings.DragDecelerationExemptionDurationRatio,
                settings);
        }
        else if (linearInertia.LengthSquared() <= 0.0000000001f)
        {
            linearInertia = Vector3.Zero;
            linearExemptionSeconds = 0;
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
        if (settings.InertiaCutoffEnabled && turnVelocity.Length() < settings.TurnCutoffRadiansPerSecond)
            angularInertia = Vector3.Zero;
        else
            angularInertia = turnVelocity * settings.TurnAccelerationMultiplier;

        angularExemptionSeconds = ExemptionDuration(
            angularInertia.Length(),
            MathF.PI / 1800,
            settings.TurnDecelerationExemptionDurationRatio,
            settings);
    }

    private static void ApplyDeceleration(
        ref Vector3 velocity,
        ref float exemptionSeconds,
        float elapsedSeconds,
        FlightMotionSettings settings,
        float stopCutoff)
    {
        if (velocity.LengthSquared() <= 0 || elapsedSeconds <= 0)
            return;

        float exemptedSeconds = Math.Min(exemptionSeconds, elapsedSeconds);
        float regularSeconds = elapsedSeconds - exemptedSeconds;
        exemptionSeconds = MathF.Max(0, exemptionSeconds - elapsedSeconds);
        float exponent = settings.InertiaDecelerationPerSecond *
            (regularSeconds + exemptedSeconds * (1 - settings.DecelerationExemptionStrength));
        velocity *= MathF.Exp(-exponent);
        if (velocity.Length() * elapsedSeconds < stopCutoff)
            velocity = Vector3.Zero;
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
        linearInertia = dragVelocity = Vector3.Zero;
        dragAnchorInRoot = previousDragPosition = Vector3.Zero;
        dragAccelerationBoostActive = false;
        dragBrakeElapsed = 0;
        dragBrakeFactor = 1;
        linearExemptionSeconds = 0;
    }

    private void CancelTurn()
    {
        angularInertia = turnVelocity = Vector3.Zero;
        turnBrakeElapsed = 0;
        turnBrakeFactor = 1;
        angularExemptionSeconds = 0;
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
