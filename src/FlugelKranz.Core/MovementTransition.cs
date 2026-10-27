using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>One-second movement that hands its applied offset to a destination mode on completion.</summary>
public abstract class MovementTransition : MovementMode
{
    public const float DurationSeconds = 1;
    private readonly RigidPose start;
    private readonly RigidPose target;
    private readonly Vector3? pivot;
    private readonly Vector3 startPivotInRoot;
    private readonly Vector3 targetPivotInRoot;
    private RigidPose current;
    private float elapsed;
    private bool handedOff;

    protected MovementTransition(RigidPose start, RigidPose target, ManipulationMode destination, Vector3? pivot = null)
    {
        if (!start.IsValid || !target.IsValid)
            throw new ArgumentException("Transition poses must be valid.");
        this.start = current = start;
        this.target = target;
        this.pivot = pivot;
        startPivotInRoot = pivot is { } startPoint ? start.Transform(startPoint) : default;
        targetPivotInRoot = pivot is { } targetPoint ? target.Transform(targetPoint) : default;
        Destination = destination;
        destination.SetOffset(start);
    }

    public ManipulationMode Destination { get; }
    public bool IsComplete => elapsed >= DurationSeconds;
    public RigidPose Current => current;
    public override RigidPose Offset => current;
    public override MovementMode NextMode => IsComplete ? Destination : this;
    public override RigidPose Update(InputFrame frame, float elapsedSeconds, FlugelKranzSettings settings) =>
        Advance(elapsedSeconds);
    public override void Release() => Destination.SetOffset(current);

    public virtual MovementMode Interrupt(RigidPose appliedOffset)
    {
        Destination.SetOffset(appliedOffset);
        return Destination;
    }

    public RigidPose Advance(float elapsedSeconds)
    {
        if (float.IsFinite(elapsedSeconds) && elapsedSeconds > 0)
            elapsed = MathF.Min(DurationSeconds, elapsed + elapsedSeconds);
        if (IsComplete)
        {
            current = target;
            if (!handedOff)
            {
                Destination.SetOffset(current);
                handedOff = true;
            }
            return current;
        }

        float progress = elapsed / DurationSeconds;
        float eased = progress * progress * (3 - 2 * progress);
        Quaternion orientation = Quaternion.Normalize(Quaternion.Slerp(start.Orientation, target.Orientation, eased));
        Vector3 position = pivot is { } point
            ? Vector3.Lerp(startPivotInRoot, targetPivotInRoot, eased) - Vector3.Transform(point, orientation)
            : Vector3.Lerp(start.Position, target.Position, eased);
        return current = new(orientation, position);
    }
}
