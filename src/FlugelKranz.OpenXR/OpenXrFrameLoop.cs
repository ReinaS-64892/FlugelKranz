using Evergine.Bindings.OpenXR;
using static Evergine.Bindings.OpenXR.OpenXRNative;

namespace FlugelKranz.OpenXR;

internal interface IOpenXrFrameApi
{
    XrFrameState Wait();
    void Begin();
    void End(long displayTime);
}

/// <summary>Pairs every begun frame with an empty submission, including failed input updates.</summary>
internal sealed class OpenXrFrameLoop(IOpenXrFrameApi api, IOpenXrClock clock)
{
    private bool active;
    private long previousDisplayTime;
    private long previousInputTime;
    public long DisplayTime { get; private set; }
    public long DisplayPeriod { get; private set; }
    public float ElapsedSeconds { get; private set; }

    public void Begin()
    {
        if (active) throw new InvalidOperationException("前の OpenXR フレームが終了していません。");
        var frame = api.Wait();
        if (frame.predictedDisplayTime <= 0 || frame.predictedDisplayPeriod <= 0
            || (previousDisplayTime > 0 && frame.predictedDisplayTime <= previousDisplayTime))
            throw new InvalidOperationException("OpenXR が不正なフレーム時刻・周期を返しました。");
        api.Begin();
        active = true;
        DisplayTime = frame.predictedDisplayTime;
        DisplayPeriod = frame.predictedDisplayPeriod;
        previousDisplayTime = DisplayTime;
    }

    public long CaptureInputTime()
    {
        if (!active) throw new InvalidOperationException("OpenXR フレームが開始していません。");
        long time = clock.Now;
        if (time <= 0 || (previousInputTime > 0 && time < previousInputTime))
            throw new InvalidOperationException("OpenXR の入力取得時刻が不正です。");
        ElapsedSeconds = previousInputTime == 0 ? 0 : (time - previousInputTime) / 1_000_000_000f;
        previousInputTime = time;
        return time;
    }

    public void WaitForApply(CancellationToken cancellationToken)
    {
        if (!active) throw new InvalidOperationException("OpenXR フレームが開始していません。");
        // Intentionally use this input in a later frame. The predicted display
        // phase schedules writes only; it is never a pose sampling timestamp.
        clock.WaitUntil(checked(DisplayTime + DisplayPeriod / 2), cancellationToken);
    }

    public void End()
    {
        if (!active) return;
        active = false;
        api.End(DisplayTime);
    }
}

internal sealed unsafe class NativeOpenXrFrameApi(XrSession session, XrEnvironmentBlendMode blendMode) : IOpenXrFrameApi
{
    public XrFrameState Wait()
    {
        var info = new XrFrameWaitInfo { type = XrStructureType.XR_TYPE_FRAME_WAIT_INFO };
        var frame = new XrFrameState { type = XrStructureType.XR_TYPE_FRAME_STATE };
        OpenXrInput.Check(xrWaitFrame(session, &info, &frame), "OpenXR フレームの待機");
        return frame;
    }

    public void Begin()
    {
        var info = new XrFrameBeginInfo { type = XrStructureType.XR_TYPE_FRAME_BEGIN_INFO };
        OpenXrInput.Check(xrBeginFrame(session, &info), "OpenXR フレームの開始");
    }

    public void End(long displayTime)
    {
        var info = new XrFrameEndInfo
        {
            type = XrStructureType.XR_TYPE_FRAME_END_INFO,
            displayTime = displayTime, environmentBlendMode = blendMode,
            layerCount = 0, layers = null
        };
        OpenXrInput.Check(xrEndFrame(session, &info), "空の OpenXR フレームの送信");
    }
}
