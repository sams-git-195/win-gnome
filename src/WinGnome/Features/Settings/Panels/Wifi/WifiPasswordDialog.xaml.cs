using System.Runtime.InteropServices;
using System.Windows;
using WinGnome.Features.Settings.Views;

namespace WinGnome.Features.Settings.Panels.Wifi;

/// <summary>
/// GNOME's "Authentication required" prompt for a Wi-Fi password. The password is read from the password box's
/// <c>SecureString</c> into a <c>char[]</c> that the caller clears; the only time it exists as a managed string is
/// while "Show password" is ticked (the text box needs one), and that text is cleared on untick and on close.
/// </summary>
internal sealed partial class WifiPasswordDialog : Window
{
    public WifiPasswordDialog(string network, string? note, bool isDark)
    {
        InitializeComponent();
        Message.Text = $"A password is required to connect to \"{network}\".";
        if (note is not null)
        {
            Note.Text = note;
            Note.Visibility = Visibility.Visible;
        }

        SourceInitialized += (_, _) => TitleBarTheme.Apply(this, isDark);
        Loaded += (_, _) => (ShowPassword.IsChecked == true ? (UIElement)PlainPassword : Password).Focus();
        Closed += (_, _) =>
        {
            Password.Clear();
            PlainPassword.Clear();
        };
    }

    /// <summary>The typed password; set when the dialog was accepted. The caller owns the array and must <see cref="Array.Clear(Array)"/> it.</summary>
    public char[]? Result { get; private set; }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        Result = ReadPassword();
        DialogResult = true;
    }

    private void OnShowPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ShowPassword.IsChecked == true)
        {
            var typed = ReadPassword();
            PlainPassword.Text = new string(typed);
            Array.Clear(typed);
            Password.Clear();
            Password.Visibility = Visibility.Collapsed;
            PlainPassword.Visibility = Visibility.Visible;
            PlainPassword.Focus();
            PlainPassword.CaretIndex = PlainPassword.Text.Length;
        }
        else
        {
            Password.Password = PlainPassword.Text;
            PlainPassword.Clear();
            PlainPassword.Visibility = Visibility.Collapsed;
            Password.Visibility = Visibility.Visible;
            Password.Focus();
        }
    }

    private char[] ReadPassword()
    {
        if (PlainPassword.Visibility == Visibility.Visible)
        {
            return PlainPassword.Text.ToCharArray();
        }

        using var secure = Password.SecurePassword;
        var length = secure.Length;
        if (length == 0)
        {
            return [];
        }

        var pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
        try
        {
            var chars = new char[length];
            Marshal.Copy(pointer, chars, 0, length);
            return chars;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }
    }
}
