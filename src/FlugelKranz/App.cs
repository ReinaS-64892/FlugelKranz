using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using FlugelKranz.ViewModels;
using FlugelKranz.Views;

namespace FlugelKranz;

public class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        var resources = new ResourceDictionary();
        resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
        {
            ["FlugelKranzSurfaceBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF")),
            ["FlugelKranzPanelBrush"] = new SolidColorBrush(Color.Parse("#F4F6FA")),
            ["FlugelKranzPrimaryBrush"] = new SolidColorBrush(Color.Parse("#D7E7FF")),
            ["FlugelKranzSecondaryBrush"] = new SolidColorBrush(Color.Parse("#E6E6E6")),
            ["FlugelKranzForegroundBrush"] = new SolidColorBrush(Color.Parse("#17202A")),
            ["FlugelKranzBorderBrush"] = new SolidColorBrush(Color.Parse("#4A6A95"))
        };
        resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
        {
            ["FlugelKranzSurfaceBrush"] = new SolidColorBrush(Color.Parse("#10151C")),
            ["FlugelKranzPanelBrush"] = new SolidColorBrush(Color.Parse("#18232E")),
            ["FlugelKranzPrimaryBrush"] = new SolidColorBrush(Color.Parse("#29486B")),
            ["FlugelKranzSecondaryBrush"] = new SolidColorBrush(Color.Parse("#2B3440")),
            ["FlugelKranzForegroundBrush"] = new SolidColorBrush(Color.Parse("#F3F6FA")),
            ["FlugelKranzBorderBrush"] = new SolidColorBrush(Color.Parse("#8FB9EE"))
        };
        Resources = resources;
        Styles.Add(new Style(x => x.OfType<Panel>().Class("app-surface"))
        {
            Setters = { new Setter(Panel.BackgroundProperty, new DynamicResourceExtension("FlugelKranzSurfaceBrush")) }
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("app-surface"))
        {
            Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("FlugelKranzSurfaceBrush")) }
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("settings-surface"))
        {
            Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("FlugelKranzPanelBrush")) }
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class("primary-action"))
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, new DynamicResourceExtension("FlugelKranzPrimaryBrush")),
                new Setter(Button.ForegroundProperty, new DynamicResourceExtension("FlugelKranzForegroundBrush")),
                new Setter(Button.BorderBrushProperty, new DynamicResourceExtension("FlugelKranzBorderBrush"))
            }
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class("settings-action"))
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, new DynamicResourceExtension("FlugelKranzSecondaryBrush")),
                new Setter(Button.ForegroundProperty, new DynamicResourceExtension("FlugelKranzForegroundBrush")),
                new Setter(Button.BorderBrushProperty, new DynamicResourceExtension("FlugelKranzBorderBrush"))
            }
        });
        RequestedThemeVariant = ThemeVariant.Light;
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
