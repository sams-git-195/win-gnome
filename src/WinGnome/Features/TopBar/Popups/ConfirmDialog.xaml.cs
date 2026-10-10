using System.Windows;

namespace WinGnome.Features.TopBar.Popups;

/// <summary>Themed modal confirmation for irreversible session actions (restart, power off, log out).</summary>
internal sealed partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string confirmLabel, string? optionLabel, bool optionChecked)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        if (optionLabel is not null)
        {
            OptionText.Text = optionLabel;
            OptionCheck.IsChecked = optionChecked;
            OptionRow.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Shows the dialog and returns true only if the user clicked the confirm button.</summary>
    public static bool Ask(string title, string message, string confirmLabel) =>
        new ConfirmDialog(title, message, confirmLabel, null, false).ShowDialog() == true;

    /// <summary>
    /// Like <see cref="Ask(string, string, string)"/> with a checkbox under the message. <paramref name="option"/> is the
    /// box's state when confirmed (<paramref name="optionDefault"/> when cancelled).
    /// </summary>
    public static bool Ask(string title, string message, string confirmLabel, string optionLabel, bool optionDefault, out bool option)
    {
        var dialog = new ConfirmDialog(title, message, confirmLabel, optionLabel, optionDefault);
        var confirmed = dialog.ShowDialog() == true;
        option = confirmed ? dialog.OptionCheck.IsChecked == true : optionDefault;
        return confirmed;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
