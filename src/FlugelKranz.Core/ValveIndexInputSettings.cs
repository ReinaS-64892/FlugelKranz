namespace FlugelKranz.Core;

public sealed record ValveIndexInputSettings
{
    public static ValveIndexInputSettings Default { get; } = new();

    public float PositionDeadZone { get; init; } = 0.3f;
    public float ForceThreshold { get; init; } = 0.5f;

    public ValveIndexInputSettings Normalized() => this with
    {
        PositionDeadZone = Math.Clamp(PositionDeadZone, 0, 1),
        ForceThreshold = Math.Clamp(ForceThreshold, 0, 1)
    };
}
