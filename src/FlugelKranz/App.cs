using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using FlugelKranz.ViewModels;
using FlugelKranz.Views;

namespace FlugelKranz;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel(Program.MonadoLibraryPath);
            var window = new Window
            {
                Title = "FlugelKranz", Width = 540, Height = 600, MinWidth = 420, MinHeight = 520,
                Content = new MainView(vm)
            };
            bool canClose = false, closing = false;
            window.Closing += async (_, e) =>
            {
                if (canClose) return;
                e.Cancel = true;
                if (closing) return;
                closing = true;
                await vm.DisposeAsync();
                canClose = true;
                window.Close();
            };
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
