namespace FlugelKranz.Core;

/// <summary>A movement operation, including a temporary operation that moves into another mode.</summary>
public abstract class MovementMode
{
    public abstract RigidPose Offset { get; }
    public virtual bool IsDragging => false;
    public virtual bool IsTurning => false;
    public virtual string? StatusMessage => null;
    public virtual MovementMode NextMode => this;
    public abstract RigidPose Update(InputFrame frame, float elapsedSeconds, FlugelKranzSettings settings);
    public abstract void Release();
}
