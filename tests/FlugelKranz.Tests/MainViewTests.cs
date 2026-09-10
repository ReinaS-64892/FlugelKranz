using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Media;
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

public sealed class TestApp : FlugelKranz.App
{
}

public class MainViewTests
{
    [AvaloniaFact]
    public async Task StartsOffAndCompiledBindingsUpdateButtonAndStatus()
    {
        string settingsPath = TemporarySettingsPath();
        await using var vm = new MainViewModel("/nonexistent/flugelkranz-test.so", settingsPath);
        Assert.Equal(40, vm.DragCutoffCentimetresPerSecond);
        Assert.Equal(45, vm.TurnCutoffDegreesPerSecond);
        Assert.Equal(0.5, vm.TurnAccelerationMultiplier);
        Assert.Equal(2, vm.InertiaDecelerationPerSecond);
        Assert.Equal(0.2, vm.DragDecelerationExemptionDurationRatio);
        Assert.Equal(0.05, vm.TurnDecelerationExemptionDurationRatio);
        Assert.Equal(0.9, vm.DecelerationExemptionStrength);
        Assert.Equal(0.01, vm.DragSmoothSeconds);
        Assert.Equal(0.05, vm.TurnSmoothSeconds);
        Assert.Equal(1, vm.VectorRotationMultiplier);
        Assert.Equal(0.2, vm.BrakeRampSeconds);
        var window = new Window { Width = 540, Height = 600, Content = new MainView(vm) };
        window.Show();
        try
        {
            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            var toggle = Assert.Single(buttons, b => Equals(b.Content, "OFF"));
            Assert.True(toggle.Bounds.Width > 0);
            Assert.True(toggle.Bounds.Height > 0);
            var reset = Assert.Single(buttons, b => Equals(b.Content, "接続時の位置・姿勢に戻す"));
            Assert.False(reset.IsEnabled);
            Assert.Same(vm.ToggleCommand, toggle.Command);
            Assert.Same(vm.ResetCommand, reset.Command);
            var sliders = window.GetVisualDescendants().OfType<Slider>().ToArray();
            Assert.Equal(17, sliders.Length);
            var scrollViewer = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.False(scrollViewer.AllowAutoHide);
            Assert.Equal(12, scrollViewer.Padding.Right);
            var stepMode = window.GetVisualDescendants().OfType<CheckBox>().First();
            Assert.False(stepMode.IsChecked);
            stepMode.IsChecked = true;
            Assert.True(vm.StepMode);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "2.00 /秒");
            vm.IsEnabled = true;
            vm.Status = "テスト中";
            Assert.Equal("ON", toggle.Content);
            Assert.Contains("flight-toggle-enabled", toggle.Classes);
            var enabledBrush = Assert.IsType<SolidColorBrush>(toggle.Background);
            Assert.Equal(Color.Parse("#2E7D32"), enabledBrush.Color);
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
        await using var vm = new MainViewModel("/nonexistent/flugelkranz-test.so", TemporarySettingsPath());
        var window = new Window { Width = 540, Height = 600, Content = new MainView(vm) };
        window.Show();
        try
        {
            vm.ToggleCommand.Execute(null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (vm.IsEnabled) await Task.Delay(10, timeout.Token);
            Assert.False(vm.IsConnected);
            Assert.Contains("flugelkranz-test.so", vm.Status);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "OFF"));
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task SavesSettingsAndRestoresThemOnNextLaunch()
    {
        string settingsPath = TemporarySettingsPath();
        try
        {
            await using (var first = new MainViewModel("/nonexistent/flugelkranz-test.so", settingsPath))
            {
                first.StepMode = true;
                first.DragCutoffCentimetresPerSecond = 12.5;
                first.TurnAccelerationMultiplier = 1.25;
                first.BrakeRampSeconds = 0.75;
            }

            await using var second = new MainViewModel("/nonexistent/flugelkranz-test.so", settingsPath);
            Assert.True(second.StepMode);
            Assert.Equal(12.5, second.DragCutoffCentimetresPerSecond);
            Assert.Equal(1.25, second.TurnAccelerationMultiplier);
            Assert.Equal(0.75, second.BrakeRampSeconds);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public async Task IndividualResetCommandRestoresOnlyItsSetting()
    {
        await using var vm = new MainViewModel("/nonexistent/flugelkranz-test.so", TemporarySettingsPath());
        vm.DragCutoffCentimetresPerSecond = 12;
        vm.TurnCutoffDegreesPerSecond = 123;

        vm.ResetDragCutoffCommand.Execute(null);

        Assert.Equal(40, vm.DragCutoffCentimetresPerSecond);
        Assert.Equal(123, vm.TurnCutoffDegreesPerSecond);
    }

    [AvaloniaFact]
    public async Task SettingsPanelExpandsAndCollapsesFromSettingsCommand()
    {
        await using var vm = new MainViewModel("/nonexistent/flugelkranz-test.so", TemporarySettingsPath());
        Assert.Equal(0, vm.SettingsPanelWidth);

        vm.SetSettingsPanelTargetWidth(1000 * 0.8);
        await vm.ToggleSettingsCommand.ExecuteAsync(null);
        Assert.True(vm.IsSettingsOpen);
        Assert.Equal(800, vm.SettingsPanelWidth);

        vm.SetSettingsPanelTargetWidth(600);
        Assert.Equal(600, vm.SettingsPanelWidth);

        await vm.ToggleSettingsCommand.ExecuteAsync(null);
        Assert.False(vm.IsSettingsOpen);
        Assert.Equal(0, vm.SettingsPanelWidth);
    }

    private static string TemporarySettingsPath() =>
        Path.Combine(Path.GetTempPath(), $"flugelkranz-test-{Guid.NewGuid():N}.json");
}
