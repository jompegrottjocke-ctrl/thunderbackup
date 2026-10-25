using ThunderbirdAccounts.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ThunderbirdAccounts.App;

internal sealed class PasswordDialog : Window
{
    private readonly PasswordBox password = new();
    private readonly PasswordBox confirmation = new();
    private readonly CheckBox allowShort = new();
    public string Secret => password.Password;
    public bool AllowsShortPassword => allowShort.IsChecked == true;

    public PasswordDialog(Window owner, bool create)
    {
        Style = (Style)FindResource(typeof(Window));
        Owner = owner; Title = create ? TextCatalog.Get("CreateEncryptedBackup") : TextCatalog.Get("OpenYourBackup");
        Width = 510; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        var panel = new StackPanel { Margin = new Thickness(30) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = create ? TextCatalog.Get("PROTECTYOURACCOUNTS") : TextCatalog.Get("RestoreAccountsEyebrow"), Foreground = (Brush)FindResource("Accent"), FontSize = 10, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = create ? TextCatalog.Get("OneBackupYoursAlone") : TextCatalog.Get("WelcomeBack"), FontSize = 27, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 10) });
        panel.Children.Add(new TextBlock
        {
            Text = create ? TextCatalog.Get("ChooseAPasswordForTheBackupFileKeepIt") : TextCatalog.Get("EnterThePasswordUsedWhenTheBackupWasCreated"),
            Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 24)
        });
        panel.Children.Add(new TextBlock { Text = TextCatalog.Get("BackupPassword"), FontSize = 12, FontWeight = FontWeights.SemiBold });
        System.Windows.Automation.AutomationProperties.SetName(password, TextCatalog.Get("BackupPassword"));
        System.Windows.Automation.AutomationProperties.SetName(confirmation, TextCatalog.Get("ConfirmBackupPassword"));
        panel.Children.Add(password);
        if (create)
        {
            panel.Children.Add(new TextBlock { Text = TextCatalog.Get("ConfirmPassword"), FontSize = 12, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(confirmation);
            allowShort.Content = TextCatalog.Get("AllowAShorterPassword");
            panel.Children.Add(allowShort);
            var guidance = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 7, 0, 16) };
            void UpdateGuidance()
            {
                guidance.Text = AllowsShortPassword
                    ? TextCatalog.Get("AtLeastCharacterAShortPasswordIsEasierTo")
                    : TextCatalog.Get("AtLeastCharactersRecommendedEnableTheOptionAboveTo");
                guidance.Foreground = (Brush)FindResource(AllowsShortPassword ? "Accent" : "Muted");
            }
            allowShort.Checked += (_, _) => UpdateGuidance();
            allowShort.Unchecked += (_, _) => UpdateGuidance();
            UpdateGuidance(); panel.Children.Add(guidance);
        }
        panel.Children.Add(new TextBlock { Text = TextCatalog.Get("ThisIsNotThunderbirdSPrimaryPassword"), Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(0, 0, 0, 16) });
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 166, 151)), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(error);
        void ClearError() { error.Text = ""; error.Visibility = Visibility.Collapsed; }
        password.PasswordChanged += (_, _) => ClearError();
        confirmation.PasswordChanged += (_, _) => ClearError();
        allowShort.Checked += (_, _) => ClearError();
        allowShort.Unchecked += (_, _) => ClearError();
        var buttons = new Grid(); buttons.ColumnDefinitions.Add(new ColumnDefinition()); buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ok = new Button { Content = create ? TextCatalog.Get("CreateEncryptedBackup") : TextCatalog.Get("UnlockBackup"), IsDefault = true, Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 10, 0) };
        ok.Click += (_, _) =>
        {
            int minimum = create && !AllowsShortPassword ? 12 : 1;
            if (password.Password.Length < minimum || password.Password.Length > 1024)
            {
                error.Text = TextCatalog.Get("UseCharacters", minimum);
                error.Visibility = Visibility.Visible; password.Focus(); return;
            }
            if (create && password.Password != confirmation.Password)
            {
                error.Text = TextCatalog.Get("ThePasswordsDoNotMatch");
                error.Visibility = Visibility.Visible; confirmation.Focus(); return;
            }
            DialogResult = true;
        };
        var cancel = new Button { Content = TextCatalog.Get("Cancel"), IsCancel = true };
        buttons.Children.Add(ok); Grid.SetColumn(cancel, 1); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        Loaded += (_, _) => password.Focus();
    }
    public void Clear() { password.Clear(); confirmation.Clear(); }
}
