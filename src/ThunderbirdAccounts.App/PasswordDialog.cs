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
        Owner = owner; Title = create ? "Skapa krypterad backup" : "Öppna din backup";
        Width = 510; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        var panel = new StackPanel { Margin = new Thickness(30) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = create ? "SKYDDA DINA KONTON" : "ÅTERSTÄLL DINA KONTON", Foreground = (Brush)FindResource("Accent"), FontSize = 10, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = create ? "En backup. Bara din." : "Välkommen tillbaka.", FontSize = 27, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 10) });
        panel.Children.Add(new TextBlock
        {
            Text = create ? "Välj ett lösenord för backupfilen. Spara det separat; ett glömt lösenord kan inte återställas." : "Ange lösenordet som användes när backupen skapades.",
            Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 24)
        });
        panel.Children.Add(new TextBlock { Text = "Backuplösenord", FontSize = 12, FontWeight = FontWeights.SemiBold });
        System.Windows.Automation.AutomationProperties.SetName(password, "Backuplösenord");
        System.Windows.Automation.AutomationProperties.SetName(confirmation, "Bekräfta backuplösenord");
        panel.Children.Add(password);
        if (create)
        {
            panel.Children.Add(new TextBlock { Text = "Bekräfta lösenord", FontSize = 12, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(confirmation);
            allowShort.Content = "Tillåt ett kortare lösenord";
            panel.Children.Add(allowShort);
            var guidance = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 7, 0, 16) };
            void UpdateGuidance()
            {
                guidance.Text = AllowsShortPassword
                    ? "Minst 1 tecken. Ett kort lösenord är lättare att gissa. Vi rekommenderar minst 12 tecken."
                    : "Minst 12 tecken rekommenderas. Aktivera valet ovan om du vill använda färre.";
                guidance.Foreground = (Brush)FindResource(AllowsShortPassword ? "Accent" : "Muted");
            }
            allowShort.Checked += (_, _) => UpdateGuidance();
            allowShort.Unchecked += (_, _) => UpdateGuidance();
            UpdateGuidance(); panel.Children.Add(guidance);
        }
        panel.Children.Add(new TextBlock { Text = "Detta är inte Thunderbirds Primary Password.", Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(0, 0, 0, 16) });
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 166, 151)), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(error);
        void ClearError() { error.Text = ""; error.Visibility = Visibility.Collapsed; }
        password.PasswordChanged += (_, _) => ClearError();
        confirmation.PasswordChanged += (_, _) => ClearError();
        allowShort.Checked += (_, _) => ClearError();
        allowShort.Unchecked += (_, _) => ClearError();
        var buttons = new Grid(); buttons.ColumnDefinitions.Add(new ColumnDefinition()); buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ok = new Button { Content = create ? "Skapa krypterad backup" : "Lås upp backup", IsDefault = true, Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 10, 0) };
        ok.Click += (_, _) =>
        {
            int minimum = create && !AllowsShortPassword ? 12 : 1;
            if (password.Password.Length < minimum || password.Password.Length > 1024)
            {
                error.Text = $"Använd {minimum}–1024 tecken.";
                error.Visibility = Visibility.Visible; password.Focus(); return;
            }
            if (create && password.Password != confirmation.Password)
            {
                error.Text = "Lösenorden stämmer inte överens.";
                error.Visibility = Visibility.Visible; confirmation.Focus(); return;
            }
            DialogResult = true;
        };
        var cancel = new Button { Content = "Avbryt", IsCancel = true };
        buttons.Children.Add(ok); Grid.SetColumn(cancel, 1); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        Loaded += (_, _) => password.Focus();
    }
    public void Clear() { password.Clear(); confirmation.Clear(); }
}
