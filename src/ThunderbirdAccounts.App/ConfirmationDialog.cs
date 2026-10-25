using ThunderbirdAccounts.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ThunderbirdAccounts.App;

internal sealed class ConfirmationDialog : Window
{
    internal ConfirmationDialog(Window owner, string text)
    {
        Style = (Style)FindResource(typeof(Window));
        Owner = owner; Title = TextCatalog.Get("ReviewRestore"); Width = 570; MaxHeight = 740;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        var panel = new StackPanel { Margin = new Thickness(30) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = TextCatalog.Get("RestoreYourAccounts"), FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), FontSize = 13 };
        panel.Children.Add(new ScrollViewer { Content = body, MaxHeight = 450, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 24) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var accept = new Button { Content = TextCatalog.Get("RestoreToNewProfile"), IsDefault = true, Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 10, 0) };
        accept.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(accept); buttons.Children.Add(new Button { Content = TextCatalog.Get("Cancel"), IsCancel = true }); panel.Children.Add(buttons);
    }
    public static bool Confirm(Window owner, string text) => new ConfirmationDialog(owner, text).ShowDialog() == true;
}
