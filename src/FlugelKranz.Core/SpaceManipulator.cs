using System.Numerics;

namespace FlugelKranz.Core;

public readonly record struct HandSample(RigidPose Pose, float Grip, bool IsTracked);
public readonly record struct InputFrame(RigidPose Head, bool HeadTracked, HandSample Left, HandSample Right);

/// <summary>Consumes poses in the unmodified physical tracking origin, never transformed XR poses.</summary>
public sealed class SpaceManipulator
{
    private const float MaximumStepSeconds = 0.1f;
    private bool leftArmed, rightArmed;
    private Vector3 linearInertia, dragVelocity;
    private Quaternion dragReferenceOrientation;
    private Vector3 previousDragControllerPosition, currentDragDelta, smoothedDragDelta;
    private Vector3 dragGesturePosition;
    private float dragBrakeElapsed, turnBrakeElapsed;
    private float dragBrakeFactor = 1, turnBrakeFactor = 1;
    private Quaternion turnAnchor;
    private Vector3 angularInertia, turnVelocity;
    private RigidPose previousTurnTarget;
    private float linearExemptionSeconds, angularExemptionSeconds;
    private double time;
    private readonly MotionHistory dragHistory = new();
    private readonly MotionHistory turnHistory = new();
    public bool IsDragging { get; private set; }
    public bool IsTurning { get; private set; }
    public bool HasLinearInertia => linearInertia.LengthSquared() > 0;
    public bool HasAngularInertia => angularInertia.LengthSquared() > 0;
    public RigidPose Offset { get; private set; }

    public SpaceManipulator(RigidPose offset) => SetOffset(offset);

    public void Release()
    {
        IsDragging = IsTurning = leftArmed = rightArmed = false;
        linearInertia = dragVelocity = angularInertia = turnVelocity = Vector3.Zero;
        dragReferenceOrientation = Quaternion.Identity;
        previousDragControllerPosition = currentDragDelta = smoothedDragDelta = dragGesturePosition = Vector3.Zero;
        dragBrakeElapsed = turnBrakeElapsed = 0;
        dragBrakeFactor = turnBrakeFactor = 1;
        linearExemptionSeconds = angularExemptionSeconds = 0;
        dragHistory.Clear();
        turnHistory.Clear();
    }

    public void SetOffset(RigidPose offset)
    {
        if (!offset.IsValid)
            throw new ArgumentException("Invalid space offset.", nameof(offset));

        Offset = offset;
        Release();
    }

    /// <summary>Compatibility overload for deterministic step-mode updates.</summary>
    public RigidPose Update(InputFrame frame) => Update(frame, 0.01f, FlightMotionSettings.Step);

