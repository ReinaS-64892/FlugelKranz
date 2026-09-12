using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Consumes poses in the unmodified physical tracking origin, never transformed XR poses.</summary>
public sealed class FreeFlightManipulator
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
    private const byte LeftHand = 1;
    private const byte RightHand = 2;
    private const byte BothHands = LeftHand | RightHand;
    private const float MaximumStepSeconds = 0.1f;
    private bool leftDragArmed, rightDragArmed, leftTurnArmed, rightTurnArmed;
    private byte dragHands, turnHands;
    private RigidPose targetOffset;
    private Vector3 linearInertia, dragVelocity;
    private Quaternion dragReferenceOrientation;
    private Vector3 dragAnchorInRoot, previousDragPosition;
    private bool positionTargetUsesDragSmoothing;
    private bool dragAccelerationBoostActive;
    private float dragBrakeElapsed, turnBrakeElapsed;
    private float dragBrakeFactor = 1, turnBrakeFactor = 1;
    private Quaternion turnAnchor, turnStartOrientation, turnStartOffsetOrientation;
    private Vector3 twoHandTurnAxis;
    private Vector3 angularInertia, turnVelocity;
    private RigidPose previousTurnTarget;
    private float linearExemptionSeconds, angularExemptionSeconds;
    public bool IsDragging => dragHands != 0;
    public bool IsTurning => turnHands != 0;
    public bool HasLinearInertia => linearInertia.LengthSquared() > 0;
    public bool HasAngularInertia => angularInertia.LengthSquared() > 0;
    public RigidPose Offset { get; private set; }

    public FreeFlightManipulator(RigidPose offset) => SetOffset(offset);

    public void Release()
    {
        dragHands = turnHands = 0;
        leftDragArmed = rightDragArmed = leftTurnArmed = rightTurnArmed = false;
        targetOffset = Offset;
        linearInertia = dragVelocity = angularInertia = turnVelocity = Vector3.Zero;
        dragReferenceOrientation = turnStartOrientation = turnStartOffsetOrientation = Quaternion.Identity;
        dragAnchorInRoot = previousDragPosition = twoHandTurnAxis = Vector3.Zero;
        positionTargetUsesDragSmoothing = false;
        dragAccelerationBoostActive = false;
        dragBrakeElapsed = turnBrakeElapsed = 0;
        dragBrakeFactor = turnBrakeFactor = 1;
        linearExemptionSeconds = angularExemptionSeconds = 0;
    }

    public void SetOffset(RigidPose offset)
    {
        if (!offset.IsValid)
            throw new ArgumentException("Invalid space offset.", nameof(offset));

        Offset = targetOffset = offset;
        Release();
    }

    public RigidPose Update(InputFrame frame) => Update(frame, 0.01f, DirectManipulationSettings);

    public RigidPose Update(InputFrame frame, float elapsedSeconds, FlightMotionSettings settings)
    {
        settings = settings.Normalized();
        float sampleSeconds = float.IsFinite(elapsedSeconds) ? MathF.Max(0, elapsedSeconds) : 0;
        float dt = Math.Min(sampleSeconds, MaximumStepSeconds);
        if (!frame.HeadTracked || !frame.Head.IsValid)
            return Offset;

        byte previousDragHands = dragHands;
        byte previousTurnHands = turnHands;
        dragHands = ActiveHands(frame, true, previousDragHands);
        turnHands = ActiveHands(frame, false, previousTurnHands);
        if (previousDragHands == BothHands && dragHands is LeftHand or RightHand)
        {
            if (dragHands == LeftHand)
                leftDragArmed = false;
            else
                rightDragArmed = false;
            dragHands = 0;
        }
        if (previousTurnHands == BothHands && turnHands is LeftHand or RightHand)
        {
            if (turnHands == LeftHand)
                leftTurnArmed = false;
            else
                rightTurnArmed = false;
            turnHands = 0;
        }
        bool dragHandsChanged = dragHands != previousDragHands;
        bool turnHandsChanged = turnHands != previousTurnHands;
        bool beganDrag = previousDragHands == 0 && dragHands != 0;
        bool beganTurn = previousTurnHands == 0 && turnHands != 0;
        bool beganTwoHandDrag = previousDragHands != BothHands && dragHands == BothHands;

        if (previousDragHands != 0 && dragHands == 0)
        {
            if (HandsUsable(frame, previousDragHands))
                FinishDrag(frame.Head, settings);
            else
                CancelDrag();
        }
        if (previousTurnHands != 0 && turnHands == 0)
        {
            if (HandsUsable(frame, previousTurnHands))
                FinishTurn(settings);
            else
                CancelTurn();
        }

        if (beganDrag)
        {
            linearExemptionSeconds = 0;
            dragBrakeElapsed = 0;
            dragBrakeFactor = 1;
            dragAccelerationBoostActive = false;
        }
        if (beganTurn)
        {
            angularExemptionSeconds = 0;
            turnBrakeElapsed = 0;
            turnBrakeFactor = 1;
        }
        if (beganTwoHandDrag)
        {
            angularExemptionSeconds = 0;
            turnBrakeElapsed = 0;
            turnBrakeFactor = 1;
        }

        bool activeHandsChanged =
            (dragHandsChanged && dragHands != 0) ||
            (turnHandsChanged && turnHands != 0);
        if (activeHandsChanged)
        {
            // A new grip starts from the pose currently presented to the runtime,
            // discarding any unapplied smoothing remainder without a jump.
            targetOffset = Offset;
            if (IsDragging)
                RebaseDrag(frame);
            if (IsTurning)
                RebaseTurn(frame);
        }

        bool linearMotionActive = linearInertia.LengthSquared() > 0;
        bool angularMotionActive = angularInertia.LengthSquared() > 0;
        bool brakingAngularInertia = IsTurning || dragHands == BothHands;
        var orientationBefore = targetOffset.Orientation;
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
            if (turnHands == BothHands)
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
        else if (dragHands == BothHands)
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

        var target = GrabTarget(frame, settings);
        UpdateRawVelocities(
            frame,
            target,
            IsDragging && !dragHandsChanged,
            IsTurning && !turnHandsChanged,
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

    private byte ActiveHands(InputFrame frame, bool drag, byte previous)
    {
        byte result = 0;
        bool leftHeld = (previous & LeftHand) != 0;
        bool rightHeld = (previous & RightHand) != 0;
        bool left = drag
            ? Held(frame.Left, frame.Left.Drag, ref leftDragArmed, leftHeld)
            : Held(frame.Left, frame.Left.Turn, ref leftTurnArmed, leftHeld);
        bool right = drag
            ? Held(frame.Right, frame.Right.Drag, ref rightDragArmed, rightHeld)
            : Held(frame.Right, frame.Right.Turn, ref rightTurnArmed, rightHeld);
        if (left)
            result |= LeftHand;
        if (right)
            result |= RightHand;
        return result;
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
        twoHandTurnAxis = turnHands == BothHands
            ? SafeDirection(frame.Right.Pose.Position - frame.Left.Pose.Position)
            : Vector3.Zero;
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
            Offset = targetOffset;
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
        Offset = new(orientation, position);
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
        Quaternion current = HandOrientation(frame, turnHands);
        if (turnHands != BothHands)
            return Quaternion.Normalize(turnAnchor * Quaternion.Conjugate(current));

        Vector3 currentAxis = SafeDirection(frame.Right.Pose.Position - frame.Left.Pose.Position);
        Vector3 axis = currentAxis.LengthSquared() > 0 ? currentAxis : twoHandTurnAxis;
        if (axis.LengthSquared() <= 0)
            return turnStartOffsetOrientation;

        Quaternion controllerDelta = Quaternion.Normalize(current * Quaternion.Conjugate(turnStartOrientation));
        Quaternion twist = TwistAroundAxis(controllerDelta, axis);
        return Quaternion.Normalize(turnStartOffsetOrientation * Quaternion.Conjugate(twist));
    }

    private Vector3 TurnPivot(InputFrame frame, FlightMotionSettings settings)
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
            var acceleration = ApplyDragAcceleration(dragVelocity, head.Orientation, settings);
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
    }

    internal static Quaternion IntegrateRotation(Quaternion rotation, Vector3 velocity, float dt)
    {
        float speed = velocity.Length();
        if (speed <= 0 || dt <= 0)
            return rotation;

        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(velocity / speed, speed * dt) * rotation);
    }

    internal static Vector3 RotationVelocity(Quaternion from, Quaternion to, float dt)
    {
        var delta = Quaternion.Normalize(to * Quaternion.Conjugate(from));
        if (delta.W < 0)
            delta = -delta;

        float angle = 2 * MathF.Acos(Math.Clamp(delta.W, -1, 1));
        float sine = MathF.Sqrt(MathF.Max(0, 1 - delta.W * delta.W));
        return sine < 0.00001f
            ? Vector3.Zero
            : new Vector3(delta.X, delta.Y, delta.Z) / sine * (angle / dt);
    }

    internal static Quaternion TwistAroundAxis(Quaternion rotation, Vector3 axis)
    {
        axis = SafeDirection(axis);
        if (axis.LengthSquared() <= 0)
            return Quaternion.Identity;

        var imaginary = new Vector3(rotation.X, rotation.Y, rotation.Z);
        var projected = axis * Vector3.Dot(imaginary, axis);
        var twist = new Quaternion(projected, rotation.W);
        return twist.LengthSquared() <= 0.0000000001f
            ? Quaternion.Identity
            : Quaternion.Normalize(twist);
    }

    private static Quaternion HandOrientation(InputFrame frame, byte hands) => hands switch
    {
        LeftHand => frame.Left.Pose.Orientation,
        RightHand => frame.Right.Pose.Orientation,
        BothHands => Average(frame.Left.Pose.Orientation, frame.Right.Pose.Orientation),
        _ => Quaternion.Identity
    };

    private static Vector3 HandPosition(InputFrame frame, byte hands) => hands switch
    {
        LeftHand => frame.Left.Pose.Position,
        RightHand => frame.Right.Pose.Position,
        BothHands => (frame.Left.Pose.Position + frame.Right.Pose.Position) * 0.5f,
        _ => Vector3.Zero
    };

    private static Quaternion Average(Quaternion left, Quaternion right)
    {
        if (Quaternion.Dot(left, right) < 0)
            right = -right;
        return Quaternion.Normalize(Quaternion.Slerp(left, right, 0.5f));
    }

    private static Vector3 SafeDirection(Vector3 value) =>
        value.LengthSquared() <= 0.0000000001f ? Vector3.Zero : Vector3.Normalize(value);

    private static bool HandsUsable(InputFrame frame, byte hands) =>
        ((hands & LeftHand) == 0 || Usable(frame.Left)) &&
        ((hands & RightHand) == 0 || Usable(frame.Right));

    private static bool Held(HandSample hand, float value, ref bool armed, bool held)
    {
        if (!Usable(hand) || !float.IsFinite(value))
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

    private static bool Usable(HandSample hand) => hand.IsTracked && hand.Pose.IsValid;
}
