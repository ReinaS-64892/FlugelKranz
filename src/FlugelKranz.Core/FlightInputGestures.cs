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
        float dt = float.IsFinite(elapsedSeconds) ? MathF.Max(0, elapsedSeconds) : 0;
        // Only count intervals whose beginning and end were both observed held.
        leftSeconds = left && previousLeft ? leftSeconds + dt : 0;
        rightSeconds = right && previousRight ? rightSeconds + dt : 0;
        if (both || left != previousLeft || right != previousRight || !(left || right))
        {
            singleSeconds = 0;
            singleTriggered = false;
        }
        else
            singleSeconds += dt;

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
