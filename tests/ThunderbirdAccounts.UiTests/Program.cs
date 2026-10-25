using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ThunderbirdAccounts.App;
using ThunderbirdAccounts.Core;

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
            main = new MainWindow(preview: true); main.Show(); Pump();
            CheckColumnHeaders(main);
            Capture(main, "overview.png", output);
            var combo = (ComboBox)main.FindName("ProfilesBox"); combo.IsDropDownOpen = true; Pump();
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            if (popup.Child is not FrameworkElement dropdown || dropdown.ActualHeight < 30) throw new Exception("Profile dropdown did not render.");
            Capture(dropdown, "profiles-dropdown.png", output); combo.IsDropDownOpen = false;
            TestPassword(main, true, output); TestPassword(main, false, output);
            var confirm = new ConfirmationDialog(main, "Återställ 10 konton från Min Thunderbird-profil?\n\nEn ny profil skapas. Befintliga profiler bevaras.\n\nPrimary Password behålls. OAuth kan kräva ny inloggning. E-postmeddelanden och bilagor ingår inte.");
            confirm.Show(); Pump(); Capture(confirm, "restore-confirmation.png", output); confirm.Close();
            main.Width = main.MinWidth; main.Height = main.MinHeight; Pump(); Capture(main, "overview-minimum-size.png", output);
            TestLanguages(main, output);
            // Full-size English overview using synthetic accounts only.
            var english = new MainWindow(preview: true, previewLanguage: "en");
            try { english.Show(); Pump(); CheckColumnHeaders(english); Capture(english, "overview-en.png", output); }
            finally { english.Close(); }
            Console.WriteLine("PASS WPF rendering: overview, minimum window size, profile popup and dark dialogs");
            Console.WriteLine("PASS Password UI: short password rejected by default, accepted with opt-in, accepted on restore");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { main?.Close(); app.Shutdown(); }
    }
    private static void CheckColumnHeaders(MainWindow main)
    {
        var accounts = (DataGrid)main.FindName("AccountsGrid");
        string[] keys = ["ACCOUNT", "EMAILADDRESS", "TYPE", "INCOMING", "OUTGOING"];
        for (int i = 0; i < keys.Length; i++)
            if (accounts.Columns[i].Header is not string header || header != TextCatalog.Get(keys[i]))
                throw new Exception($"Column {i} header: expected '{TextCatalog.Get(keys[i])}', got '{accounts.Columns[i].Header}'.");
    }
    private static void TestLanguages(MainWindow main, string output)
    {
        var languages = (ComboBox)main.FindName("LanguageBox");
        var accounts = (DataGrid)main.FindName("AccountsGrid");
        var count = (TextBlock)main.FindName("CountText");
        object source = accounts.ItemsSource;
        int selection = languages.SelectedIndex;
        try
        {
            foreach (var (index, label, heading, suffix) in new[] { (1, "Skapa backup", "KONTO", "sv"), (2, "Create backup", "ACCOUNT", "en") })
            {
                languages.SelectedIndex = index; Pump();
                if (!Descendants<TextBlock>(main).Any(t => t.Text == label)) throw new Exception("Main window did not switch language: " + suffix);
                CheckColumnHeaders(main);
                if (accounts.Columns[0].Header is not string header || header != heading || count.Text != "5 " + (index == 1 ? "konton" : "accounts"))
                    throw new Exception($"Account table did not switch language: expected {heading}, got '{accounts.Columns[0].Header}', count '{count.Text}'.");
                if (!ReferenceEquals(accounts.ItemsSource, source)) throw new Exception("Language switch replaced the selected accounts.");
                var confirm = new ConfirmationDialog(main, TextCatalog.Get("RestoreAccountsFromANewProfileWillBeCreated", 5, "Test", ""));
                if (confirm.Title != (index == 1 ? "Granska återställning" : "Review restore")) throw new Exception("Confirmation title is not localized.");
                confirm.Show(); Pump(); Capture(confirm, "restore-confirmation-" + suffix + ".png", output); confirm.Close();
                TestPassword(main, true, output);
                TestPassword(main, false, output);
                Capture(main, "overview-" + suffix + ".png", output);
            }
            languages.SelectedIndex = 0; Pump();
            if (TextCatalog.Culture.Name != TextCatalog.ResolveCulture("auto", System.Globalization.CultureInfo.CurrentUICulture).Name)
                throw new Exception("Automatic language did not follow the system UI language.");
            Console.WriteLine("PASS UI languages: Swedish, English, automatic, translated dialogs and preserved account selection");
        }
        finally { languages.SelectedIndex = selection; }
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
