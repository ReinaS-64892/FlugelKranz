using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Consumes poses in the unmodified physical tracking origin, never transformed XR poses.</summary>
public sealed class FreeFlightManipulator
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
    private const byte LeftHand = 1;
    private const byte RightHand = 2;
    private const byte BothHands = LeftHand | RightHand;
    private const float MaximumStepSeconds = 0.1f;
    private bool leftDragArmed, rightDragArmed, leftTurnArmed, rightTurnArmed;
    private byte dragHands, turnHands;
    private Vector3 linearInertia, dragVelocity;
    private Quaternion dragReferenceOrientation;
    private Vector3 previousDragPosition, currentDragDelta, smoothedDragDelta;
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
        linearInertia = dragVelocity = angularInertia = turnVelocity = Vector3.Zero;
        dragReferenceOrientation = turnStartOrientation = turnStartOffsetOrientation = Quaternion.Identity;
        previousDragPosition = currentDragDelta = smoothedDragDelta = twoHandTurnAxis = Vector3.Zero;
        dragAccelerationBoostActive = false;
        dragBrakeElapsed = turnBrakeElapsed = 0;
        dragBrakeFactor = turnBrakeFactor = 1;
        linearExemptionSeconds = angularExemptionSeconds = 0;
    }

    public void SetOffset(RigidPose offset)
    {
        if (!offset.IsValid)
            throw new ArgumentException("Invalid space offset.", nameof(offset));

        Offset = offset;
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
        bool dragHandsChanged = dragHands != previousDragHands;
        bool turnHandsChanged = turnHands != previousTurnHands;
        bool beganDrag = previousDragHands == 0 && dragHands != 0;
        bool beganTurn = previousTurnHands == 0 && turnHands != 0;

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

        if (dragHandsChanged && dragHands != 0)
            RebaseDrag(frame);
        if (turnHandsChanged && turnHands != 0)
            RebaseTurn(frame);

        var orientationBefore = Offset.Orientation;
        AdvanceFreeInertia(frame, dt, !IsDragging, !IsTurning, settings);
        Vector3 dragInertiaStep = Vector3.Zero;
        if (IsDragging)
        {
            currentDragDelta = Vector3.Zero;
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
            dragInertiaStep = linearInertia * dt;
            ApplyDeceleration(
                ref linearInertia,
                ref linearExemptionSeconds,
                dt,
                settings);
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
                settings);
        }

        var target = GrabTarget(
            frame,
            dragInertiaStep,
            sampleSeconds,
            settings,
            includeCurrentDragDelta: false);
        if (turnHandsChanged && IsTurning)
            previousTurnTarget = target;
        UpdateRawVelocities(
            frame,
            target,
            IsDragging && !dragHandsChanged,
            IsTurning && !turnHandsChanged,
            sampleSeconds);
        target = GrabTarget(
            frame,
            dragInertiaStep,
            sampleSeconds,
            settings,
            includeCurrentDragDelta: true);
        ApplySmoothedTarget(frame, target, dt, settings);
        RotateLinearInertia(orientationBefore, Offset.Orientation, settings.VectorRotationMultiplier);
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
        dragReferenceOrientation = Offset.Orientation;
        previousDragPosition = HandPosition(frame, dragHands);
        currentDragDelta = smoothedDragDelta = dragVelocity = Vector3.Zero;
    }

    private void RebaseTurn(InputFrame frame)
    {
        turnStartOrientation = HandOrientation(frame, turnHands);
        turnStartOffsetOrientation = Offset.Orientation;
        turnAnchor = Quaternion.Normalize(Offset.Orientation * turnStartOrientation);
        twoHandTurnAxis = turnHands == BothHands
            ? SafeDirection(frame.Right.Pose.Position - frame.Left.Pose.Position)
            : Vector3.Zero;
        turnVelocity = Vector3.Zero;
        previousTurnTarget = Offset;
    }

    private void ApplySmoothedTarget(
        InputFrame frame,
        RigidPose target,
        float elapsedSeconds,
        FlightMotionSettings settings)
    {
        float turnAlpha = SmoothingAlpha(settings.TurnSmoothSeconds, elapsedSeconds);
        var smoothedRotation = IsTurning
            ? Quaternion.Normalize(Quaternion.Slerp(Offset.Orientation, target.Orientation, turnAlpha))
            : Offset.Orientation;
        var smoothedPosition = Offset.Position;
        if (IsTurning)
        {
            Vector3 pivot = TurnPivot(frame, settings);
            Vector3 pivotInRoot = Offset.Transform(pivot);
            Vector3 fullTurnPosition = pivotInRoot - Vector3.Transform(pivot, target.Orientation);
            Vector3 dragAndInertiaTranslation = target.Position - fullTurnPosition;
            smoothedPosition = pivotInRoot - Vector3.Transform(pivot, smoothedRotation) +
                dragAndInertiaTranslation;
        }
        else if (IsDragging)
        {
            // The complete controller displacement is retained. Smoothing only changes
            // its direction; release velocity always uses the unsmoothed sample.
            smoothedPosition = target.Position;
        }

        Offset = new(smoothedRotation, smoothedPosition);
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
            currentDragDelta = -Vector3.Transform(controllerDelta, dragReferenceOrientation);
            dragVelocity = currentDragDelta / sampleSeconds;
            previousDragPosition = current;
        }
        else if (!dragging)
            currentDragDelta = Vector3.Zero;

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

    internal static float SmoothingAlpha(float smoothSeconds, float elapsedSeconds) =>
        smoothSeconds <= 0 || elapsedSeconds <= 0
            ? 1
            : 1 - MathF.Exp(-elapsedSeconds / smoothSeconds);

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

    private RigidPose GrabTarget(
        InputFrame frame,
        Vector3 dragInertiaStep,
        float elapsedSeconds,
        FlightMotionSettings settings,
        bool includeCurrentDragDelta)
    {
        var rotation = Offset.Orientation;
        var translation = Offset.Position;
        if (IsTurning)
        {
            rotation = TurnTargetOrientation(frame);
            Vector3 pivot = TurnPivot(frame, settings);
            translation = Offset.Transform(pivot) - Vector3.Transform(pivot, rotation);
        }

        if (IsDragging)
        {
            translation += dragInertiaStep;
            if (includeCurrentDragDelta)
                translation += SmoothDragDelta(currentDragDelta, elapsedSeconds, settings.DragSmoothSeconds);
        }

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

    private Vector3 SmoothDragDelta(Vector3 rawDelta, float elapsedSeconds, float smoothSeconds)
    {
        if (rawDelta.LengthSquared() <= 0)
        {
            float decayAlpha = SmoothingAlpha(smoothSeconds, elapsedSeconds);
            smoothedDragDelta = Vector3.Lerp(smoothedDragDelta, Vector3.Zero, decayAlpha);
            return Vector3.Zero;
        }

        float alpha = SmoothingAlpha(smoothSeconds, elapsedSeconds);
        smoothedDragDelta = Vector3.Lerp(smoothedDragDelta, rawDelta, alpha);
        return smoothedDragDelta.LengthSquared() <= 0.0000000001f
            ? rawDelta
            : Vector3.Normalize(smoothedDragDelta) * rawDelta.Length();
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

        var rotation = Offset.Orientation;
        var translation = Offset.Position;
        if (turn && angularInertia.LengthSquared() > 0)
        {
            Vector3 pivot = TurnPivot(frame, settings);
            var pivotInRoot = Offset.Transform(pivot);
            rotation = IntegrateRotation(rotation, angularInertia, dt);
            translation = pivotInRoot - Vector3.Transform(pivot, rotation);
        }

        if (move)
            translation += linearInertia * dt;

        Offset = new(rotation, translation);
        if (move)
            ApplyDeceleration(
                ref linearInertia,
                ref linearExemptionSeconds,
                dt,
                settings);
        if (turn)
            ApplyDeceleration(
                ref angularInertia,
                ref angularExemptionSeconds,
                dt,
                settings);
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
        FlightMotionSettings settings)
    {
        if (velocity.LengthSquared() <= 0 || elapsedSeconds <= 0)
            return;

        float exemptedSeconds = Math.Min(exemptionSeconds, elapsedSeconds);
        float regularSeconds = elapsedSeconds - exemptedSeconds;
        exemptionSeconds = MathF.Max(0, exemptionSeconds - elapsedSeconds);
        float exponent = settings.InertiaDecelerationPerSecond *
            (regularSeconds + exemptedSeconds * (1 - settings.DecelerationExemptionStrength));
        velocity *= MathF.Exp(-exponent);
        if (velocity.LengthSquared() < 0.0000000001f)
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
        currentDragDelta = smoothedDragDelta = Vector3.Zero;
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
