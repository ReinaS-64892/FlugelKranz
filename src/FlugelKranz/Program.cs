using System.CommandLine;
using Avalonia;
using FlugelKranz.OpenXR;

namespace FlugelKranz;

internal static class Program
{
    private const string DefaultMonadoLibraryPath = "/usr/lib/wivrn/libmonado_wivrn.so";
    public static string MonadoLibraryPath { get; private set; } = DefaultMonadoLibraryPath;

    [STAThread]
    public static int Main(string[] args)
    {
        var libraryOption = new Option<string>("--lib-monado", "LibMonadoPath", "lib")
        {
            Description = "libmonado.so / libmonado_wivrn.so のパス",
            HelpName = "PATH",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => DefaultMonadoLibraryPath
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
        var root = new RootCommand("FlugelKranz — Space Drag / Space Turn。Linux Wayland セッションで起動してください。");
        root.Options.Add(libraryOption);
        root.Options.Add(diagnoseOption);
        root.SetAction(result =>
        {
            MonadoLibraryPath = result.GetValue(libraryOption)!;
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
                foreach (var origin in runtime.TrackingOrigins)
                    Console.WriteLine($"原点 {origin.Index}: 接続時={origin.Original} 現在={origin.Current}");
                for (int i = 0; i < 200; i++)
                {
                    var frame = runtime.ReadPhysical();
                    if (frame.HeadTracked && frame.Left.IsTracked && frame.Right.IsTracked)
                    { Console.WriteLine("Monado / OpenXR 接続、固定 STAGE、HMD・両手の入力を確認しました。空間は変更していません。"); return 0; }
                    Thread.Sleep(10);
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
