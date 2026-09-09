using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Declarative;
using Avalonia.Media;
using FlugelKranz.ViewModels;

namespace FlugelKranz.Views;

public sealed class MainView(MainViewModel vm) : ViewBase<MainViewModel>(vm)
{
    protected override object Build(MainViewModel model) =>
        new ScrollViewer().Content(
            new StackPanel().Margin(32).Spacing(20).Children(
                new TextBlock().Text("FlugelKranz").FontSize(32).FontWeight(FontWeight.SemiBold),
                new TextBlock().Text("自由飛行の、その先へ。"),
                new Button()
                    .HorizontalAlignment(HorizontalAlignment.Stretch)
                    .HorizontalContentAlignment(HorizontalAlignment.Center)
                    .MinHeight(60)
                    .FontSize(22)
                    .Content(model, x => x.ToggleLabel)
                    .Command(model, x => x.ToggleCommand),
                new TextBlock().Text(model, x => x.Status).TextWrapping(TextWrapping.Wrap),
                new Border().Padding(18).CornerRadius(new CornerRadius(12))
                    .Background(new SolidColorBrush(Color.Parse("#202D3A")))
                    .Child(new StackPanel().Spacing(14).Children(
                        new TextBlock().Text("SPACE DRAG / 左手").FontWeight(FontWeight.SemiBold),
                        new TextBlock().Text(model, x => x.LeftStatus).TextWrapping(TextWrapping.Wrap),
                        new TextBlock().Text("SPACE TURN / 右手").FontWeight(FontWeight.SemiBold),
                        new TextBlock().Text(model, x => x.RightStatus).TextWrapping(TextWrapping.Wrap)
                    )),
                new TextBlock()
                    .Text("オフでは位置・姿勢を保持します。両手を握ると、左手を支点に移動と回転を組み合わせます。")
                    .TextWrapping(TextWrapping.Wrap),
                new Button().Content("接続時の位置・姿勢に戻す")
                    .IsEnabled(model, x => x.IsConnected)
                    .Command(model, x => x.ResetCommand),
                new TextBlock().Text("通常終了時にも接続時の状態へ戻します。")
                    .FontSize(12).TextWrapping(TextWrapping.Wrap)
            ));
}
