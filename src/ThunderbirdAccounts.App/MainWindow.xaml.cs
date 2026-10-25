using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ThunderbirdAccounts.Core;

namespace ThunderbirdAccounts.App;

public partial class MainWindow : Window
{
    private readonly BackupService service = new();
    private string? executable;
    private string? restoredPath;
    private bool busy;
    public MainWindow() { InitializeComponent(); Loaded += (_, _) => Refresh(); }
    private void Status(string text, bool error = false)
    { StatusText.Text = text; StatusText.Foreground = new System.Windows.Media.SolidColorBrush(error ? System.Windows.Media.Color.FromRgb(255, 166, 151) : System.Windows.Media.Color.FromRgb(189, 208, 225)); }
    private void SetBusy(bool value)
    {
        busy = value; BackupButton.IsEnabled = !value; RestoreButton.IsEnabled = !value;
        ProfilesBox.IsEnabled = !value; LaunchButton.IsEnabled = !value;
        Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Refresh()
    {
        if (busy) return;
        try
        {
            executable ??= ProfileDiscovery.FindExecutable(); ShowInstallation();
            var profiles = ProfileDiscovery.Find(ProfileDiscovery.ThunderbirdRoot);
            ProfilesBox.ItemsSource = profiles;
            ProfilesBox.SelectedItem = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles.FirstOrDefault();
            if (profiles.Count == 0) Status("Ingen registrerad profil hittades. Välj profilmapp eller återställ en backup.");
        }
        catch (Exception ex) { Status(ex.Message, true); }
    }
    private void ShowInstallation() => InstallationText.Text = executable is null ? "Thunderbird hittades inte automatiskt. Välj thunderbird.exe för versionskontroll och start." : $"Thunderbird {ProfileDiscovery.Version(executable)} • {executable}";
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFolderDialog { Title = "Välj Thunderbird-profilen som innehåller prefs.js" };
        if (dialog.ShowDialog(this) != true) return;
        var profile = new Profile(Path.GetFileName(dialog.FolderName), dialog.FolderName, false);
        ProfilesBox.ItemsSource = new[] { profile }; ProfilesBox.SelectedItem = profile;
    }
    private void Executable_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "Thunderbird|thunderbird.exe", Title = "Välj thunderbird.exe" };
        if (dialog.ShowDialog(this) == true) { executable = dialog.FileName; ShowInstallation(); }
    }
    private async void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (busy || ProfilesBox.SelectedItem is not Profile profile) return;
        SetBusy(true);
        try
        {
            var archive = await Task.Run(() => service.Capture(profile, ProfileDiscovery.Version(executable)));
            try { var accounts = AccountSettings.Read(archive.Preferences); AccountsGrid.ItemsSource = accounts; CountText.Text = $"{accounts.Count} konton"; Status("Profilen har lästs. Backupen inkluderar inga e-postmeddelanden."); }
            finally { BackupService.ClearFiles(archive); }
        }
        catch (Exception ex) { AccountsGrid.ItemsSource = null; CountText.Text = ""; Status(ex.Message, true); }
        finally { SetBusy(false); }
    }
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (ProfilesBox.SelectedItem is not Profile profile) { Status("Välj en profil först.", true); return; }
        var save = new SaveFileDialog { Filter = "Thunderbird accounts|*.tbaccounts", DefaultExt = ".tbaccounts", FileName = "Thunderbird-accounts-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss"), OverwritePrompt = true };
        if (save.ShowDialog(this) != true) return;
        if (File.Exists(save.FileName)) { Status("Välj ett nytt filnamn. Befintliga backuper skrivs inte över.", true); return; }
        var password = new PasswordDialog(this, true);
        if (password.ShowDialog() != true) { password.Clear(); return; }
        string secret = password.Secret; password.Clear(); SetBusy(true); Status("Skapar och krypterar kontobackup…");
        try { await Task.Run(() => service.Backup(profile, ProfileDiscovery.Version(executable), save.FileName, secret)); Status("Backup skapad: " + save.FileName + ". Förvara lösenordet separat och provåterställ före ominstallation."); }
        catch (Exception ex) { Status(ex.Message, true); }
        finally { secret = ""; SetBusy(false); }
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (executable is null) { Status("Installera Thunderbird eller välj thunderbird.exe först.", true); return; }
        var open = new OpenFileDialog { Filter = "Thunderbird accounts|*.tbaccounts" };
        if (open.ShowDialog(this) != true) return;
        var password = new PasswordDialog(this, false);
        if (password.ShowDialog() != true) { password.Clear(); return; }
        string secret = password.Secret; password.Clear(); SetBusy(true); Status("Kontrollerar backupens integritet…");
        AccountArchive? archive = null;
        try
        {
            archive = await Task.Run(() => Archive.Read(open.FileName, secret)); secret = "";
            static int Major(string version) => int.TryParse(version.Split('.')[0], out int n) ? n : 0;
            int from = Major(archive.ThunderbirdVersion), to = Major(ProfileDiscovery.Version(executable));
            if (from > 0 && to > 0 && from > to) throw new InvalidDataException("Backupen kommer från en nyare Thunderbird-version. Uppdatera Thunderbird först.");
            var accounts = AccountSettings.Read(archive.Preferences);
            string pop = accounts.Any(a => a.Type == "pop3") ? "\n\nPOP3: lokal e-post saknas i backupen. Servermeddelanden kan vara borttagna. Automatisk hämtning stängs av och lämna på server aktiveras." : "";
            var decision = MessageBox.Show(this, $"Återställ {accounts.Count} konton från {archive.ProfileName}?\n\nEn ny profil skapas. Befintliga profiler bevaras. Starta därefter med knappen Öppna återställd profil.\n\nPrimary Password behövs fortfarande om det använts. OAuth kan kräva ny autentisering. Externa signaturfiler och OpenPGP/S/MIME-nycklar ingår inte. Automatisk inloggning vid start är avstängd; hämta e-post när du kontrollerat inställningarna.{pop}", "Granska återställning", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (decision != MessageBoxResult.OK) { Status("Återställningen avbröts."); return; }
            Status("Återställer konton till en separat profil…");
            var result = await Task.Run(() => service.Restore(archive, ProfileDiscovery.ThunderbirdRoot));
            restoredPath = result.ProfilePath; LaunchButton.Visibility = Visibility.Visible;
            AccountsGrid.ItemsSource = accounts; CountText.Text = $"{accounts.Count} konton";
            Status((result.AlreadyRestored ? "Backupen har redan återställts; inga dubbletter skapades. " : "Kontona har återställts. ") + "Öppna profilen med knappen ovan. Välj sedan standardprofil via about:profiles i Thunderbird. " + (result.RegistryBackup is null ? "" : "Tidigare profilregister: " + result.RegistryBackup));
        }
        catch (Exception ex) { Status(ex.Message, true); }
        finally { if (archive is not null) BackupService.ClearFiles(archive); secret = ""; SetBusy(false); }
    }
    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (busy || executable is null || restoredPath is null) return;
            SafeFiles.EnsureStopped();
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            start.ArgumentList.Add("-no-remote"); start.ArgumentList.Add("-profile"); start.ArgumentList.Add(restoredPath);
            Process.Start(start); Status("Thunderbird har startats med den återställda profilen.");
        }
        catch (Exception ex) { Status(ex.Message, true); }
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; Status("Vänta tills pågående filarbete är klart."); }
        base.OnClosing(e);
    }
}
