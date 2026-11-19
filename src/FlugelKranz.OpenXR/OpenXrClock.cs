using System.Runtime.InteropServices;
using Evergine.Bindings.OpenXR;

namespace FlugelKranz.OpenXR;

internal interface IOpenXrClock
{
    long Now { get; }
    void WaitUntil(long time, CancellationToken cancellationToken);
}

/// <summary>Uses one monotonic time domain for input sampling and absolute write deadlines.</summary>
internal sealed unsafe class OpenXrClock : IOpenXrClock
{
    private const int ClockMonotonic = 1, TimerAbsolute = 1, Interrupted = 4;
    private readonly XrInstance instance;
    private readonly delegate* unmanaged[Cdecl]<XrInstance, Timespec*, long*, XrResult> toXrTime;
    private readonly delegate* unmanaged[Cdecl]<XrInstance, long, Timespec*, XrResult> toTimespec;

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec { public long Seconds, Nanoseconds; }

    [DllImport("libc", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int clock_gettime(int clockId, out Timespec time);
    [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
    private static extern int clock_nanosleep(int clockId, int flags, in Timespec time, nint remaining);

    public OpenXrClock(XrInstance instance)
    {
        this.instance = instance;
        toXrTime = (delegate* unmanaged[Cdecl]<XrInstance, Timespec*, long*, XrResult>)
            OpenXrInput.GetFunction(instance, "xrConvertTimespecTimeToTimeKHR");
        toTimespec = (delegate* unmanaged[Cdecl]<XrInstance, long, Timespec*, XrResult>)
            OpenXrInput.GetFunction(instance, "xrConvertTimeToTimespecTimeKHR");
    }

    public long Now
    {
        get
        {
            if (clock_gettime(ClockMonotonic, out var now) != 0)
                throw new InvalidOperationException("単調増加時計を取得できません。");
            long time;
            OpenXrInput.Check(toXrTime(instance, &now, &time), "入力取得時刻の変換");
            return time;
        }
    }

    public void WaitUntil(long time, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (time <= Now) return;
        Timespec deadline;
        OpenXrInput.Check(toTimespec(instance, time, &deadline), "オフセット反映時刻の変換");
        int result;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Absolute high-resolution sleep: processing time and interrupted
            // sleeps never move the deadline. No spin wait or millisecond rounding.
            result = clock_nanosleep(ClockMonotonic, TimerAbsolute, in deadline, 0);
        } while (result == Interrupted);
        if (result != 0)
            throw new InvalidOperationException($"オフセット反映の待機に失敗しました（errno: {result}）。");
        cancellationToken.ThrowIfCancellationRequested();
    }
}
