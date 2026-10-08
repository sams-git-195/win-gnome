using System.Windows;

namespace WinGnome.Features.TopBar.Popups;

/// <summary>Themed modal confirmation for irreversible session actions (restart, power off, log out).</summary>
internal sealed partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string confirmLabel)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
    }

    /// <summary>Shows the dialog and returns true only if the user clicked the confirm button.</summary>
    public static bool Ask(string title, string message, string confirmLabel) =>
        new ConfirmDialog(title, message, confirmLabel).ShowDialog() == true;

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
