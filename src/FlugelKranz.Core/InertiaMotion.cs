using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Integrates speed decay and a grab brake over elapsed time, including threshold crossings.</summary>
internal sealed class InertiaMotion
{
    // Preserve the former 100 Hz stopping sensitivity without depending on the current update interval.
    private const double StopReferenceSeconds = 0.01;
    private double brakeElapsed;
    private double brakeFactor = 1;
    public Vector3 Velocity;
    public float ExemptionSeconds;

    public void Clear()
    {
        Velocity = Vector3.Zero;
        StartBrake();
    }

    public void StartBrake()
    {
        ExemptionSeconds = 0;
        brakeElapsed = 0;
        brakeFactor = 1;
    }

    public Vector3 Advance(float seconds, FlightMotionSettings settings, bool braking = false)
    {
        if (seconds <= 0 || Velocity.LengthSquared() <= 0) return Vector3.Zero;
        double speed = Velocity.Length();
        double stopSpeed = settings.InertiaStopDisplacementMetres / StopReferenceSeconds;
        double exemption = ExemptionSeconds;
        double rate = settings.InertiaDecelerationPerSecond;
        double exemptRate = rate * (1 - settings.DecelerationExemptionStrength);
        double ramp = settings.BrakeRampSeconds;

        double Scale(double time)
        {
            double exemptTime = Math.Min(exemption, time);
            double decay = Math.Exp(-exemptRate * exemptTime - rate * (time - exemptTime));
            if (!braking) return decay;
            return brakeFactor <= 0 ? 0 : decay * Math.Min(brakeFactor, BrakeFactor(brakeElapsed + time, ramp)) / brakeFactor;
        }

        double duration = seconds;
        if (speed <= stopSpeed || Scale(0) == 0)
            duration = 0;
        else if (speed * Scale(duration) <= stopSpeed)
        {
            // Decay and braking are monotonic. Integrate only until the fixed stopping speed is reached.
            double lower = 0, upper = braking ? Math.Min(duration, Math.Max(0, ramp - brakeElapsed)) : duration;
            for (int i = 0; i < 40; i++)
            {
                double middle = (lower + upper) / 2;
                if (speed * Scale(middle) > stopSpeed) lower = middle;
                else upper = middle;
            }
            duration = (lower + upper) / 2;
        }

        double exemptDuration = Math.Min(exemption, duration);
        double integral = braking
            ? IntegrateBrake(0, exemptDuration, Scale) + IntegrateBrake(exemptDuration, duration, Scale)
            : DecayIntegral(exemptRate, exemptDuration)
                + Math.Exp(-exemptRate * exemptDuration) * DecayIntegral(rate, duration - exemptDuration);
        Vector3 displacement = Velocity * (float)integral;
        Velocity = duration < seconds || speed * Scale(seconds) <= stopSpeed
            ? Vector3.Zero : Velocity * (float)Scale(seconds);
        ExemptionSeconds = MathF.Max(0, ExemptionSeconds - seconds);
        if (braking)
        {
            brakeElapsed = Math.Min(ramp, brakeElapsed + seconds);
            brakeFactor = Math.Min(brakeFactor, BrakeFactor(brakeElapsed, ramp));
        }
        return displacement;
    }

    private static double BrakeFactor(double elapsed, double ramp)
    {
        if (ramp <= 0) return 0;
        double progress = Math.Clamp(elapsed / ramp, 0, 1);
        return 1 - progress * progress * (3 - 2 * progress);
    }

    private static double DecayIntegral(double rate, double seconds) =>
        rate <= 0 ? seconds : (1 - Math.Exp(-rate * seconds)) / rate;

    private static double IntegrateBrake(double start, double end, Func<double, double> scale)
    {
        // Eight-point Gauss-Legendre quadrature integrates the cubic brake envelope times an exponential.
        // Split at the exemption boundary so every interval has one decay rate.
        if (end <= start) return 0;
        ReadOnlySpan<double> nodes = [0.183434642495650, 0.525532409916329, 0.796666477413627, 0.960289856497536];
        ReadOnlySpan<double> weights = [0.362683783378362, 0.313706645877887, 0.222381034453374, 0.101228536290376];
        double middle = (start + end) / 2, half = (end - start) / 2, sum = 0;
        for (int i = 0; i < nodes.Length; i++)
            sum += weights[i] * (scale(middle - half * nodes[i]) + scale(middle + half * nodes[i]));
        return half * sum;
    }
}
