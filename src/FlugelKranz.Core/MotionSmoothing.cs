using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Moves applied motion toward an independently calculated target pose.</summary>
internal static class MotionSmoothing
{
    // Stop interpolation below tracking-relevant motion instead of leaving a long exponential tail.
    private const float PositionSettleDistanceMetres = 0.0001f;
    private const float RotationSettleDotTolerance = 0.0000001f;

    public static float Follow(float current, float target, float smoothSeconds, float elapsedSeconds)
    {
        if (smoothSeconds <= 0)
            return target;
        if (elapsedSeconds <= 0 || current == target)
            return current;

        float result = float.Lerp(current, target, Alpha(smoothSeconds, elapsedSeconds));
        return MathF.Abs(target - result) <= PositionSettleDistanceMetres ? target : result;
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
        return Vector3.DistanceSquared(result, target) <=
            PositionSettleDistanceMetres * PositionSettleDistanceMetres
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
        if (1 - MathF.Abs(Quaternion.Dot(current, target)) <= RotationSettleDotTolerance)
            return target;

        Quaternion result = Quaternion.Normalize(Quaternion.Slerp(
            current,
            target,
            Alpha(smoothSeconds, elapsedSeconds)));
        return 1 - MathF.Abs(Quaternion.Dot(result, target)) <= RotationSettleDotTolerance
            ? target
            : result;
    }

    private static float Alpha(float smoothSeconds, float elapsedSeconds) =>
        1 - MathF.Exp(-elapsedSeconds / smoothSeconds);
}
