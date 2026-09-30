namespace FlugelKranz.Core;

/// <summary>Release-to-arm and hysteresis for logical Drag / Turn, independent of the movement policy.</summary>
internal sealed class LogicalHandInputs
{
    private bool leftDragArmed, rightDragArmed, leftTurnArmed, rightTurnArmed;

    public void Release() => leftDragArmed = rightDragArmed = leftTurnArmed = rightTurnArmed = false;

    public void DisarmDrag(ManipulationHands hands)
    {
        if (hands.HasFlag(ManipulationHands.Left)) leftDragArmed = false;
        if (hands.HasFlag(ManipulationHands.Right)) rightDragArmed = false;
    }

    public void DisarmTurn(ManipulationHands hands)
    {
        if (hands.HasFlag(ManipulationHands.Left)) leftTurnArmed = false;
        if (hands.HasFlag(ManipulationHands.Right)) rightTurnArmed = false;
    }

    public ManipulationHands ActiveHands(InputFrame frame, bool drag, ManipulationHands previous)
    {
        bool leftWasHeld = previous.HasFlag(ManipulationHands.Left);
        bool rightWasHeld = previous.HasFlag(ManipulationHands.Right);
        bool left = drag
            ? Held(frame.Left, frame.Left.Drag, ref leftDragArmed, leftWasHeld)
            : Held(frame.Left, frame.Left.Turn, ref leftTurnArmed, leftWasHeld);
        bool right = drag
            ? Held(frame.Right, frame.Right.Drag, ref rightDragArmed, rightWasHeld)
            : Held(frame.Right, frame.Right.Turn, ref rightTurnArmed, rightWasHeld);
        return (left ? ManipulationHands.Left : ManipulationHands.None) |
            (right ? ManipulationHands.Right : ManipulationHands.None);
    }

    private static bool Held(HandSample hand, float value, ref bool armed, bool held)
    {
        if (!hand.IsTracked || !hand.Pose.IsValid || !float.IsFinite(value))
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
}
