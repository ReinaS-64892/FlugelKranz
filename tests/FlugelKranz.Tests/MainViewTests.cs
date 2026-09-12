using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Presenters;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FlugelKranz.Core;
using FlugelKranz.ViewModels;
using FlugelKranz.Views;
using System.Text.Json;
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
        Assert.Equal(0.4, vm.TurnAccelerationMultiplier);
        Assert.Equal(2, vm.ZAccelerationMultiplier);
        Assert.True(vm.InertiaAccelerationBoostEnabled);
        Assert.Equal(4, vm.InertiaAccelerationBoostMaximumMultiplier);
        Assert.Equal(2, vm.InertiaDecelerationPerSecond);
        Assert.Equal(0.2, vm.DragDecelerationExemptionDurationRatio);
        Assert.Equal(0.15, vm.TurnDecelerationExemptionDurationRatio);
        Assert.Equal(0.9, vm.DecelerationExemptionStrength);
        Assert.Equal(0.01, vm.DragSmoothSeconds);
        Assert.Equal(0.05, vm.TurnSmoothSeconds);
        Assert.Equal(1, vm.VectorRotationMultiplier);
        Assert.Equal(0.4, vm.BrakeRampSeconds);
        Assert.Equal(FlightMode.InfiniteWalking, vm.Mode);
        Assert.Equal(0, vm.InfiniteDragSmoothSeconds);
        Assert.Equal(0, vm.InfiniteTurnSmoothSeconds);
        Assert.Equal(0, vm.InfiniteTurnHeadSmoothSeconds);
        Assert.Equal(1, vm.InfiniteWalkingBoostMultiplier);
        Assert.Equal(0.3, vm.ValveIndexPositionDeadZone);
        Assert.Equal(0.2, vm.ValveIndexForceThreshold);
        var window = new Window { Width = 540, Height = 600, Content = new MainView(vm) };
        window.Show();
        try
        {
            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            var toggle = Assert.Single(buttons, b => Equals(b.Content, "OFF"));
            var mode = Assert.Single(buttons, b => Equals(b.Content, "I"));
            Assert.True(toggle.Bounds.Width > 0);
            Assert.True(toggle.Bounds.Height > 0);
            var settingsButton = Assert.Single(buttons, b => Equals(b.Content, "⚙"));
            var reset = Assert.Single(buttons, b => ReferenceEquals(vm.ResetCommand, b.Command));
            Assert.Equal("↻", reset.Content);
            Assert.Equal(64, reset.Bounds.Width);
            Assert.Equal(64, reset.Bounds.Height);
            Assert.False(reset.IsEnabled);
            Assert.Same(vm.ToggleCommand, toggle.Command);
            Assert.Same(vm.ToggleModeCommand, mode.Command);
            Assert.Same(vm.ResetCommand, reset.Command);
            Assert.Equal(64, mode.Bounds.Width);
            Assert.Equal(64, mode.Bounds.Height);
            Assert.Equal(new CornerRadius(32), mode.CornerRadius);
            var toggleLayoutCenter = toggle.TranslatePoint(
                new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2),
                window);
            var settingsLayoutCenter = settingsButton.TranslatePoint(
                new Point(settingsButton.Bounds.Width / 2, settingsButton.Bounds.Height / 2),
                window);
            var resetLayoutCenter = reset.TranslatePoint(
                new Point(reset.Bounds.Width / 2, reset.Bounds.Height / 2),
                window);
            Assert.True(toggleLayoutCenter.HasValue);
            Assert.True(settingsLayoutCenter.HasValue);
            Assert.True(resetLayoutCenter.HasValue);
            var modeLayoutCenter = mode.TranslatePoint(
                new Point(mode.Bounds.Width / 2, mode.Bounds.Height / 2),
                window);
            Assert.True(modeLayoutCenter.HasValue);
            Assert.True(modeLayoutCenter.Value.X < toggleLayoutCenter.Value.X);
            Assert.True(settingsLayoutCenter.Value.Y < toggleLayoutCenter.Value.Y);
            Assert.True(toggleLayoutCenter.Value.Y < resetLayoutCenter.Value.Y);
            var sliders = window.GetVisualDescendants().OfType<Slider>().ToArray();
            Assert.Equal(20, sliders.Length);
            var scrollViewer = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.False(scrollViewer.AllowAutoHide);
            Assert.Equal(12, scrollViewer.Padding.Right);
            var turnOrigin = window.GetVisualDescendants().OfType<CheckBox>().First();
            Assert.False(turnOrigin.IsChecked);
            turnOrigin.IsChecked = true;
            Assert.True(vm.UseHeadTurnOrigin);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "2.00 /秒");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "倍率: 1.00");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "補正値: 1.00 倍");
            vm.IsValveIndexDetected = true;
            vm.LeftValveIndexForce = 0.42;
            vm.RightValveIndexForce = 0.87;
            var forceStatus = Assert.Single(
                window.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Text == "Valve Index force — 左: 0.42 / 右: 0.87");
            Assert.True(forceStatus.IsVisible);
            vm.ToggleModeCommand.Execute(null);
            Assert.Equal(FlightMode.FreeFlight, vm.Mode);
            Assert.Equal("F", mode.Content);
            vm.IsEnabled = true;
            vm.Status = "テスト中";
            Assert.Equal("ON", toggle.Content);
            Assert.Contains("flight-toggle-enabled", toggle.Classes);
            Assert.True(Application.Current!.TryGetResource(
                "CheckBoxCheckBackgroundFillChecked",
                ThemeVariant.Light,
                out var checkboxBrush));
            Assert.Same(checkboxBrush, toggle.Background);
            var enabledBrush = Assert.IsAssignableFrom<ISolidColorBrush>(toggle.Background);
            Assert.NotEqual(Colors.Transparent, enabledBrush.Color);
            Assert.True(Application.Current.TryGetResource(
                "CheckBoxCheckGlyphForegroundChecked",
                ThemeVariant.Light,
                out var checkboxForeground));
            Assert.Same(checkboxForeground, toggle.Foreground);
            var toggleCenter = toggle.TranslatePoint(
                new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2),
                window);
            Assert.True(toggleCenter.HasValue);
            window.MouseMove(toggleCenter.Value);
            Assert.True(toggle.IsPointerOver);
            Assert.True(Application.Current.TryGetResource(
                "CheckBoxCheckBackgroundFillCheckedPointerOver",
                ThemeVariant.Light,
                out var checkboxHoverBrush));
            Assert.Same(checkboxHoverBrush, toggle.Background);
            Assert.True(Application.Current.TryGetResource(
                "CheckBoxCheckGlyphForegroundCheckedPointerOver",
                ThemeVariant.Light,
                out var checkboxHoverForeground));
            Assert.Same(checkboxHoverForeground, toggle.Foreground);
            var presenter = Assert.Single(
                toggle.GetVisualDescendants().OfType<ContentPresenter>(),
                p => p.Name == "PART_ContentPresenter");
            Assert.Same(checkboxHoverBrush, presenter.Background);
            Assert.Same(checkboxHoverForeground, presenter.Foreground);
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
                first.Mode = FlugelKranz.Core.FlightMode.FreeFlight;
                first.UseHeadTurnOrigin = true;
                first.DragCutoffCentimetresPerSecond = 12.5;
                first.TurnAccelerationMultiplier = 1.25;
                first.ZAccelerationMultiplier = 4.25;
                first.InertiaAccelerationBoostEnabled = false;
                first.InertiaAccelerationBoostMaximumMultiplier = 3.25;
                first.BrakeRampSeconds = 0.75;
                first.InfiniteTurnHeadSmoothSeconds = 0.4;
                first.InfiniteWalkingBoostMultiplier = 1.6;
                first.ValveIndexPositionDeadZone = 0.44;
                first.ValveIndexForceThreshold = 0.62;
            }

            await using var second = new MainViewModel("/nonexistent/flugelkranz-test.so", settingsPath);
            Assert.Equal(FlugelKranz.Core.FlightMode.FreeFlight, second.Mode);
            Assert.True(second.UseHeadTurnOrigin);
            Assert.Equal(12.5, second.DragCutoffCentimetresPerSecond);
            Assert.Equal(1.25, second.TurnAccelerationMultiplier);
            Assert.Equal(4.25, second.ZAccelerationMultiplier);
            Assert.False(second.InertiaAccelerationBoostEnabled);
            Assert.Equal(3.25, second.InertiaAccelerationBoostMaximumMultiplier);
            Assert.Equal(0.75, second.BrakeRampSeconds);
            Assert.Equal(0.4, second.InfiniteTurnHeadSmoothSeconds);
            Assert.Equal(1.6, second.InfiniteWalkingBoostMultiplier);
            Assert.Equal(0.44, second.ValveIndexPositionDeadZone);
            Assert.Equal(0.62, second.ValveIndexForceThreshold);

            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            Assert.Equal(FlugelKranzSettings.CurrentSchemaVersion,
                document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(JsonValueKind.Object,
                document.RootElement.GetProperty("freeFlight").ValueKind);
            Assert.Equal(JsonValueKind.Object,
                document.RootElement.GetProperty("infiniteWalking").ValueKind);
            Assert.Equal(1.6,
                document.RootElement.GetProperty("infiniteWalking")
                    .GetProperty("turnMovementBoostMultiplier").GetDouble());
            Assert.Equal(JsonValueKind.Object,
                document.RootElement.GetProperty("valveIndex").ValueKind);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public async Task LegacyFlatSettingsAreReplacedByCurrentModeHierarchy()
    {
        string settingsPath = TemporarySettingsPath();
        try
        {
            File.WriteAllText(settingsPath, """
                {
                  "stepMode": true,
                  "dragAccelerationMultiplier": 4.5
                }
                """);

            await using var vm = new MainViewModel(
                "/nonexistent/flugelkranz-test.so",
                settingsPath);

            Assert.Equal(FlightMode.InfiniteWalking, vm.Mode);
            Assert.Equal(1, vm.DragAccelerationMultiplier);
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            Assert.Equal(FlugelKranzSettings.CurrentSchemaVersion,
                document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(document.RootElement.TryGetProperty("freeFlight", out _));
            Assert.True(document.RootElement.TryGetProperty("infiniteWalking", out _));
            Assert.True(document.RootElement.TryGetProperty("valveIndex", out _));
            Assert.False(document.RootElement.TryGetProperty("stepMode", out _));
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
        vm.ZAccelerationMultiplier = 4;
        vm.InertiaAccelerationBoostEnabled = false;
        vm.InertiaAccelerationBoostMaximumMultiplier = 3;
        vm.InfiniteDragSmoothSeconds = 0.6;
        vm.InfiniteTurnSmoothSeconds = 0.7;
        vm.InfiniteTurnHeadSmoothSeconds = 0.8;
        vm.InfiniteWalkingBoostMultiplier = 0.4;
        vm.ValveIndexPositionDeadZone = 0.8;
        vm.ValveIndexForceThreshold = 0.9;

        vm.ResetDragCutoffCommand.Execute(null);
        vm.ResetZAccelerationCommand.Execute(null);
        vm.ResetInertiaAccelerationBoostEnabledCommand.Execute(null);
        vm.ResetInertiaAccelerationBoostMaximumMultiplierCommand.Execute(null);
        vm.ResetInfiniteDragSmoothCommand.Execute(null);
        vm.ResetInfiniteTurnSmoothCommand.Execute(null);
        vm.ResetInfiniteTurnHeadSmoothCommand.Execute(null);
        vm.ResetInfiniteWalkingBoostCommand.Execute(null);
        vm.ResetValveIndexForceThresholdCommand.Execute(null);

        Assert.Equal(40, vm.DragCutoffCentimetresPerSecond);
        Assert.Equal(123, vm.TurnCutoffDegreesPerSecond);
        Assert.Equal(2, vm.ZAccelerationMultiplier);
        Assert.True(vm.InertiaAccelerationBoostEnabled);
        Assert.Equal(4, vm.InertiaAccelerationBoostMaximumMultiplier);
        Assert.Equal(0, vm.InfiniteDragSmoothSeconds);
        Assert.Equal(0, vm.InfiniteTurnSmoothSeconds);
        Assert.Equal(0, vm.InfiniteTurnHeadSmoothSeconds);
        Assert.Equal(1, vm.InfiniteWalkingBoostMultiplier);
        Assert.Equal(0.8, vm.ValveIndexPositionDeadZone);
        Assert.Equal(0.2, vm.ValveIndexForceThreshold);
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
