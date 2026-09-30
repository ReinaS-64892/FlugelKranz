namespace FlugelKranz.Core;

/// <summary>Release-to-arm and hysteresis for logical Drag / Turn, independent of the movement policy.</summary>
internal sealed class LogicalHandInputs
{
    private bool leftDragArmed, rightDragArmed, leftTurnArmed, rightTurnArmed;

    public void Release() => leftDragArmed = rightDragArmed = leftTurnArmed = rightTurnArmed = false;

    public void DisarmDrag(byte hands)
    {
        if ((hands & 1) != 0) leftDragArmed = false;
        if ((hands & 2) != 0) rightDragArmed = false;
    }

    public void DisarmTurn(byte hands)
    {
        if ((hands & 1) != 0) leftTurnArmed = false;
        if ((hands & 2) != 0) rightTurnArmed = false;
    }

    public byte ActiveHands(InputFrame frame, bool drag, byte previous)
    {
        bool left = drag
            ? Held(frame.Left, frame.Left.Drag, ref leftDragArmed, (previous & 1) != 0)
            : Held(frame.Left, frame.Left.Turn, ref leftTurnArmed, (previous & 1) != 0);
        bool right = drag
            ? Held(frame.Right, frame.Right.Drag, ref rightDragArmed, (previous & 2) != 0)
            : Held(frame.Right, frame.Right.Turn, ref rightTurnArmed, (previous & 2) != 0);
        return (byte)((left ? 1 : 0) | (right ? 2 : 0));
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
