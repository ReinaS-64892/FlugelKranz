using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FlugelKranz.ViewModels;
using FlugelKranz.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(FlugelKranz.Tests.TestAppBuilder))]

namespace FlugelKranz.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia().UseHarfBuzz().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class TestApp : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = ThemeVariant.Dark; }
}

public class MainViewTests
{
    [AvaloniaFact]
    public async Task StartsOffAndCompiledBindingsUpdateButtonAndStatus()
    {
        await using var vm = new MainViewModel("/nonexistent/flugelkranz-test.so");
        var window = new Window { Width = 540, Height = 600, Content = new MainView(vm) };
        window.Show();
        try
        {
            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            var toggle = Assert.Single(buttons, b => Equals(b.Content, "オンにする"));
            var reset = Assert.Single(buttons, b => Equals(b.Content, "接続時の位置・姿勢に戻す"));
            Assert.False(reset.IsEnabled);
            Assert.Same(vm.ToggleCommand, toggle.Command);
            Assert.Same(vm.ResetCommand, reset.Command);
            vm.IsEnabled = true;
            vm.Status = "テスト中";
            Assert.Equal("オフにする", toggle.Content);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "テスト中");
            vm.IsEnabled = false;
            vm.Status = "オフ — オンにするとランタイムへ接続します。";
            if (Environment.GetEnvironmentVariable("FLUGELKRANZ_TEST_SCREENSHOT") is { Length: > 0 } path)
            {
                window.UpdateLayout();
                using var bitmap = new RenderTargetBitmap(new PixelSize(540, 600), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MissingRuntimeReturnsToggleToOffAndDisplaysError()
    {
        await using var vm = new MainViewModel("/nonexistent/flugelkranz-test.so");
        var window = new Window { Width = 540, Height = 600, Content = new MainView(vm) };
        window.Show();
        try
        {
            vm.ToggleCommand.Execute(null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (vm.IsEnabled) await Task.Delay(10, timeout.Token);
            Assert.False(vm.IsConnected);
            Assert.Contains("flugelkranz-test.so", vm.Status);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "オンにする"));
        }
        finally { window.Close(); }
    }
}
