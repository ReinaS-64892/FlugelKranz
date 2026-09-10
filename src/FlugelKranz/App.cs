using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
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
            ["FlugelKranzPanelBrush"] = new SolidColorBrush(Color.Parse("#F4F6FA"))
        };
        resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
        {
            ["FlugelKranzSurfaceBrush"] = new SolidColorBrush(Color.Parse("#10151C")),
            ["FlugelKranzPanelBrush"] = new SolidColorBrush(Color.Parse("#18232E"))
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
        Styles.Add(new Style(x => x.OfType<Button>().Class("flight-toggle").Class("flight-toggle-enabled"))
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, new DynamicResourceExtension("CheckBoxCheckBackgroundFillChecked")),
                new Setter(Button.ForegroundProperty, new DynamicResourceExtension("CheckBoxCheckGlyphForegroundChecked"))
            }
        });
        Styles.Add(new Style(x => x.OfType<Button>()
            .Class("flight-toggle")
            .Class("flight-toggle-enabled")
            .Class(":pointerover"))
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, new DynamicResourceExtension("CheckBoxCheckBackgroundFillCheckedPointerOver")),
                new Setter(Button.ForegroundProperty, new DynamicResourceExtension("CheckBoxCheckGlyphForegroundCheckedPointerOver"))
            }
        });
        Styles.Add(new Style(x => x.OfType<Button>()
            .Class("flight-toggle")
            .Class("flight-toggle-enabled")
            .Class(":pointerover")
            .Template()
            .OfType<ContentPresenter>()
            .Name("PART_ContentPresenter"))
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, new DynamicResourceExtension("CheckBoxCheckBackgroundFillCheckedPointerOver")),
                new Setter(ContentPresenter.ForegroundProperty, new DynamicResourceExtension("CheckBoxCheckGlyphForegroundCheckedPointerOver"))
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
                Title = "FlugelKranz", Width = 1100, Height = 650, MinWidth = 420, MinHeight = 520,
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
