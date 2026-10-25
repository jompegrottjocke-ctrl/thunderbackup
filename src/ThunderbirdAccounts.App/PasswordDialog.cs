using System.Windows;
using System.Windows.Controls;

namespace ThunderbirdAccounts.App;

internal sealed class PasswordDialog : Window
{
    private readonly PasswordBox password = new();
    private readonly PasswordBox confirmation = new();
    public string Secret => password.Password;
    public PasswordDialog(Window owner, bool create)
    {
        Owner = owner; Title = create ? "Skydda din backup" : "Öppna backup";
        Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(28) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = create ? "Välj ett starkt backuplösenord" : "Ange backuplösenordet", FontSize = 20, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Minst 12 tecken. Lösenordet kan inte återställas.\nDetta är inte Thunderbirds Primary Password.", TextWrapping = TextWrapping.Wrap });
        System.Windows.Automation.AutomationProperties.SetName(password, "Backuplösenord");
        panel.Children.Add(password);
        if (create) { panel.Children.Add(new TextBlock { Text = "Bekräfta lösenord" }); panel.Children.Add(confirmation); }
        var ok = new Button { Content = create ? "Kryptera backup" : "Lås upp", IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (password.Password.Length < 12 || password.Password.Length > 1024) { MessageBox.Show(this, "Använd 12–1024 tecken."); return; }
            if (create && password.Password != confirmation.Password) { MessageBox.Show(this, "Lösenorden stämmer inte överens."); return; }
            DialogResult = true;
        };
        var cancel = new Button { Content = "Avbryt", IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        Loaded += (_, _) => password.Focus();
    }
    public void Clear() { password.Clear(); confirmation.Clear(); }
}
