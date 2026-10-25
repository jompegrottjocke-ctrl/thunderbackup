using System.Diagnostics;
using System.Globalization;
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
    private readonly CultureInfo systemCulture = CultureInfo.CurrentUICulture;
    private bool languageInitialized;
    private readonly bool previewMode;
    private void ApplyLanguage(string choice)
    {
        TextCatalog.SetLanguage(choice, systemCulture);
        foreach (var entry in TextCatalog.All()) Resources[entry.Key] = entry.Value;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(TextCatalog.Culture.IetfLanguageTag);
    }
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!languageInitialized || LanguageBox.SelectedItem is not ComboBoxItem item || item.Tag is not string choice) return;
        ApplyLanguage(choice);
        ShowInstallation();
        if (AccountsGrid.ItemsSource is IReadOnlyList<MailAccount> accounts) CountText.Text = TextCatalog.Get("AccountCount", accounts.Count);
        Status(TextCatalog.Get("ReadySelectAProfileToBegin"));
        if (previewMode) return;
        try { LanguagePreference.Save(LanguagePreference.DefaultPath, choice); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status(TextCatalog.Get("TheLanguageCouldNotBeSavedYourSelectionApplies"), true); }
    }
    public MainWindow() : this(false) { }
    internal MainWindow(bool preview)
    {
        previewMode = preview;
        string choice = preview ? "auto" : LanguagePreference.Load(LanguagePreference.DefaultPath);
        ApplyLanguage(choice);
        InitializeComponent();
        LanguageBox.SelectedIndex = choice == "sv" ? 1 : choice == "en" ? 2 : 0;
        languageInitialized = true;
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        if (!preview) Loaded += (_, _) => Refresh();
        else
        {
            // Synthetic data for Windows UI verification; no user profile is read in this mode.
            var profiles = new[] { new Profile(TextCatalog.Get("MyThunderbirdProfile"), @"C:\Users\Test\AppData\Roaming\Thunderbird\Profiles\default-release", true), new Profile(TextCatalog.Get("WorkProfile"), @"D:\Thunderbird\Arbetsprofil", false) };
            busy = true; ProfilesBox.ItemsSource = profiles; ProfilesBox.SelectedIndex = 0; busy = false;
            AccountsGrid.ItemsSource = new[]
            {
                new MailAccount("account1", TextCatalog.Get("Personal"), "anna@example.org", "imap", "imap.example.org:993", "smtp.example.org:465"),
                new MailAccount("account2", TextCatalog.Get("Work"), "anna@company.example", "imap", "outlook.office365.com:993", "smtp.office365.com:587"),
                new MailAccount("account3", TextCatalog.Get("Family"), "familj@example.org", "imap", "imap.example.org:993", "smtp.example.org:587"),
                new MailAccount("account4", TextCatalog.Get("ArchiveAccount"), "arkiv@example.org", "pop3", "pop.example.org:995", "smtp.example.org:465"),
                new MailAccount("account5", TextCatalog.Get("LocalFolders"), "", "none", "Local Folders", "")
            };
            CountText.Text = TextCatalog.Get("PreviewAccountCount");
            InstallationText.Text = @"Thunderbird • C:\Program Files\Mozilla Thunderbird\thunderbird.exe";
            Status(TextCatalog.Get("ProfileLoadedAllSupportedAccountsAreIncludedInThe"));
        }
    }
    private void Status(string text, bool error = false)
    { StatusText.Text = text; StatusText.Foreground = new System.Windows.Media.SolidColorBrush(error ? System.Windows.Media.Color.FromRgb(255, 166, 151) : System.Windows.Media.Color.FromRgb(197, 196, 205)); }
    private void SetBusy(bool value)
    {
        busy = value; LanguageBox.IsEnabled = !value; ToolsPanel.IsEnabled = !value; BackupButton.IsEnabled = !value; RestoreButton.IsEnabled = !value;
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
            if (profiles.Count == 0) Status(TextCatalog.Get("NoRegisteredProfileFoundChooseAProfileFolderOr"));
        }
        catch (Exception ex) { Status(ex.Message, true); }
    }
    private void ShowInstallation() => InstallationText.Text = executable is null ? TextCatalog.Get("ThunderbirdWasNotFoundAutomaticallyChooseThunderbirdExeTo") : $"Thunderbird {ProfileDiscovery.Version(executable)} • {executable}";
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFolderDialog { Title = TextCatalog.Get("ChooseTheThunderbirdProfileContainingPrefsJs") };
        if (dialog.ShowDialog(this) != true) return;
        var profile = new Profile(Path.GetFileName(dialog.FolderName), dialog.FolderName, false);
        ProfilesBox.ItemsSource = new[] { profile }; ProfilesBox.SelectedItem = profile;
    }
    private void Executable_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "Thunderbird|thunderbird.exe", Title = TextCatalog.Get("ChooseThunderbirdExe") };
        if (dialog.ShowDialog(this) == true) { executable = dialog.FileName; ShowInstallation(); }
    }
    private async void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (busy || ProfilesBox.SelectedItem is not Profile profile) return;
        SetBusy(true);
        try
        {
            var archive = await Task.Run(() => service.Capture(profile, ProfileDiscovery.Version(executable)));
            try { var accounts = AccountSettings.Read(archive.Preferences); AccountsGrid.ItemsSource = accounts; CountText.Text = TextCatalog.Get("AccountCount", accounts.Count); Status(TextCatalog.Get("ProfileLoadedTheBackupDoesNotIncludeEmailMessages")); }
            finally { BackupService.ClearFiles(archive); }
        }
        catch (Exception ex) { AccountsGrid.ItemsSource = null; CountText.Text = ""; Status(ex.Message, true); }
        finally { SetBusy(false); }
    }
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (ProfilesBox.SelectedItem is not Profile profile) { Status(TextCatalog.Get("SelectAProfileFirst"), true); return; }
        var save = new SaveFileDialog { Filter = "Thunderbird accounts|*.tbaccounts", DefaultExt = ".tbaccounts", FileName = "Thunderbird-accounts-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss"), OverwritePrompt = true };
        if (save.ShowDialog(this) != true) return;
        if (File.Exists(save.FileName)) { Status(TextCatalog.Get("ChooseANewFileNameExistingBackupsAreNever"), true); return; }
        var password = new PasswordDialog(this, true);
        if (password.ShowDialog() != true) { password.Clear(); return; }
        bool allowShortPassword = password.AllowsShortPassword;
        string secret = password.Secret; password.Clear(); SetBusy(true); Status(TextCatalog.Get("CreatingAndEncryptingAccountBackup"));
        try { await Task.Run(() => service.Backup(profile, ProfileDiscovery.Version(executable), save.FileName, secret, allowShortPassword)); Status(TextCatalog.Get("BackupCreated") + save.FileName + TextCatalog.Get("KeepThePasswordSeparatelyAndTestRestoringBeforeReinstalling")); }
        catch (Exception ex) { Status(ex.Message, true); }
        finally { secret = ""; SetBusy(false); }
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (executable is null) { Status(TextCatalog.Get("InstallThunderbirdOrChooseThunderbirdExeFirst"), true); return; }
        var open = new OpenFileDialog { Filter = "Thunderbird accounts|*.tbaccounts" };
        if (open.ShowDialog(this) != true) return;
        var password = new PasswordDialog(this, false);
        if (password.ShowDialog() != true) { password.Clear(); return; }
        string secret = password.Secret; password.Clear(); SetBusy(true); Status(TextCatalog.Get("CheckingBackupIntegrity"));
        AccountArchive? archive = null;
        try
        {
            archive = await Task.Run(() => Archive.Read(open.FileName, secret)); secret = "";
            static int Major(string version) => int.TryParse(version.Split('.')[0], out int n) ? n : 0;
            int from = Major(archive.ThunderbirdVersion), to = Major(ProfileDiscovery.Version(executable));
            if (from > 0 && to > 0 && from > to) throw new InvalidDataException(TextCatalog.Get("TheBackupComesFromANewerThunderbirdVersionUpdate"));
            var accounts = AccountSettings.Read(archive.Preferences);
            string pop = accounts.Any(a => a.Type == "pop3") ? TextCatalog.Get("POPLocalEmailIsNotIncludedInTheBackup") : "";
            bool decision = ConfirmationDialog.Confirm(this, TextCatalog.Get("RestoreAccountsFromANewProfileWillBeCreated", accounts.Count, archive.ProfileName, pop));
            if (!decision) { Status(TextCatalog.Get("RestoreCancelled")); return; }
            Status(TextCatalog.Get("RestoringAccountsToASeparateProfile"));
            var result = await Task.Run(() => service.Restore(archive, ProfileDiscovery.ThunderbirdRoot));
            restoredPath = result.ProfilePath; LaunchButton.Visibility = Visibility.Visible;
            AccountsGrid.ItemsSource = accounts; CountText.Text = TextCatalog.Get("AccountCount", accounts.Count);
            Status((result.AlreadyRestored ? TextCatalog.Get("TheBackupHasAlreadyBeenRestoredNoDuplicatesWere") : TextCatalog.Get("AccountsRestored")) + TextCatalog.Get("OpenTheProfileUsingTheButtonAboveThenChoose") + (result.RegistryBackup is null ? "" : TextCatalog.Get("PreviousProfileRegistry") + result.RegistryBackup));
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
            Process.Start(start); Status(TextCatalog.Get("ThunderbirdHasStartedWithTheRestoredProfile"));
        }
        catch (Exception ex) { Status(ex.Message, true); }
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; Status(TextCatalog.Get("WaitForTheCurrentFileOperationToFinish")); }
        base.OnClosing(e);
    }
}
