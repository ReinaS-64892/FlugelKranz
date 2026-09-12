namespace FlugelKranz.Core;

public enum FlightMode
{
    InfiniteWalking,
    FreeFlight
}

public enum TurnOrigin
{
    TrackedElementsMidpoint,
    Head
}

public sealed record FlightMotionSettings
{
    public static FlightMotionSettings Default { get; } = new();

    public TurnOrigin TurnOrigin { get; init; } = TurnOrigin.TrackedElementsMidpoint;
    public bool InertiaCutoffEnabled { get; init; } = true;
    public float DragCutoffMetresPerSecond { get; init; } = 0.4f;
    public float TurnCutoffRadiansPerSecond { get; init; } = MathF.PI / 4;
    public float DragAccelerationMultiplier { get; init; } = 1;
    public float TurnAccelerationMultiplier { get; init; } = 0.4f;
    public float ZAccelerationMultiplier { get; init; } = 2;
    public bool InertiaAccelerationBoostEnabled { get; init; } = true;
    public float InertiaAccelerationBoostMaximumMultiplier { get; init; } = 4;
    public float VectorRotationMultiplier { get; init; } = 1;
    public float InertiaDecelerationPerSecond { get; init; } = 2;
    public bool DecelerationExemptionEnabled { get; init; } = true;
    public float DragDecelerationExemptionDurationRatio { get; init; } = 0.2f;
    public float TurnDecelerationExemptionDurationRatio { get; init; } = 0.15f;
    public float DecelerationExemptionStrength { get; init; } = 0.9f;
    public float DragSmoothSeconds { get; init; } = 0.01f;
    public float TurnSmoothSeconds { get; init; } = 0.05f;
    public float BrakeRampSeconds { get; init; } = 0.4f;

    public FlightMotionSettings Normalized() => this with
    {
        TurnOrigin = Enum.IsDefined(TurnOrigin) ? TurnOrigin : TurnOrigin.TrackedElementsMidpoint,
        DragCutoffMetresPerSecond = Math.Clamp(DragCutoffMetresPerSecond, 0, 5),
        TurnCutoffRadiansPerSecond = Math.Clamp(TurnCutoffRadiansPerSecond, 0, MathF.PI * 4),
        DragAccelerationMultiplier = Math.Clamp(DragAccelerationMultiplier, 0, 5),
        TurnAccelerationMultiplier = Math.Clamp(TurnAccelerationMultiplier, 0, 5),
        ZAccelerationMultiplier = Math.Clamp(ZAccelerationMultiplier, 1, 5),
        InertiaAccelerationBoostMaximumMultiplier = Math.Clamp(InertiaAccelerationBoostMaximumMultiplier, 1, 6),
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

public sealed record InfiniteWalkingSettings
{
    public static InfiniteWalkingSettings Default { get; } = new();

    public float DragSmoothSeconds { get; init; }
    public float TurnSmoothSeconds { get; init; }
    public float TurnHeadSmoothSeconds { get; init; }

    public InfiniteWalkingSettings Normalized() => this with
    {
        DragSmoothSeconds = Math.Clamp(DragSmoothSeconds, 0, 1),
        TurnSmoothSeconds = Math.Clamp(TurnSmoothSeconds, 0, 1),
        TurnHeadSmoothSeconds = Math.Clamp(TurnHeadSmoothSeconds, 0, 1)
    };
}

public sealed record FlugelKranzSettings
{
    public const int CurrentSchemaVersion = 2;
    public static FlugelKranzSettings Default { get; } = new();

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public FlightMode Mode { get; init; } = FlightMode.InfiniteWalking;
    public FlightMotionSettings FreeFlight { get; init; } = FlightMotionSettings.Default;
    public InfiniteWalkingSettings InfiniteWalking { get; init; } = InfiniteWalkingSettings.Default;
    public ValveIndexInputSettings ValveIndex { get; init; } = ValveIndexInputSettings.Default;

    public FlugelKranzSettings Normalized() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        Mode = Enum.IsDefined(Mode) ? Mode : FlightMode.InfiniteWalking,
        FreeFlight = (FreeFlight ?? FlightMotionSettings.Default).Normalized(),
        InfiniteWalking = (InfiniteWalking ?? InfiniteWalkingSettings.Default).Normalized(),
        ValveIndex = (ValveIndex ?? ValveIndexInputSettings.Default).Normalized()
    };
}
