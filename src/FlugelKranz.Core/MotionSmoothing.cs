using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Moves applied motion toward an independently calculated target pose.</summary>
internal static class MotionSmoothing
{
    private const float PositionTolerance = 0.00001f;
    private const float RotationTolerance = 0.0000001f;

    public static float Follow(float current, float target, float smoothSeconds, float elapsedSeconds)
    {
        if (smoothSeconds <= 0)
            return target;
        if (elapsedSeconds <= 0 || current == target)
            return current;

        float result = float.Lerp(current, target, Alpha(smoothSeconds, elapsedSeconds));
        return MathF.Abs(target - result) <= PositionTolerance ? target : result;
    }

    public static Vector3 Follow(
        Vector3 current,
        Vector3 target,
        float smoothSeconds,
        float elapsedSeconds)
    {
        if (smoothSeconds <= 0)
            return target;
        if (elapsedSeconds <= 0 || current == target)
            return current;

        Vector3 result = Vector3.Lerp(current, target, Alpha(smoothSeconds, elapsedSeconds));
        return Vector3.DistanceSquared(result, target) <= PositionTolerance * PositionTolerance
            ? target
            : result;
    }

    public static Quaternion Follow(
        Quaternion current,
        Quaternion target,
        float smoothSeconds,
        float elapsedSeconds)
    {
        if (smoothSeconds <= 0)
            return target;
        if (elapsedSeconds <= 0)
            return current;
        if (1 - MathF.Abs(Quaternion.Dot(current, target)) <= RotationTolerance)
            return target;

        Quaternion result = Quaternion.Normalize(Quaternion.Slerp(
            current,
            target,
            Alpha(smoothSeconds, elapsedSeconds)));
        return 1 - MathF.Abs(Quaternion.Dot(result, target)) <= RotationTolerance
            ? target
            : result;
    }

    private static float Alpha(float smoothSeconds, float elapsedSeconds) =>
        1 - MathF.Exp(-elapsedSeconds / smoothSeconds);
}