    public RigidPose Update(InputFrame frame, float elapsedSeconds, FlightMotionSettings settings)
    {
        settings = settings.Normalized();
        float sampleSeconds = float.IsFinite(elapsedSeconds) ? MathF.Max(0, elapsedSeconds) : 0;
        float dt = Math.Min(sampleSeconds, MaximumStepSeconds);
        if (!frame.HeadTracked || !frame.Head.IsValid)
            return Offset;

        time += sampleSeconds;

        bool wasDragging = IsDragging;
        bool wasTurning = IsTurning;
        IsDragging = Held(frame.Left, ref leftArmed, wasDragging);
        IsTurning = Held(frame.Right, ref rightArmed, wasTurning);
        bool beganDrag = IsDragging && !wasDragging;
        bool beganTurn = IsTurning && !wasTurning;

        if (beganDrag)
        {
            linearExemptionSeconds = 0;
            dragBrakeElapsed = 0;
            dragBrakeFactor = 1;
        }
        if (beganTurn)
        {
            angularExemptionSeconds = 0;
            turnBrakeElapsed = 0;
            turnBrakeFactor = 1;
        }

        if (beganDrag)
        {
            dragReferenceOrientation = Offset.Orientation;
            previousDragControllerPosition = frame.Left.Pose.Position;
            currentDragDelta = smoothedDragDelta = dragGesturePosition = Vector3.Zero;
        }
        if (beganTurn)
            turnAnchor = Quaternion.Normalize(Offset.Orientation * frame.Right.Pose.Orientation);

        if (wasDragging && !IsDragging)
        {
            if (Usable(frame.Left))
                FinishDrag(frame.Head, settings);
            else
                CancelDrag();
        }
        if (wasTurning && !IsTurning)
        {
            if (Usable(frame.Right))
                FinishTurn(settings);
            else
                CancelTurn();
        }

        if (settings.StepMode)
        {
            linearInertia = angularInertia = Vector3.Zero;
            linearExemptionSeconds = angularExemptionSeconds = 0;
        }

        var orientationBefore = Offset.Orientation;
        AdvanceFreeInertia(frame.Head.Position, dt, !IsDragging, !IsTurning, settings);
        Vector3 dragInertiaStep = Vector3.Zero;
        if (IsDragging)
        {
            currentDragDelta = Vector3.Zero;
            ApplyGripBrake(
                ref linearInertia,
                ref dragBrakeElapsed,
                ref dragBrakeFactor,
                dt,
                settings.BrakeStrength,
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
            ApplyGripBrake(
                ref angularInertia,
                ref turnBrakeElapsed,
                ref turnBrakeFactor,
                dt,
                settings.BrakeStrength,
                settings.BrakeRampSeconds);
            turnAnchor = IntegrateRotation(turnAnchor, angularInertia, dt);
            ApplyDeceleration(
                ref angularInertia,
                ref angularExemptionSeconds,
                dt,
                settings);
        }

        // Build a base target before measuring this frame's controller movement. This
        // gives turn history a stable sample while the drag measurement is refreshed
        // below, independently of any turn-induced translation.
        var target = GrabTarget(
            frame,
            IsDragging,
            IsTurning,
            dragInertiaStep,
            sampleSeconds,
            settings,
            includeCurrentDragDelta: false);
        if (beganDrag || beganTurn)
        {
            if (beganDrag)
            {
                dragVelocity = Vector3.Zero;
                dragHistory.Begin(time, new(Quaternion.Identity, Vector3.Zero));
            }
            if (beganTurn)
            {
                turnVelocity = Vector3.Zero;
                previousTurnTarget = target;
                turnHistory.Begin(time, target);
            }
        }
        UpdateRawVelocities(
            frame,
            target,
            IsDragging && !beganDrag,
            IsTurning && !beganTurn,
            sampleSeconds);
        target = GrabTarget(
            frame,
            IsDragging,
            IsTurning,
            dragInertiaStep,
            sampleSeconds,
            settings,
            includeCurrentDragDelta: true);
        ApplySmoothedTarget(frame, target, IsDragging, IsTurning, dt, settings);
        RotateLinearInertia(orientationBefore, Offset.Orientation, settings.VectorRotationMultiplier);
        return Offset;
    }

    private void ApplySmoothedTarget(
        InputFrame frame,
        RigidPose target,
        bool dragging,
        bool turning,
        float elapsedSeconds,
        FlightMotionSettings settings)
    {
        float turnAlpha = SmoothingAlpha(settings.TurnSmoothSeconds, elapsedSeconds);
        var smoothedRotation = Quaternion.Normalize(
            Quaternion.Slerp(Offset.Orientation, target.Orientation, turnAlpha));
        var smoothedPosition = Offset.Position;
        if (turning)
        {
            var headInRoot = Offset.Transform(frame.Head.Position);
            smoothedPosition = headInRoot - Vector3.Transform(frame.Head.Position, smoothedRotation);
            if (dragging)
            {
                var turnTargetPosition = headInRoot -
                    Vector3.Transform(frame.Head.Position, target.Orientation);
                smoothedPosition += target.Position - turnTargetPosition;
            }
        }
        else if (dragging)
        {
            // The drag position follows the complete controller displacement. Smoothing
            // is applied to the movement direction in GrabTarget, while release velocity
            // remains based on the unsmoothed displacement.
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
            var controllerDelta = frame.Left.Pose.Position - previousDragControllerPosition;
            currentDragDelta = -Vector3.Transform(controllerDelta, dragReferenceOrientation);
            dragVelocity = currentDragDelta / sampleSeconds;
            previousDragControllerPosition = frame.Left.Pose.Position;
            dragGesturePosition += currentDragDelta;
            dragHistory.Add(time, new(Quaternion.Identity, dragGesturePosition));
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
            turnHistory.Add(time, target);
        }
    }

    private static float SmoothingAlpha(float smoothSeconds, float elapsedSeconds) =>
        smoothSeconds <= 0 || elapsedSeconds <= 0
            ? 1
            : 1 - MathF.Exp(-elapsedSeconds / smoothSeconds);

    private static void ApplyGripBrake(
        ref Vector3 velocity,
        ref float elapsed,
        ref float previousFactor,
        float deltaSeconds,
        float strength,
        float rampSeconds)
    {
        if (deltaSeconds <= 0 || velocity.LengthSquared() <= 0 || strength <= 0)
            return;

        if (rampSeconds <= 0)
        {
            float immediateFactor = MathF.Min(previousFactor, MathF.Max(0, 1 - strength));
            if (previousFactor > 0)
                velocity *= immediateFactor / previousFactor;
            previousFactor = immediateFactor;
            elapsed = 0;
            return;
        }

        elapsed = MathF.Min(rampSeconds, elapsed + deltaSeconds);
        float progress = elapsed / rampSeconds;
        float easedProgress = progress * progress * (3 - 2 * progress);
        float targetFactor = 1 - strength * easedProgress;
        targetFactor = MathF.Min(previousFactor, MathF.Max(0, targetFactor));

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
        bool dragging,
        bool turning,
        Vector3 dragInertiaStep,
        float elapsedSeconds,
        FlightMotionSettings settings,
        bool includeCurrentDragDelta)
    {
        var rotation = Offset.Orientation;
        var translation = Offset.Position;
        if (turning)
        {
            rotation = Quaternion.Normalize(turnAnchor * Quaternion.Conjugate(frame.Right.Pose.Orientation));
            translation = Offset.Transform(frame.Head.Position) - Vector3.Transform(frame.Head.Position, rotation);
        }

        if (dragging)
        {
            translation += dragInertiaStep;
            if (includeCurrentDragDelta)
                translation += SmoothDragDelta(currentDragDelta, elapsedSeconds, settings.DragSmoothSeconds);
        }

        return new(rotation, translation);
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
        Vector3 head,
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
            var headInRoot = Offset.Transform(head);
            rotation = IntegrateRotation(rotation, angularInertia, dt);
            translation = headInRoot - Vector3.Transform(head, rotation);
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
        if (settings.StepMode)
        {
            linearInertia = dragVelocity = Vector3.Zero;
            linearExemptionSeconds = 0;
            return;
        }

        if (settings.DirectionCorrectionEnabled && dragHistory.IsStraightPositionPath(settings.DragCorrectionMaxSeconds))
            dragVelocity = CorrectDirection(
                dragVelocity,
                dragHistory.AverageLinearVelocity,
                settings.DragCorrectionStrength);

        if (settings.InertiaCutoffEnabled && dragVelocity.Length() < settings.DragCutoffMetresPerSecond)
            linearInertia = Vector3.Zero;
        else
            linearInertia = ApplyDragAcceleration(dragVelocity, head.Orientation, settings);

        linearExemptionSeconds = ExemptionDuration(
            linearInertia.Length(),
            0.001f,
            settings.DragDecelerationExemptionDurationRatio,
            settings);

        dragHistory.Clear();
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
        if (settings.StepMode)
        {
            angularInertia = turnVelocity = Vector3.Zero;
            angularExemptionSeconds = 0;
            return;
        }

        if (settings.DirectionCorrectionEnabled && turnHistory.IsStraightRotationPath(settings.TurnCorrectionMaxSeconds))
            turnVelocity = CorrectDirection(
                turnVelocity,
                turnHistory.AverageAngularVelocity,
                settings.TurnCorrectionStrength);

        if (settings.InertiaCutoffEnabled && turnVelocity.Length() < settings.TurnCutoffRadiansPerSecond)
            angularInertia = Vector3.Zero;
        else
            angularInertia = turnVelocity * settings.TurnAccelerationMultiplier;

        angularExemptionSeconds = ExemptionDuration(
            angularInertia.Length(),
            MathF.PI / 1800,
            settings.TurnDecelerationExemptionDurationRatio,
            settings);

        turnHistory.Clear();
    }

    private static Vector3 CorrectDirection(
        Vector3 releaseVelocity,
        Vector3 averageVelocity,
        float strength)
    {
        float speed = releaseVelocity.Length();
        if (speed <= 0 || averageVelocity.LengthSquared() <= 0 || strength <= 0)
            return releaseVelocity;

        var direction = Vector3.Lerp(
            releaseVelocity / speed,
            Vector3.Normalize(averageVelocity),
            strength);
        return direction.LengthSquared() <= 0.0000000001f
            ? releaseVelocity
            : Vector3.Normalize(direction) * speed;
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
        currentDragDelta = smoothedDragDelta = dragGesturePosition = Vector3.Zero;
        dragBrakeElapsed = 0;
        dragBrakeFactor = 1;
        linearExemptionSeconds = 0;
        dragHistory.Clear();
    }

    private void CancelTurn()
    {
        angularInertia = turnVelocity = Vector3.Zero;
        turnBrakeElapsed = 0;
        turnBrakeFactor = 1;
        angularExemptionSeconds = 0;
        turnHistory.Clear();
    }

    private static Quaternion IntegrateRotation(Quaternion rotation, Vector3 velocity, float dt)
    {
        float speed = velocity.Length();
        if (speed <= 0 || dt <= 0)
            return rotation;

        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(velocity / speed, speed * dt) * rotation);
    }

