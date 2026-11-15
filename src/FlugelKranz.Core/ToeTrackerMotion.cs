using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Estimates support motion, with a bounded settling period after a downward impact.</summary>
internal sealed class ToeTrackerMotion(Vector3 position, Vector3 floorPosition)
{
    private const float MaximumSampleSeconds = 0.05f;
    private const float MinimumDescentMetres = 0.02f;
    private const float MinimumDescentSpeed = 0.2f;
    private const float SettlingSeconds = 0.12f;
    private const float MaximumBounceMetres = 0.015f;
    private const float MaximumHorizontalDriftMetres = 0.01f;

    private Vector3 previousPosition = position;
    private Vector3 previousFloorPosition = floorPosition;
    private Vector3 previousVelocity;
    private float descentMetres;
    private Vector3? impactPosition;
    private float settlingElapsed;

    public float Speed { get; private set; }

    public void Update(Vector3 position, Vector3 floorPosition, float dt, float groundRange)
    {
        if (dt <= 0)
        {
            ResetImpact();
            previousPosition = position;
            previousFloorPosition = floorPosition;
            return;
        }

        Vector3 floorStep = floorPosition - previousFloorPosition;
        Vector3 velocity = floorStep / dt;
        float horizontalSpeed = new Vector2(velocity.X, velocity.Z).Length();
        float measuredSpeed = Vector3.Distance(position, previousPosition) / dt;
        bool nearFloor = floorPosition.Y >= 0 && floorPosition.Y <= groundRange;
        if (dt > MaximumSampleSeconds || !nearFloor)
            ResetImpact();
        else
        {
            // A downward stroke of at least 2 cm must brake or reverse near the floor.
            // Small repeated bounces cannot repeatedly restart the settling window.
            if (impactPosition is null && descentMetres >= MinimumDescentMetres
                && previousVelocity.Y <= -MinimumDescentSpeed && velocity.Y >= -0.05f
                && new Vector2(previousVelocity.X, previousVelocity.Z).Length() <= 0.15f
                && horizontalSpeed <= 0.15f)
            {
                impactPosition = previousFloorPosition;
                settlingElapsed = 0;
                // Drop the approach-speed tail: it describes descent, not motion after impact.
                Speed = MathF.Min(Speed, 0.05f);
            }

            if (impactPosition is { } impact)
            {
                settlingElapsed += dt;
                Vector3 drift = floorPosition - impact;
                float horizontalDrift = new Vector2(drift.X, drift.Z).Length();
                if (settlingElapsed > SettlingSeconds || MathF.Abs(drift.Y) > MaximumBounceMetres
                    || horizontalDrift > MaximumHorizontalDriftMetres || horizontalSpeed > 0.5f)
                {
                    impactPosition = null;
                }
                else
                {
                    // Ignore bounded lateral jitter, while coherent sliding still raises the speed.
                    float driftSpeed = MathF.Max(0, horizontalDrift - 0.002f) / MathF.Max(0.02f, settlingElapsed);
                    measuredSpeed = new Vector2(driftSpeed, velocity.Y * 0.1f).Length();
                }
            }
            descentMetres = floorStep.Y < 0 ? descentMetres - floorStep.Y : 0;
        }

        Speed = float.Lerp(Speed, measuredSpeed, 1 - MathF.Exp(-dt / 0.1f));
        previousPosition = position;
        previousFloorPosition = floorPosition;
        previousVelocity = velocity;
    }

    private void ResetImpact()
    {
        impactPosition = null;
        settlingElapsed = 0;
        descentMetres = 0;
        previousVelocity = Vector3.Zero;
    }
}
