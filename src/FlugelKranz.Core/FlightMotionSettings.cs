namespace FlugelKranz.Core;

public sealed record FlightMotionSettings
{
    public static FlightMotionSettings Default { get; } = new();
    public static FlightMotionSettings Step { get; } = new()
    {
        StepMode = true,
        DragSmoothSeconds = 0,
        TurnSmoothSeconds = 0
    };

    public bool StepMode { get; init; }
    public bool InertiaCutoffEnabled { get; init; } = true;
    public float DragCutoffMetresPerSecond { get; init; } = 0.4f;
    public float TurnCutoffRadiansPerSecond { get; init; } = MathF.PI / 4;
    public float DragAccelerationMultiplier { get; init; } = 1;
    public float TurnAccelerationMultiplier { get; init; } = 0.5f;
    public float ZAccelerationMultiplier { get; init; } = 1.5f;
    public bool InertiaAccelerationBoostEnabled { get; init; } = true;
    public float InertiaAccelerationBoostMaximumMultiplier { get; init; } = 1.5f;
    public float VectorRotationMultiplier { get; init; } = 1;
    public float InertiaDecelerationPerSecond { get; init; } = 2;
    public bool DecelerationExemptionEnabled { get; init; } = true;
    public float DragDecelerationExemptionDurationRatio { get; init; } = 0.2f;
    public float TurnDecelerationExemptionDurationRatio { get; init; } = 0.05f;
    public float DecelerationExemptionStrength { get; init; } = 0.9f;
    public float DragSmoothSeconds { get; init; } = 0.01f;
    public float TurnSmoothSeconds { get; init; } = 0.05f;
    public float BrakeRampSeconds { get; init; } = 0.2f;

    public FlightMotionSettings Normalized() => this with
    {
        DragCutoffMetresPerSecond = Math.Clamp(DragCutoffMetresPerSecond, 0, 5),
        TurnCutoffRadiansPerSecond = Math.Clamp(TurnCutoffRadiansPerSecond, 0, MathF.PI * 4),
        DragAccelerationMultiplier = Math.Clamp(DragAccelerationMultiplier, 0, 5),
        TurnAccelerationMultiplier = Math.Clamp(TurnAccelerationMultiplier, 0, 5),
        ZAccelerationMultiplier = Math.Clamp(ZAccelerationMultiplier, 1, 5),
        InertiaAccelerationBoostMaximumMultiplier = Math.Clamp(InertiaAccelerationBoostMaximumMultiplier, 1, 4),
        VectorRotationMultiplier = Math.Clamp(VectorRotationMultiplier, 0, 1),
        InertiaDecelerationPerSecond = Math.Clamp(InertiaDecelerationPerSecond, 0, 10),
        DragDecelerationExemptionDurationRatio = Math.Clamp(DragDecelerationExemptionDurationRatio, 0, 1),
        TurnDecelerationExemptionDurationRatio = Math.Clamp(TurnDecelerationExemptionDurationRatio, 0, 1),
        DecelerationExemptionStrength = Math.Clamp(DecelerationExemptionStrength, 0, 1),
        DragSmoothSeconds = Math.Clamp(DragSmoothSeconds, 0, 1),
        TurnSmoothSeconds = Math.Clamp(TurnSmoothSeconds, 0, 1),
        BrakeRampSeconds = Math.Clamp(BrakeRampSeconds, 0, 1)
    };
}
