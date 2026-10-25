using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ThunderbirdAccounts.App;

namespace ThunderbirdAccounts.UiTests;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui-preview");
        Directory.CreateDirectory(output);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new ThunderbirdAccounts.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        MainWindow? main = null;
        try
        {
            main = new MainWindow(preview: true); main.Show(); Pump(); Capture(main, "overview.png", output);
            var combo = (ComboBox)main.FindName("ProfilesBox"); combo.IsDropDownOpen = true; Pump();
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            if (popup.Child is not FrameworkElement dropdown || dropdown.ActualHeight < 30) throw new Exception("Profile dropdown did not render.");
            Capture(dropdown, "profiles-dropdown.png", output); combo.IsDropDownOpen = false;
            TestPassword(main, true, output); TestPassword(main, false, output);
            var confirm = new ConfirmationDialog(main, "Återställ 10 konton från Min Thunderbird-profil?\n\nEn ny profil skapas. Befintliga profiler bevaras.\n\nPrimary Password behålls. OAuth kan kräva ny inloggning. E-postmeddelanden och bilagor ingår inte.");
            confirm.Show(); Pump(); Capture(confirm, "restore-confirmation.png", output); confirm.Close();
            main.Width = main.MinWidth; main.Height = main.MinHeight; Pump(); Capture(main, "overview-minimum-size.png", output);
            Console.WriteLine("PASS WPF rendering: overview, minimum window size, profile popup and dark dialogs");
            Console.WriteLine("PASS Password UI: short password rejected by default, accepted with opt-in, accepted on restore");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { main?.Close(); app.Shutdown(); }
    }
    private static void TestPassword(Window owner, bool create, string output)
    {
        var dialog = new PasswordDialog(owner, create);
        Exception? error = null;
        dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                Capture(dialog, create ? "backup-password.png" : "restore-password.png", output);
                foreach (var box in Descendants<PasswordBox>(dialog)) box.Password = "1234";
                var accept = Descendants<Button>(dialog).Single(b => b.IsDefault);
                if (create)
                {
                    accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (!dialog.IsVisible || !Descendants<TextBlock>(dialog).Any(t => t.Text.Contains("12–1024", StringComparison.Ordinal) && t.Visibility == Visibility.Visible))
                        throw new Exception("Short password was not rejected by default.");
                    Descendants<CheckBox>(dialog).Single().IsChecked = true; Pump();
                    Capture(dialog, "backup-short-password.png", output);
                }
                accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex) { error = ex; dialog.Close(); }
        }));
        bool? result = dialog.ShowDialog();
        if (error is not null) throw error;
        if (result != true) throw new Exception("Password dialog did not accept the requested password.");
        dialog.Clear();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Capture(FrameworkElement element, string name, string output)
    {
        element.UpdateLayout();
        int width = (int)Math.Ceiling(element.ActualWidth), height = (int)Math.Ceiling(element.ActualHeight);
        if (width < 100 || height < 30) throw new Exception("Window or control has no layout size.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name)); png.Save(file);
    }
}