    private static Vector3 RotationVelocity(Quaternion from, Quaternion to, float dt)
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

    private static float RotationDistance(Quaternion from, Quaternion to) =>
        2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(from, to)), -1, 1));

    private static bool Held(HandSample hand, ref bool armed, bool held)
    {
        if (!Usable(hand))
        {
            armed = false;
            return false;
        }

        if (hand.Grip <= 0.35f)
        {
            armed = true;
            return false;
        }

        return armed && hand.Grip >= (held ? 0.35f : 0.65f);
    }

    private static bool Usable(HandSample hand) =>
        hand.IsTracked && hand.Pose.IsValid && float.IsFinite(hand.Grip);

    private sealed class MotionHistory
    {
        private readonly List<Sample> samples = [];
        private bool exceededMaximumWindow;

        public Vector3 AverageLinearVelocity => samples.Count < 2
            ? Vector3.Zero
            : (samples[^1].Pose.Position - samples[0].Pose.Position) / (float)(samples[^1].Time - samples[0].Time);

        public Vector3 AverageAngularVelocity => samples.Count < 2
            ? Vector3.Zero
            : RotationVelocity(
                samples[0].Pose.Orientation,
                samples[^1].Pose.Orientation,
                (float)(samples[^1].Time - samples[0].Time));

        public void Begin(double time, RigidPose pose)
        {
            samples.Clear();
            exceededMaximumWindow = false;
            samples.Add(new(time, pose));
        }

        public void Add(double time, RigidPose pose)
        {
            if (samples.Count == 0)
                return;

            if (time - samples[0].Time > 5)
            {
                exceededMaximumWindow = true;
                return;
            }

            if (time - samples[^1].Time >= 1.0 / 60)
                samples.Add(new(time, pose));
        }

        public bool IsStraightPositionPath(float maximumSeconds)
        {
            if (!ValidDuration(maximumSeconds))
                return false;

            float path = 0;
            for (int i = 1; i < samples.Count; i++)
                path += Vector3.Distance(samples[i - 1].Pose.Position, samples[i].Pose.Position);

            float direct = Vector3.Distance(samples[0].Pose.Position, samples[^1].Pose.Position);
            return direct > 0.001f && path > 0 && direct / path >= 0.9f;
        }

        public bool IsStraightRotationPath(float maximumSeconds)
        {
            if (!ValidDuration(maximumSeconds))
                return false;

            float path = 0;
            for (int i = 1; i < samples.Count; i++)
                path += RotationDistance(samples[i - 1].Pose.Orientation, samples[i].Pose.Orientation);

            float direct = RotationDistance(samples[0].Pose.Orientation, samples[^1].Pose.Orientation);
            return direct > 0.001f && path > 0 && direct / path >= 0.9f;
        }

        private bool ValidDuration(float maximumSeconds) =>
            !exceededMaximumWindow &&
            samples.Count >= 2 &&
            maximumSeconds > 0 &&
            samples[^1].Time > samples[0].Time &&
            samples[^1].Time - samples[0].Time <= maximumSeconds;

        public void Clear()
        {
            samples.Clear();
            exceededMaximumWindow = false;
        }

        private readonly record struct Sample(double Time, RigidPose Pose);
    }
}
