namespace FlugelKranz.Core;

public sealed record FlightMotionSettings
{
    public static FlightMotionSettings Default { get; } = new();
    public static FlightMotionSettings Step { get; } = new() { StepMode = true, SmoothSeconds = 0 };

    public bool StepMode { get; init; }
    public bool InertiaCutoffEnabled { get; init; } = true;
    public float DragCutoffMetresPerSecond { get; init; } = 0.05f;
    public float TurnCutoffRadiansPerSecond { get; init; } = MathF.PI / 36;
    public float DragAccelerationMultiplier { get; init; } = 1;
    public float TurnAccelerationMultiplier { get; init; } = 1;
    public float InertiaDecelerationPerSecond { get; init; } = 0.01f;
    public bool DecelerationExemptionEnabled { get; init; } = true;
    public float DecelerationExemptionDurationRatio { get; init; } = 0.2f;
    public float DecelerationExemptionStrength { get; init; } = 0.5f;
    public float SmoothSeconds { get; init; } = 0.05f;
    public float BrakeStrength { get; init; } = 1;
    public bool DirectionCorrectionEnabled { get; init; } = true;
    public float DragCorrectionMaxSeconds { get; init; } = 1;
    public float DragCorrectionStrength { get; init; } = 1;
    public float TurnCorrectionMaxSeconds { get; init; } = 0.5f;
    public float TurnCorrectionStrength { get; init; } = 1;

    public FlightMotionSettings Normalized() => this with
    {
        DragCutoffMetresPerSecond = Math.Clamp(DragCutoffMetresPerSecond, 0, 5),
        TurnCutoffRadiansPerSecond = Math.Clamp(TurnCutoffRadiansPerSecond, 0, MathF.PI * 4),
        DragAccelerationMultiplier = Math.Clamp(DragAccelerationMultiplier, 0, 5),
        TurnAccelerationMultiplier = Math.Clamp(TurnAccelerationMultiplier, 0, 5),
        InertiaDecelerationPerSecond = Math.Clamp(InertiaDecelerationPerSecond, 0, 10),
        DecelerationExemptionDurationRatio = Math.Clamp(DecelerationExemptionDurationRatio, 0, 1),
        DecelerationExemptionStrength = Math.Clamp(DecelerationExemptionStrength, 0, 1),
        SmoothSeconds = Math.Clamp(SmoothSeconds, 0, 1),
        BrakeStrength = Math.Clamp(BrakeStrength, 0, 1),
        DragCorrectionMaxSeconds = Math.Clamp(DragCorrectionMaxSeconds, 0, 5),
        DragCorrectionStrength = Math.Clamp(DragCorrectionStrength, 0, 1),
        TurnCorrectionMaxSeconds = Math.Clamp(TurnCorrectionMaxSeconds, 0, 5),
        TurnCorrectionStrength = Math.Clamp(TurnCorrectionStrength, 0, 1)
    };
}
