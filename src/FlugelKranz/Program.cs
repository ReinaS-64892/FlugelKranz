using System.CommandLine;
using Avalonia;
using FlugelKranz.Core;
using FlugelKranz.OpenVR;
using FlugelKranz.OpenXR;

namespace FlugelKranz;

internal static class Program
{
    public static RuntimeConfiguration Runtime { get; private set; } = new(
        RuntimeConfiguration.SelectBackend("auto", OperatingSystem.IsWindows()),
        static () => MonadoRuntimeLocator.Resolve(), new());

    [STAThread]
    public static int Main(string[] args)
    {
        var libraryOption = new Option<string>("--lib-monado", "LibMonadoPath", "lib")
        {
            Description = "libmonado.so / libmonado_wivrn.so のパス",
            HelpName = "PATH",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => MonadoRuntimeLocator.Resolve()
        };
        libraryOption.Validators.Add(result =>
        {
            var path = result.GetValueOrDefault<string>();
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith('-'))
                result.AddError("--lib-monado にはライブラリのパスを指定してください。'-' で始まるファイル名には './' を付けてください。");
        });
        var backendOption = new Option<string>("--runtime")
        {
            Description = "auto: Windows は SteamVR、Linux は Monado / WiVRn",
            DefaultValueFactory = _ => "auto"
        };
        backendOption.AcceptOnlyFromAmong("auto", "monado", "steamvr");
        var rateOption = new Option<double>("--steamvr-rate")
        {
            Description = "SteamVR 固定更新 / フレーム同期失敗時の更新レート (30〜1000 Hz)",
            DefaultValueFactory = _ => 120
        };
        rateOption.Validators.Add(result =>
        {
            double rate = result.GetValueOrDefault<double>();
            if (!double.IsFinite(rate) || rate < 30 || rate > 1000)
                result.AddError("--steamvr-rate は 30〜1000 Hz を指定してください。");
        });
        var noSyncOption = new Option<bool>("--steamvr-no-sync") { Description = "SteamVR のフレーム同期を使わず固定レートで更新" };
        var diagnoseOption = new Option<bool>("--diagnose") { Description = "UI を開かず接続・入力を確認（空間の書き込みなし）" };
        var root = new RootCommand("FlugelKranz — 自由飛行 / 無限歩行の Space Drag・Space Turn。Windows / Linux 対応。");
        root.Options.Add(libraryOption);
        root.Options.Add(backendOption);
        root.Options.Add(rateOption);
        root.Options.Add(noSyncOption);
        root.Options.Add(diagnoseOption);
        root.SetAction(result =>
        {
            string libraryPath = result.GetValue(libraryOption)!;
            bool explicitLibraryPath = args.Any(static argument =>
                argument is "lib" or "LibMonadoPath" or "--lib-monado" || argument.StartsWith("--lib-monado=", StringComparison.Ordinal));
            Runtime = new(RuntimeConfiguration.SelectBackend(result.GetValue(backendOption)!, OperatingSystem.IsWindows()),
                explicitLibraryPath ? () => libraryPath : static () => MonadoRuntimeLocator.Resolve(),
                new(result.GetValue(rateOption), !result.GetValue(noSyncOption)));
            return Run(result.GetValue(diagnoseOption));
        });
        return root.Parse(args).Invoke();
    }

    private static int Run(bool diagnose)
    {
        try
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Windows / Linux に対応します。");
            if (diagnose) return Diagnose();
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int Diagnose()
    {
        using var runtime = Runtime.Create(static () => ValveIndexInputSettings.Default);
        var pacing = runtime as IFramePacedFlightRuntime;
        for (int i = 0; i < 200; i++)
        {
            bool started = pacing?.WaitFrame() ?? false;
            try
            {
                var frame = runtime.ReadPhysical();
                if (started)
                {
                    pacing!.CommitFrame(CancellationToken.None);
                    if (pacing.WaitForMidFrameUpdate(CancellationToken.None)) frame = runtime.ReadPhysical();
                }
                if (frame.HeadTracked && frame.Left.IsTracked && frame.Right.IsTracked)
                {
                    Console.WriteLine($"{Runtime.Backend}: HMD・両手の入力を確認しました。空間は変更していません。");
                    if (runtime is MonadoFlightRuntime monado)
                    {
                        Console.WriteLine($"xrWaitFrame: {monado.FramePeriodNanoseconds / 1_000_000.0:F3} ms。描画レイヤー: 0。半周期後は入力・演算のみ。");
                        Console.WriteLine($"トラッカー空間: {monado.TrackerSpacesSupported}、つま先逆補正 API: {monado.TrackerRotationOffsetsSupported}。");
                    }
                    if (runtime is SteamVrFlightRuntime steam)
                        Console.WriteLine($"OpenVR overlay: {(steam.FrameSynchronized ? "WaitFrameSync" : "固定周期")}、実測周期 {steam.FrameElapsedSeconds * 1000:F3} ms。つま先逆補正: 非対応。");
                    Console.WriteLine($"有効なトラッカー: {frame.Trackers?.Count ?? 0} 台。");
                    return 0;
                }
            }
            finally { if (started) pacing!.EndUpdateCycle(); }
            if (!started) Thread.Sleep(10);
        }
        Console.Error.WriteLine("接続できましたが、HMD・両手の有効な入力を確認できませんでした。");
        return 1;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UseSkia().UseHarfBuzz();
        // Preserve native Wayland when available; Windows and Linux X11 use the
        // Avalonia desktop platform selected for the host.
        return (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            ? builder.UseWayland() : builder.UsePlatformDetect()).LogToTrace();
    }
}
