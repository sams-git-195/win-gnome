using System.Windows;

namespace WinGnome.Features.Settings.Views;

/// <summary>A themed yes/no prompt. For destructive actions the confirming button is red and Cancel is the default.</summary>
internal sealed partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmLabel, bool isDestructive, bool isDark)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Message.Text = message;

        var confirm = isDestructive ? DangerButton : ConfirmButton;
        var unused = isDestructive ? ConfirmButton : DangerButton;
        confirm.Content = confirmLabel;
        unused.Visibility = Visibility.Collapsed;
        if (isDestructive)
        {
            CancelButton.IsDefault = true;
        }
        else
        {
            confirm.IsDefault = true;
        }

        SourceInitialized += (_, _) => TitleBarTheme.Apply(this, isDark);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
