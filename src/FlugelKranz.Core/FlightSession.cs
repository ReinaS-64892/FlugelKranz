using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Coordinates input gestures, mode selection and status reporting for one runtime connection.</summary>
internal sealed class FlightSession
{
    private readonly IFlightRuntime runtime;
    private readonly IProgress<FlightStatus> progress;
    private readonly MovementSession movement;
    private readonly FlightInputGestures gestures = new();
    private FlightMode? inputModeOverride;
    private RigidPose? previousReferenceSpaceOffset;
    private int tick;

    public FlightSession(IFlightRuntime runtime, IProgress<FlightStatus> progress, FlugelKranzSettings settings)
    {
        this.runtime = runtime;
        this.progress = progress;
        movement = new(MovementModes.Create(settings.Mode, runtime.CurrentOffset), runtime.CurrentOffset, runtime.OriginalOffset);
    }

    public FlightMode Mode => MovementModes.Selection(movement.SelectedMode);
    public void Release() => movement.Release(runtime.CurrentOffset);

    public void Reset()
    {
        runtime.Restore();
        movement.Reset(runtime.CurrentOffset);
    }

    public void Update(InputFrame frame, float elapsedSeconds, FlugelKranzSettings settings, bool enabled)
    {
        if (inputModeOverride == settings.Mode)
            inputModeOverride = null;
        Select(inputModeOverride ?? settings.Mode, enabled);
        var gesture = gestures.Update(frame, elapsedSeconds, movement.SelectedMode.CanReset(frame));
        if (gesture.ToggleMode)
        {
            movement.Select(movement.SelectedMode.CreateAlternate(runtime.CurrentOffset), runtime.CurrentOffset);
            inputModeOverride = Mode;
            if (enabled)
                SendModeChangeHaptic();
            progress.Report(new(enabled, true, ModeChangedMessage(), Mode: Mode));
        }
        else if (enabled && gesture.ResetSpace && movement.StartSpaceReset(frame, settings))
            progress.Report(new(enabled, true, movement.SelectedMode.ResetMessage, Mode: Mode));

        if (enabled)
        {
            var offset = movement.Update(frame, elapsedSeconds, settings);
            // Exact equality avoids accumulating unapplied substeps as feedback.
            if (offset != runtime.CurrentOffset)
                runtime.Apply(offset);
        }

        if (tick++ % 10 == 0)
            ReportStatus(frame, enabled);
    }

    private void Select(FlightMode selection, bool enabled)
    {
        if (MovementModes.Matches(movement.SelectedMode, selection))
            return;
        movement.Select(MovementModes.Create(selection, runtime.CurrentOffset), runtime.CurrentOffset);
        if (enabled)
            SendModeChangeHaptic();
    }

    private string ModeChangedMessage() => Mode == FlightMode.InfiniteWalking
        ? "無限歩行モードへ切り替えました。操作入力を離してから使用してください。"
        : "自由飛行モードへ切り替えました。操作入力を離してから使用してください。";

    private void SendModeChangeHaptic()
    {
        if (runtime is not IHapticFeedback haptics)
            return;
        try { haptics.SendHapticPulse(0.08f, 0, 0.35f); }
        catch { }
    }

    private void ReportStatus(InputFrame frame, bool enabled)
    {
        string message = !enabled ? "オフ — 現在の位置・姿勢を保持しています。"
            : movement.ActiveMode.StatusMessage ?? (!frame.HeadTracked ? "HMD のトラッキングを待っています。"
            : !frame.Left.IsTracked || !frame.Right.IsTracked ? "コントローラーの姿勢・操作入力を待っています。"
            : "オン — 操作入力を一度離してから使用してください。");
        var referenceSpaceOffset = (runtime as IReferenceSpaceOffsetProvider)?.ReferenceSpaceOffset;
        var recentMovement = referenceSpaceOffset is { } current && previousReferenceSpaceOffset is { } previous
            ? current.Position - previous.Position
            : (Vector3?)null;
        previousReferenceSpaceOffset = referenceSpaceOffset;
        progress.Report(new(enabled, true, message, movement.ActiveMode.IsDragging, movement.ActiveMode.IsTurning,
            Mode, TrackpadForce(frame.Left), TrackpadForce(frame.Right), referenceSpaceOffset, recentMovement));
    }

    private static float? TrackpadForce(HandSample hand) => hand.TrackpadForceActive && float.IsFinite(hand.TrackpadForce)
        ? Math.Clamp(hand.TrackpadForce, 0, 1) : null;
}
