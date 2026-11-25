using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FlugelKranz.OpenVR;

internal interface IFrameClock
{
    long Frequency { get; }
    long Timestamp { get; }
    void WaitUntil(long deadline);
}

/// <summary>Absolute software deadlines or compositor sync, always with measured elapsed time.</summary>
internal sealed class SteamVrFramePacer(SteamVrOptions options, Func<uint, bool> synchronize, IFrameClock clock)
{
    private long lastFrame, nextDeadline;
    private bool hasFrame;
    public bool FrameSynchronized { get; private set; }
    public float ElapsedSeconds { get; private set; }

    public void Wait()
    {
        long period = Math.Max(1, (long)(clock.Frequency / options.UpdateRate));
        long before = clock.Timestamp;
        FrameSynchronized = options.SynchronizeFrames && synchronize((uint)Math.Ceiling(1000 / options.UpdateRate));
        if (!FrameSynchronized || (hasFrame && clock.Timestamp - lastFrame < clock.Frequency / 2000))
        {
            FrameSynchronized = false;
            if (nextDeadline == 0) nextDeadline = before + period;
            clock.WaitUntil(nextDeadline);
        }
        long now = clock.Timestamp;
        ElapsedSeconds = hasFrame ? (float)((now - lastFrame) / (double)clock.Frequency) : 0;
        hasFrame = true;
        lastFrame = now;
        // Preserve deadlines under small scheduling jitter; skip missed periods
        // instead of emitting a burst of catch-up updates after a long stall.
        nextDeadline = FrameSynchronized || nextDeadline == 0 || now - nextDeadline >= period
            ? now + period : nextDeadline + period;
    }
}

/// <summary>Stopwatch time domain with high-resolution Windows waitable timers.</summary>
internal sealed class MonotonicFrameClock : IFrameClock, IDisposable
{
    private readonly SafeWaitHandle? timer;
    public long Frequency => Stopwatch.Frequency;
    public long Timestamp => Stopwatch.GetTimestamp();

    public MonotonicFrameClock()
    {
        if (!OperatingSystem.IsWindows()) return;
        // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS.
        timer = CreateWaitableTimerEx(0, null, 2, 0x1F0003);
        if (timer.IsInvalid)
        {
            timer.Dispose();
            timer = null; // Older Windows versions can still use software waits.
        }
    }

    public void WaitUntil(long deadline)
    {
        long remaining;
        while ((remaining = deadline - Timestamp) > 0)
        {
            if (timer is not null)
            {
                long due = -Math.Max(1, (long)Math.Ceiling(remaining * 10_000_000.0 / Frequency));
                if (SetWaitableTimer(timer, in due, 0, 0, 0, false) && WaitForSingleObject(timer, 1000) == 0)
                    continue;
            }
            Thread.Sleep(Math.Max(1, (int)Math.Ceiling(remaining * 1000.0 / Frequency)));
        }
    }

    public void Dispose() => timer?.Dispose();

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, in long dueTime, int period, nint completion, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
