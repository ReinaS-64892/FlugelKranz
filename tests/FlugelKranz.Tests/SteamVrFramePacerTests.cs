using FlugelKranz.OpenVR;
using Xunit;

namespace FlugelKranz.Tests;

public class SteamVrFramePacerTests
{
    [Fact]
    public void FixedRateExcludesProcessingTimeFromDeadlinesAndMeasuresElapsedTime()
    {
        var clock = new FakeClock();
        var pacer = new SteamVrFramePacer(new(100, false), _ => throw new InvalidOperationException(), clock);
        pacer.Wait();
        Assert.Equal(11_000, clock.Now);
        Assert.Equal(0, pacer.ElapsedSeconds);
        clock.Now += 2_500;
        pacer.Wait();
        Assert.Equal(21_000, clock.Now);
        Assert.Equal(.01f, pacer.ElapsedSeconds, .000001f);
        Assert.False(pacer.FrameSynchronized);
    }

    [Fact]
    public void FailedSyncUsesFallbackAndLongStallsDoNotProduceCatchupBursts()
    {
        var clock = new FakeClock();
        var pacer = new SteamVrFramePacer(new(100), timeout => { Assert.Equal(10u, timeout); return false; }, clock);
        pacer.Wait();
        clock.Now = 60_000;
        pacer.Wait();
        Assert.Equal(60_000, clock.Now);
        Assert.Equal(.049f, pacer.ElapsedSeconds, .000001f);
        pacer.Wait();
        Assert.Equal(70_000, clock.Now);
        Assert.False(pacer.FrameSynchronized);
    }

    [Fact]
    public void SuccessfulSyncUsesRuntimeTimingWithoutSoftwareWait()
    {
        var clock = new FakeClock();
        var pacer = new SteamVrFramePacer(new(120), _ => { clock.Now += 11_111; return true; }, clock);
        pacer.Wait();
        pacer.Wait();
        Assert.True(pacer.FrameSynchronized);
        Assert.Equal(0, clock.Waits);
        Assert.Equal(.011111f, pacer.ElapsedSeconds, .000001f);
    }

    [Fact]
    public void SpuriousImmediateSyncFallsBackToSoftwareDeadline()
    {
        var clock = new FakeClock();
        var pacer = new SteamVrFramePacer(new(100), _ => true, clock);
        pacer.Wait();
        Assert.True(pacer.FrameSynchronized);
        pacer.Wait();
        Assert.False(pacer.FrameSynchronized);
        Assert.Equal(11_000, clock.Now);
        Assert.Equal(.01f, pacer.ElapsedSeconds, .000001f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(1001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void UnsupportedRatesAreRejectedBeforeConnecting(double rate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SteamVrFlightRuntime(new(rate)));

    private sealed class FakeClock : IFrameClock
    {
        public long Now = 1000;
        public int Waits;
        public long Frequency => 1_000_000;
        public long Timestamp => Now;
        public void WaitUntil(long deadline) { Waits++; Now = Math.Max(Now, deadline); }
    }
}
