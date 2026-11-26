using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace SSHTunnel4Win.Services;

public static class ClipboardHelper
{
    /// <summary>
    /// Sets clipboard text, retrying briefly because another process may hold the clipboard open.
    /// </summary>
    public static bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                Clipboard.SetText(text);
                if (Clipboard.GetText() == text) return true;
            }
            catch (ExternalException) { }
            Thread.Sleep(50);
        }
        return false;
    }

    public static string TryGetText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : "";
        }
        catch (ExternalException)
        {
            return "";
        }
    }
}
