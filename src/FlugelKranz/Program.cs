using System.CommandLine;
using Avalonia;
using FlugelKranz.OpenXR;

namespace FlugelKranz;

internal static class Program
{
    public static string MonadoLibraryPath { get; private set; } = MonadoRuntimeLocator.Resolve();
    public static Func<string> MonadoLibraryPathResolver { get; private set; } = static () => MonadoRuntimeLocator.Resolve();

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
        var diagnoseOption = new Option<bool>("--diagnose")
        {
            Description = "UI を開かず接続・入力を確認（空間の書き込みなし）"
        };
        var root = new RootCommand("FlugelKranz — 自由飛行 / 無限歩行の Space Drag・Space Turn。Linux Wayland セッションで起動してください。");
        root.Options.Add(libraryOption);
        root.Options.Add(diagnoseOption);
        root.SetAction(result =>
        {
            MonadoLibraryPath = result.GetValue(libraryOption)!;
            bool explicitLibraryPath = args.Any(static argument =>
                argument is "lib" or "LibMonadoPath" or "--lib-monado" ||
                argument.StartsWith("--lib-monado=", StringComparison.Ordinal));
            MonadoLibraryPathResolver = explicitLibraryPath
                ? static () => MonadoLibraryPath
                : static () => MonadoRuntimeLocator.Resolve();
            return Run(result.GetValue(diagnoseOption));
        });

        // Keep Avalonia startup on the entry thread by invoking the action synchronously.
        return root.Parse(args).Invoke();
    }

    private static int Run(bool diagnose)
    {
        try
        {
            if (diagnose)
            {
                using var runtime = new MonadoFlightRuntime(MonadoLibraryPath);
                for (int i = 0; i < 200; i++)
                {
                    bool frameStarted = runtime.WaitFrame();
                    try
                    {
                        var frame = runtime.ReadPhysical();
                        if (frameStarted)
                        {
                            if (runtime.WaitForMidFrameUpdate(CancellationToken.None))
                                frame = runtime.ReadPhysical();
                            runtime.CommitFrame(CancellationToken.None);
                        }
                        if (frameStarted && frame.HeadTracked && frame.Left.IsTracked && frame.Right.IsTracked)
                        {
                            Console.WriteLine("Monado / OpenXR 接続、固定 STAGE、HMD・両手の入力を確認しました。空間は変更していません。");
                            Console.WriteLine($"フレーム待機: xrWaitFrame、更新周期: {runtime.FramePeriodNanoseconds / 1_000_000.0:F3} ms ({1_000_000_000.0 / runtime.FramePeriodNanoseconds:F2} Hz、初回はランタイム値、以降は復帰間隔の実測)。描画レイヤー: 0。");
                            Console.WriteLine("入力取得・内部演算: フレーム開始時と半周期後（2倍レート目標）。Monado 反映: 半周期後の演算後、1フレームに1回。");
                            Console.WriteLine($"トラッカー空間: {(runtime.TrackerSpacesSupported ? "対応" : "非対応")}、有効なトラッカー: {frame.Trackers?.Count ?? 0} 台。");
                            Console.WriteLine($"つま先逆補正 API: {(runtime.TrackerRotationOffsetsSupported ? "対応" : "非対応（つま先逆補正は無効）")}");
                            return 0;
                        }
                    }
                    finally
                    {
                        if (frameStarted) runtime.EndFrame();
                    }
                    if (!frameStarted) Thread.Sleep(10);
                }
                Console.Error.WriteLine("接続できましたが、HMD・両手の有効な入力を確認できませんでした。");
                return 1;
            }
            if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                throw new InvalidOperationException("Linux の Wayland セッションが必要です（WAYLAND_DISPLAY が未設定）。");
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHarfBuzz().UseWayland().LogToTrace();
}
