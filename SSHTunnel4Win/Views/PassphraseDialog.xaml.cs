using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;

namespace SSHTunnel4Win.Views;

/// <summary>Asks for a PuTTY key's passphrase and only accepts one that actually unlocks the key.</summary>
public partial class PassphraseDialog : Window
{
    private readonly PuttyKey _key;

    public string Passphrase { get; private set; } = "";

    public PassphraseDialog(PuttyKey key, string keyPath, IEnumerable<string> usedBy)
    {
        _key = key;
        InitializeComponent();
        KeyNameText.Text = string.IsNullOrEmpty(key.DisplayComment) ? Path.GetFileName(keyPath) : key.DisplayComment;
        KeyPathText.Text = keyPath;
        KeyPathText.ToolTip = keyPath;
        UsedByText.Text = string.Format(Strings.PassphraseUsedBy, string.Join(", ", usedBy));
        Loaded += (_, _) => PassphraseBox.Focus();
    }

    private void PassphraseChanged(object sender, RoutedEventArgs e)
    {
        OkBtn.IsEnabled = PassphraseBox.Password.Length > 0;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        // Key derivation (Argon2 for PPK v3) takes a moment
        Mouse.OverrideCursor = Cursors.Wait;
        bool ok;
        try
        {
            ok = _key.CheckPassphrase(PassphraseBox.Password);
        }
        catch (PuttyKeyException ex)
        {
            // Not a wrong passphrase: the file itself can't be read, so say that and leave Skip as the way out
            ErrorText.Text = PuttyKeyMessages.Describe(ex);
            ok = false;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        if (!ok)
        {
            ErrorText.Visibility = Visibility.Visible;
            PassphraseBox.SelectAll();
            PassphraseBox.Focus();
            return;
        }
        Passphrase = PassphraseBox.Password;
        DialogResult = true;
    }
}
