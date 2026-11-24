using SSHTunnel4Win.Resources;

namespace SSHTunnel4Win.Services;

public static class PuttyKeyMessages
{
    /// <summary>User-facing, localized explanation of why a PuTTY key can't be used.</summary>
    public static string Describe(PuttyKeyException e) => e.Error switch
    {
        PuttyKeyError.NotAPuttyKey => Strings.KeyErrorNotPutty,
        PuttyKeyError.UnsupportedVersion => Strings.KeyErrorUnsupportedVersion,
        PuttyKeyError.UnsupportedAlgorithm => string.Format(Strings.KeyErrorUnsupportedAlgorithm, e.Detail),
        PuttyKeyError.PassphraseRequired => Strings.KeyErrorPassphraseRequired,
        PuttyKeyError.WrongPassphrase => Strings.KeyErrorWrongPassphrase,
        _ => Strings.KeyErrorCorrupt
    };
}
