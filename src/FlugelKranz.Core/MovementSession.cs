namespace FlugelKranz.Core;

/// <summary>Owns one active operation and the user-selected destination. All handoffs use the applied offset.</summary>
public sealed class MovementSession
{
    private readonly RigidPose original;

    public MovementSession(ManipulationMode initialMode, RigidPose current, RigidPose original)
    {
        this.original = original;
        SelectedMode = initialMode;
        SelectedMode.SetOffset(current);
        ActiveMode = initialMode;
    }

    public ManipulationMode SelectedMode { get; private set; }
    public MovementMode ActiveMode { get; private set; }
    public bool IsTransitioning => ActiveMode is MovementTransition;

    public void Select(ManipulationMode destination, RigidPose appliedOffset, InputFrame frame, FlugelKranzSettings settings)
    {
        var previous = SelectedMode;
        // Entry may need the previous grip's pivot; capture it before releasing the source.
        var next = destination.EnterFrom(previous, new(appliedOffset, original, frame, settings));
        ActiveMode.Release();
        SelectedMode = destination;
        ActiveMode = next;
    }

    public void Release(RigidPose appliedOffset)
    {
        ActiveMode.Release();
        if (ActiveMode is MovementTransition transition)
            ActiveMode = transition.Interrupt(appliedOffset);
        else
        {
            SelectedMode.SetOffset(appliedOffset);
            ActiveMode = SelectedMode;
        }
    }

    public void Reset(RigidPose appliedOffset)
    {
        ActiveMode.Release();
        SelectedMode.SetOffset(appliedOffset);
        ActiveMode = SelectedMode;
    }

    public bool StartSpaceReset(InputFrame frame, FlugelKranzSettings settings)
    {
        if (IsTransitioning || !SelectedMode.CanReset(frame))
            return false;
        // Capture the turn pivot before releasing the current grip and inertia.
        var transition = SelectedMode.CreateReset(frame, original, settings);
        SelectedMode.Release();
        ActiveMode = transition;
        return true;
    }

    public RigidPose Update(InputFrame frame, float elapsedSeconds, FlugelKranzSettings settings)
    {
        var offset = ActiveMode.Update(frame, elapsedSeconds, settings);
        ActiveMode = ActiveMode.NextMode;
        return offset;
    }
}
