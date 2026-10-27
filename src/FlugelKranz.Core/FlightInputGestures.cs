namespace FlugelKranz.Core;

internal readonly record struct FlightGesture(bool ToggleMode, bool ResetSpace);

/// <summary>Tracks staggered two-hand holds and single-hand holds independently of movement.</summary>
internal sealed class FlightInputGestures
{
    private bool previousLeft, previousRight;
    private float leftSeconds, rightSeconds, singleSeconds;
    private bool modeTriggered, singleTriggered;

    public FlightGesture Update(InputFrame frame, float elapsedSeconds, bool canReset)
    {
        bool left = Held(frame.Left);
        bool right = Held(frame.Right);
        bool both = left && right;
        leftSeconds = left ? leftSeconds + elapsedSeconds : 0;
        rightSeconds = right ? rightSeconds + elapsedSeconds : 0;
        if (both || left != previousLeft || right != previousRight || !(left || right))
        {
            singleSeconds = 0;
            singleTriggered = false;
        }
        else
            singleSeconds += elapsedSeconds;

        if (!both || !(previousLeft && previousRight))
            modeTriggered = false;
        previousLeft = left;
        previousRight = right;

        if (both && !modeTriggered && (leftSeconds >= 1 || rightSeconds >= 1))
        {
            modeTriggered = true;
            return new(true, false);
        }
        if (!both && !singleTriggered && singleSeconds >= 1 && canReset)
        {
            singleTriggered = true;
            return new(false, true);
        }
        return default;
    }

    private static bool Held(HandSample hand) => hand.IsTracked && hand.Pose.IsValid &&
        float.IsFinite(hand.DpadDown) && hand.DpadDown >= 0.65f;
}
