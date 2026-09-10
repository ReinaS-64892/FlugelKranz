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
        time += sampleSeconds;
        if (!frame.HeadTracked || !frame.Head.IsValid)
        {
            Release();
            return Offset;
        }

        bool wasDragging = IsDragging;
        bool wasTurning = IsTurning;
        IsDragging = Held(frame.Left, ref leftArmed, wasDragging);
        IsTurning = Held(frame.Right, ref rightArmed, wasTurning);
        bool beganDrag = IsDragging && !wasDragging;
        bool beganTurn = IsTurning && !wasTurning;

        if (beganDrag || beganTurn)
        {
            float retained = 1 - settings.BrakeStrength;
            linearInertia *= retained;
            angularInertia *= retained;
            linearExemptionSeconds *= retained;
            angularExemptionSeconds *= retained;
        }

        if (beganDrag)
        {
            dragAnchor = Offset.Transform(frame.Left.Pose.Position);
            dragHistory.Begin(time, Offset);
        }

        if (beganTurn)
        {
            turnAnchor = Quaternion.Normalize(Offset.Orientation * frame.Right.Pose.Orientation);
            turnHistory.Begin(time, Offset);
        }

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

        var before = Offset;
        AdvanceFreeInertia(frame.Head.Position, dt, !IsDragging, !IsTurning, settings);
        if (IsDragging)
        {
            dragAnchor += linearInertia * dt;
            ApplyDeceleration(
                ref linearInertia,
                ref linearExemptionSeconds,
                dt,
                settings.DragCutoffMetresPerSecond,
                settings);
        }
        if (IsTurning)
        {
            turnAnchor = IntegrateRotation(turnAnchor, angularInertia, dt);
            ApplyDeceleration(
                ref angularInertia,
                ref angularExemptionSeconds,
                dt,
                settings.TurnCutoffRadiansPerSecond,
                settings);
        }

        var target = GrabTarget(frame);
        float alpha = settings.SmoothSeconds <= 0 || dt <= 0
            ? 1
            : 1 - MathF.Exp(-dt / settings.SmoothSeconds);
        var smoothedRotation = Quaternion.Normalize(
            Quaternion.Slerp(Offset.Orientation, target.Orientation, alpha));
        var smoothedPosition = Offset.Position;
        if (IsDragging)
        {
            var dragTarget = dragAnchor - Vector3.Transform(frame.Left.Pose.Position, smoothedRotation);
            smoothedPosition = Vector3.Lerp(Offset.Position, dragTarget, alpha);
        }
        else if (IsTurning)
        {
            var headInRoot = Offset.Transform(frame.Head.Position);
            smoothedPosition = headInRoot - Vector3.Transform(frame.Head.Position, smoothedRotation);
        }

        Offset = new(smoothedRotation, smoothedPosition);

        if (sampleSeconds > 0)
        {
            if (IsDragging)
                dragVelocity = (Offset.Position - before.Position) / sampleSeconds;
            if (IsTurning)
                turnVelocity = RotationVelocity(before.Orientation, Offset.Orientation, sampleSeconds);
        }

        if (IsDragging)
            dragHistory.Add(time, Offset);
        if (IsTurning)
            turnHistory.Add(time, Offset);

        return Offset;
    }

    private RigidPose GrabTarget(InputFrame frame)
    {
        var rotation = Offset.Orientation;
        var translation = Offset.Position;
        if (IsTurning)
        {
            rotation = Quaternion.Normalize(turnAnchor * Quaternion.Conjugate(frame.Right.Pose.Orientation));
            translation = Offset.Transform(frame.Head.Position) - Vector3.Transform(frame.Head.Position, rotation);
        }

        if (IsDragging)
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
                settings.DragCutoffMetresPerSecond,
                settings);
        if (turn)
            ApplyDeceleration(
                ref angularInertia,
                ref angularExemptionSeconds,
                dt,
                settings.TurnCutoffRadiansPerSecond,
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
            dragVelocity = Vector3.Lerp(dragVelocity, dragHistory.AverageLinearVelocity, settings.DragCorrectionStrength);

        if (settings.InertiaCutoffEnabled && dragVelocity.Length() < settings.DragCutoffMetresPerSecond)
            linearInertia = Vector3.Zero;
        else
            linearInertia = dragVelocity * settings.DragAccelerationMultiplier;

        linearExemptionSeconds = ExemptionDuration(
            linearInertia.Length(),
            settings.InertiaCutoffEnabled ? settings.DragCutoffMetresPerSecond : 0.001f,
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
            turnVelocity = Vector3.Lerp(turnVelocity, turnHistory.AverageAngularVelocity, settings.TurnCorrectionStrength);

        if (settings.InertiaCutoffEnabled && turnVelocity.Length() < settings.TurnCutoffRadiansPerSecond)
            angularInertia = Vector3.Zero;
        else
            angularInertia = turnVelocity * settings.TurnAccelerationMultiplier;

        angularExemptionSeconds = ExemptionDuration(
            angularInertia.Length(),
            settings.InertiaCutoffEnabled ? settings.TurnCutoffRadiansPerSecond : MathF.PI / 1800,
            settings);

        turnHistory.Clear();
    }

    private static void ApplyDeceleration(
        ref Vector3 velocity,
        ref float exemptionSeconds,
        float elapsedSeconds,
        float terminalSpeed,
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
        if ((settings.InertiaCutoffEnabled && velocity.Length() < terminalSpeed) ||
            velocity.LengthSquared() < 0.0000000001f)
            velocity = Vector3.Zero;
    }

    private static float ExemptionDuration(float speed, float terminalSpeed, FlightMotionSettings settings)
    {
        if (!settings.DecelerationExemptionEnabled ||
            settings.DecelerationExemptionDurationRatio <= 0 ||
            settings.InertiaDecelerationPerSecond <= 0 ||
            speed <= terminalSpeed)
            return 0;

        float positiveTerminalSpeed = MathF.Max(terminalSpeed, 0.00001f);
        float expectedSeconds = MathF.Log(speed / positiveTerminalSpeed) / settings.InertiaDecelerationPerSecond;
        return expectedSeconds * settings.DecelerationExemptionDurationRatio;
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
