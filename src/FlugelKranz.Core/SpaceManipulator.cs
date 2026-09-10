using System.Numerics;

namespace FlugelKranz.Core;

public readonly record struct HandSample(RigidPose Pose, float Grip, bool IsTracked);
public readonly record struct InputFrame(RigidPose Head, bool HeadTracked, HandSample Left, HandSample Right);

/// <summary>Consumes poses in the unmodified physical tracking origin, never transformed XR poses.</summary>
public sealed class SpaceManipulator
{
    private const float MaximumStepSeconds = 0.1f;
    private bool leftArmed, rightArmed;
    private Vector3 dragAnchor, linearInertia, dragVelocity;
    private Quaternion turnAnchor;
    private Vector3 angularInertia, turnVelocity;
    private RigidPose previousDragTarget, previousTurnTarget;
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

        float retained = 1 - settings.BrakeStrength;
        if (beganDrag)
        {
            linearInertia *= retained;
            linearExemptionSeconds = 0;
        }
        if (beganTurn)
        {
            angularInertia *= retained;
            angularExemptionSeconds = 0;
        }

        if (beganDrag)
            dragAnchor = Offset.Transform(frame.Left.Pose.Position);
        if (beganTurn)
            turnAnchor = Quaternion.Normalize(Offset.Orientation * frame.Right.Pose.Orientation);

        if (wasDragging && !IsDragging)
        {
            if (Usable(frame.Left))
                FinishDrag(settings);
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
        if (IsDragging)
        {
            dragAnchor += linearInertia * dt;
            ApplyDeceleration(
                ref linearInertia,
                ref linearExemptionSeconds,
                dt,
                settings);
        }
        if (IsTurning)
        {
            turnAnchor = IntegrateRotation(turnAnchor, angularInertia, dt);
            ApplyDeceleration(
                ref angularInertia,
                ref angularExemptionSeconds,
                dt,
                settings);
        }

        var target = GrabTarget(frame, IsDragging, IsTurning);
        if (beganDrag || beganTurn)
        {
            if (beganDrag)
            {
                dragVelocity = Vector3.Zero;
                previousDragTarget = target;
                dragHistory.Begin(time, target);
            }
            if (beganTurn)
            {
                turnVelocity = Vector3.Zero;
                previousTurnTarget = target;
                turnHistory.Begin(time, target);
            }
        }
        UpdateRawVelocities(target, IsDragging && !beganDrag, IsTurning && !beganTurn, sampleSeconds);
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
        float dragAlpha = SmoothingAlpha(settings.DragSmoothSeconds, elapsedSeconds);
        var smoothedRotation = Quaternion.Normalize(
            Quaternion.Slerp(Offset.Orientation, target.Orientation, turnAlpha));
        var smoothedPosition = Offset.Position;
        if (dragging)
        {
            var dragTarget = dragAnchor - Vector3.Transform(frame.Left.Pose.Position, smoothedRotation);
            smoothedPosition = Vector3.Lerp(Offset.Position, dragTarget, dragAlpha);
        }
        else if (turning)
        {
            var headInRoot = Offset.Transform(frame.Head.Position);
            smoothedPosition = headInRoot - Vector3.Transform(frame.Head.Position, smoothedRotation);
        }

        Offset = new(smoothedRotation, smoothedPosition);
    }

    private void UpdateRawVelocities(
        RigidPose target,
        bool dragging,
        bool turning,
        float sampleSeconds)
    {
        if (dragging && sampleSeconds > 0)
        {
            dragVelocity = (target.Position - previousDragTarget.Position) / sampleSeconds;
            previousDragTarget = target;
            dragHistory.Add(time, target);
        }

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

    private RigidPose GrabTarget(InputFrame frame, bool dragging, bool turning)
    {
        var rotation = Offset.Orientation;
        var translation = Offset.Position;
        if (turning)
        {
            rotation = Quaternion.Normalize(turnAnchor * Quaternion.Conjugate(frame.Right.Pose.Orientation));
            translation = Offset.Transform(frame.Head.Position) - Vector3.Transform(frame.Head.Position, rotation);
        }

        if (dragging)
            translation = dragAnchor - Vector3.Transform(frame.Left.Pose.Position, rotation);

        return new(rotation, translation);
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

    private void FinishDrag(FlightMotionSettings settings)
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
            linearInertia = dragVelocity * settings.DragAccelerationMultiplier;

        linearExemptionSeconds = ExemptionDuration(
            linearInertia.Length(),
            0.001f,
            settings.DragDecelerationExemptionDurationRatio,
            settings);

        dragHistory.Clear();
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
        linearExemptionSeconds = 0;
        dragHistory.Clear();
    }

    private void CancelTurn()
    {
        angularInertia = turnVelocity = Vector3.Zero;
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
