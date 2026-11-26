using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace SSHTunnel4Win.Services;

public enum PuttyKeyError
{
    NotAPuttyKey,
    UnsupportedVersion,
    UnsupportedAlgorithm,
    PassphraseRequired,
    WrongPassphrase,
    Corrupt
}

public class PuttyKeyException : Exception
{
    public PuttyKeyError Error { get; }
    /// <summary>Extra context for the message, e.g. the unsupported key type.</summary>
    public string Detail { get; init; } = "";
    public PuttyKeyException(PuttyKeyError error, string message) : base(message) => Error = error;
}

/// <summary>
/// Reader for PuTTY .ppk private keys (format versions 2 and 3) that converts them to the
/// OpenSSH private key format, since Windows OpenSSH's ssh.exe cannot read .ppk files.
/// Format reference: PuTTY sshpubk.c.
/// </summary>
public sealed class PuttyKey
{
    private const string MacKeyHeader = "putty-private-key-file-mac-key";

    public int Version { get; private init; }
    public string Algorithm { get; private init; } = "";
    public string Encryption { get; private init; } = "";
    /// <summary>Raw comment (Latin-1 mapped bytes); use <see cref="DisplayComment"/> for UI.</summary>
    public string Comment { get; private init; } = "";
    public string DisplayComment
    {
        get
        {
            var bytes = Encoding.Latin1.GetBytes(Comment);
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { return Comment; }
        }
    }
    public byte[] PublicBlob { get; private init; } = Array.Empty<byte>();
    public bool IsEncrypted => Encryption != "none";

    private byte[] _privateBlob = Array.Empty<byte>();
    private byte[] _mac = Array.Empty<byte>();
    private string _kdf = "";
    private int _argonMemory, _argonPasses, _argonParallelism;
    private byte[] _argonSalt = Array.Empty<byte>();

    private static readonly HashSet<string> SupportedAlgorithms = new()
    {
        "ssh-rsa", "ssh-ed25519", "ecdsa-sha2-nistp256", "ecdsa-sha2-nistp384", "ecdsa-sha2-nistp521"
    };

    /// <summary>True if the file starts like a PuTTY key, regardless of its extension.</summary>
    public static bool IsPuttyKeyFile(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var first = reader.ReadLine();
            return first != null && first.StartsWith("PuTTY-User-Key-File-");
        }
        catch
        {
            return false;
        }
    }

    // Header text is read as Latin-1 so every byte round-trips exactly into the MAC computation
    public static PuttyKey Load(string path) => Parse(Encoding.Latin1.GetString(File.ReadAllBytes(path)));

    public static PuttyKey Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int i = 0;

        string Header(string expected)
        {
            if (i >= lines.Length) throw Corrupt();
            var line = lines[i++];
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0 || line[..colon] != expected) throw Corrupt();
            return line[(colon + 2)..];
        }

        byte[] Base64Lines(string header)
        {
            if (!int.TryParse(Header(header), out var count) || count < 0 || i + count > lines.Length) throw Corrupt();
            var sb = new StringBuilder();
            for (int n = 0; n < count; n++) sb.Append(lines[i++].Trim());
            try { return Convert.FromBase64String(sb.ToString()); }
            catch (FormatException) { throw Corrupt(); }
        }

        byte[] HexValue(string header)
        {
            try { return Convert.FromHexString(Header(header).Trim()); }
            catch (FormatException) { throw Corrupt(); }
        }

        var first = lines.Length > 0 ? lines[0] : "";
        if (!first.StartsWith("PuTTY-User-Key-File-"))
            throw new PuttyKeyException(PuttyKeyError.NotAPuttyKey, "Not a PuTTY private key file.");
        var colon0 = first.IndexOf(": ", StringComparison.Ordinal);
        if (colon0 < 0) throw Corrupt();
        if (!int.TryParse(first["PuTTY-User-Key-File-".Length..colon0], out var version) || version is < 2 or > 3)
            throw new PuttyKeyException(PuttyKeyError.UnsupportedVersion,
                $"PuTTY key file version {first["PuTTY-User-Key-File-".Length..colon0]} is not supported. Re-save it with a current PuTTYgen.");
        var algorithm = first[(colon0 + 2)..];
        i = 1;

        var encryption = Header("Encryption");
        if (encryption is not ("none" or "aes256-cbc"))
            throw new PuttyKeyException(PuttyKeyError.Corrupt, $"Unknown key encryption \"{encryption}\".");
        var comment = Header("Comment");
        var publicBlob = Base64Lines("Public-Lines");

        string kdf = ""; int memory = 0, passes = 0, parallelism = 0; byte[] salt = Array.Empty<byte>();
        if (version == 3 && encryption != "none")
        {
            kdf = Header("Key-Derivation");
            if (kdf is not ("Argon2id" or "Argon2i" or "Argon2d")) throw Corrupt();
            if (!int.TryParse(Header("Argon2-Memory"), out memory) ||
                !int.TryParse(Header("Argon2-Passes"), out passes) ||
                !int.TryParse(Header("Argon2-Parallelism"), out parallelism))
                throw Corrupt();
            // Argon2 itself rejects these (m >= 8p per RFC 9106) with exceptions callers don't expect
            if (passes < 1 || parallelism < 1 || memory < 8L * parallelism) throw Corrupt();
            salt = HexValue("Argon2-Salt");
        }

        var privateBlob = Base64Lines("Private-Lines");
        var mac = HexValue("Private-MAC");

        return new PuttyKey
        {
            Version = version,
            Algorithm = algorithm,
            Encryption = encryption,
            Comment = comment,
            PublicBlob = publicBlob,
            _privateBlob = privateBlob,
            _mac = mac,
            _kdf = kdf,
            _argonMemory = memory,
            _argonPasses = passes,
            _argonParallelism = parallelism,
            _argonSalt = salt
        };
    }

    private static PuttyKeyException Corrupt() =>
        new(PuttyKeyError.Corrupt, "The PuTTY key file is damaged or incomplete.");

    /// <summary>Checks the passphrase (and the file's integrity) without producing key material.</summary>
    public bool CheckPassphrase(string? passphrase)
    {
        try
        {
            var plain = DecryptPrivateBlob(passphrase);
            CryptographicOperations.ZeroMemory(plain);
            return true;
        }
        catch (PuttyKeyException e) when (e.Error == PuttyKeyError.WrongPassphrase || e.Error == PuttyKeyError.PassphraseRequired)
        {
            return false;
        }
    }

    /// <summary>Decrypts (if needed) the private blob and verifies the file MAC.</summary>
    private byte[] DecryptPrivateBlob(string? passphrase)
    {
        if (IsEncrypted && string.IsNullOrEmpty(passphrase))
            throw new PuttyKeyException(PuttyKeyError.PassphraseRequired, "This key is protected by a passphrase.");
        if (!IsEncrypted)
            return DecryptPrivateBlob(Array.Empty<byte>());

        // PuTTYgen may have taken a non-ASCII passphrase as UTF-8 or in the ANSI code page
        PuttyKeyException? last = null;
        foreach (var encoding in PassphraseEncodings(passphrase!))
        {
            try { return DecryptPrivateBlob(encoding.GetBytes(passphrase!)); }
            catch (PuttyKeyException e) when (e.Error == PuttyKeyError.WrongPassphrase) { last = e; }
        }
        throw last!;
    }

    private static IEnumerable<Encoding> PassphraseEncodings(string passphrase)
    {
        yield return Encoding.UTF8;
        if (passphrase.Any(c => c > 0x7F))
        {
            Encoding? ansi = null;
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                ansi = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
            }
            catch { }
            if (ansi != null) yield return ansi;
        }
    }

    private byte[] DecryptPrivateBlob(byte[] pass)
    {
        byte[] plain;
        byte[] macKey;
        try
        {
            if (Version == 2)
            {
                macKey = SHA1.HashData(Concat(Encoding.ASCII.GetBytes(MacKeyHeader), pass));
                plain = IsEncrypted ? AesCbcDecrypt(V2CipherKey(pass), new byte[16], _privateBlob) : (byte[])_privateBlob.Clone();
            }
            else
            {
                if (IsEncrypted)
                {
                    var derived = Argon2Derive(pass, 80);
                    var cipherKey = derived[..32];
                    var iv = derived[32..48];
                    macKey = derived[48..80];
                    plain = AesCbcDecrypt(cipherKey, iv, _privateBlob);
                    CryptographicOperations.ZeroMemory(derived);
                }
                else
                {
                    macKey = Array.Empty<byte>();
                    plain = (byte[])_privateBlob.Clone();
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pass);
        }

        var macData = new SshWriter();
        macData.WriteString(Encoding.Latin1.GetBytes(Algorithm));
        macData.WriteString(Encoding.Latin1.GetBytes(Encryption));
        macData.WriteString(Encoding.Latin1.GetBytes(Comment));
        macData.WriteString(PublicBlob);
        macData.WriteString(plain);
        var data = macData.ToArray();
        var computed = Version == 2 ? HMACSHA1.HashData(macKey, data) : HMACSHA256.HashData(macKey, data);
        CryptographicOperations.ZeroMemory(data);

        if (!CryptographicOperations.FixedTimeEquals(computed, _mac))
        {
            CryptographicOperations.ZeroMemory(plain);
            throw IsEncrypted
                ? new PuttyKeyException(PuttyKeyError.WrongPassphrase, "Wrong passphrase for this key.")
                : Corrupt();
        }
        return plain;
    }

    private static byte[] V2CipherKey(byte[] pass)
    {
        var key = new byte[32];
        var h0 = SHA1.HashData(Concat(new byte[] { 0, 0, 0, 0 }, pass));
        var h1 = SHA1.HashData(Concat(new byte[] { 0, 0, 0, 1 }, pass));
        Buffer.BlockCopy(h0, 0, key, 0, 20);
        Buffer.BlockCopy(h1, 0, key, 20, 12);
        return key;
    }

    private byte[] Argon2Derive(byte[] pass, int length)
    {
        Argon2 argon = _kdf switch
        {
            "Argon2i" => new Argon2i(pass),
            "Argon2d" => new Argon2d(pass),
            _ => new Argon2id(pass)
        };
        argon.Salt = _argonSalt;
        argon.MemorySize = _argonMemory;
        argon.Iterations = _argonPasses;
        argon.DegreeOfParallelism = _argonParallelism;
        return argon.GetBytes(length);
    }

    private static byte[] AesCbcDecrypt(byte[] key, byte[] iv, byte[] data)
    {
        if (data.Length % 16 != 0) throw Corrupt();
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(data, iv, PaddingMode.None);
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }

    /// <summary>Throws if Windows OpenSSH can't use this key type (e.g. Ed448, or DSA which OpenSSH dropped).</summary>
    public void EnsureSupported()
    {
        if (!SupportedAlgorithms.Contains(Algorithm))
            throw new PuttyKeyException(PuttyKeyError.UnsupportedAlgorithm,
                $"Key type \"{Algorithm}\" can't be used with Windows OpenSSH.") { Detail = Algorithm };
    }

    /// <summary>
    /// Produces an unencrypted OpenSSH private key ("openssh-key-v1") for ssh.exe.
    /// Callers must treat the result as secret and keep it off disk except briefly.
    /// </summary>
    public string ToOpenSshPrivateKey(string? passphrase)
    {
        EnsureSupported();
        var priv = DecryptPrivateBlob(passphrase);
        try
        {
            var pub = new SshReader(PublicBlob);
            var privReader = new SshReader(priv);
            var section = new SshWriter();

            // Matching check ints let OpenSSH confirm a successful decryption; any value works unencrypted
            var check = RandomNumberGenerator.GetInt32(int.MaxValue);
            section.WriteUInt32((uint)check);
            section.WriteUInt32((uint)check);

            var type = pub.ReadAsciiString();
            if (type != Algorithm) throw Corrupt();
            section.WriteString(type);

            switch (Algorithm)
            {
                case "ssh-rsa":
                {
                    var e = pub.ReadBytes();
                    var n = pub.ReadBytes();
                    var d = privReader.ReadBytes();
                    var p = privReader.ReadBytes();
                    var q = privReader.ReadBytes();
                    var iqmp = privReader.ReadBytes();
                    // OpenSSH order: n, e, d, iqmp, p, q (all already in SSH mpint encoding)
                    section.WriteString(n);
                    section.WriteString(e);
                    section.WriteString(d);
                    section.WriteString(iqmp);
                    section.WriteString(p);
                    section.WriteString(q);
                    break;
                }
                case "ssh-ed25519":
                {
                    var pk = pub.ReadBytes();
                    var seed = privReader.ReadBytes();
                    if (pk.Length != 32 || seed.Length > 32) throw Corrupt();
                    if (seed.Length < 32)
                    {
                        // PuTTYgen before 0.75 stored the seed as a little-endian integer, dropping its trailing zero bytes
                        var full = new byte[32];
                        seed.CopyTo(full, 0);
                        CryptographicOperations.ZeroMemory(seed);
                        seed = full;
                    }
                    section.WriteString(pk);
                    // OpenSSH stores the 32-byte seed followed by the public key
                    var sk = Concat(seed, pk);
                    section.WriteString(sk);
                    CryptographicOperations.ZeroMemory(sk);
                    CryptographicOperations.ZeroMemory(seed);
                    break;
                }
                default: // ecdsa-sha2-nistp*
                {
                    var curve = pub.ReadBytes();
                    var point = pub.ReadBytes();
                    var d = privReader.ReadBytes();
                    section.WriteString(curve);
                    section.WriteString(point);
                    section.WriteString(d);
                    break;
                }
            }
            section.WriteString(Encoding.Latin1.GetBytes(Comment));
            // Pad the private section to the 8-byte cipher block size with 1, 2, 3, ...
            for (byte pad = 1; section.Length % 8 != 0; pad++)
                section.WriteByte(pad);

            var file = new SshWriter();
            file.WriteRaw(Encoding.ASCII.GetBytes("openssh-key-v1\0"));
            file.WriteString("none");
            file.WriteString("none");
            file.WriteString(Array.Empty<byte>());
            file.WriteUInt32(1);
            file.WriteString(PublicBlob);
            var sectionBytes = section.ToArray();
            file.WriteString(sectionBytes);
            CryptographicOperations.ZeroMemory(sectionBytes);

            var body = file.ToArray();
            var b64 = Convert.ToBase64String(body);
            CryptographicOperations.ZeroMemory(body);
            var sb = new StringBuilder("-----BEGIN OPENSSH PRIVATE KEY-----\n");
            for (int pos = 0; pos < b64.Length; pos += 70)
                sb.Append(b64, pos, Math.Min(70, b64.Length - pos)).Append('\n');
            sb.Append("-----END OPENSSH PRIVATE KEY-----\n");
            return sb.ToString();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(priv);
        }
    }

    /// <summary>The public key in authorized_keys format, e.g. for matching against known keys.</summary>
    public string PublicKeyLine => $"{Algorithm} {Convert.ToBase64String(PublicBlob)}";
}

/// <summary>Minimal SSH wire-format reader (RFC 4251 uint32 / string).</summary>
internal sealed class SshReader
{
    private readonly byte[] _data;
    private int _pos;
    public SshReader(byte[] data) => _data = data;

    public uint ReadUInt32()
    {
        if (_pos + 4 > _data.Length) throw new PuttyKeyException(PuttyKeyError.Corrupt, "The key data is truncated.");
        var v = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(_pos));
        _pos += 4;
        return v;
    }

    public byte[] ReadBytes()
    {
        var len = ReadUInt32();
        if (len > _data.Length - _pos) throw new PuttyKeyException(PuttyKeyError.Corrupt, "The key data is truncated.");
        var r = _data.AsSpan(_pos, (int)len).ToArray();
        _pos += (int)len;
        return r;
    }

    public string ReadAsciiString() => Encoding.ASCII.GetString(ReadBytes());
}

/// <summary>Minimal SSH wire-format writer.</summary>
internal sealed class SshWriter
{
    private readonly MemoryStream _ms = new();
    public long Length => _ms.Length;

    public void WriteUInt32(uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        _ms.Write(b);
    }

    public void WriteByte(byte b) => _ms.WriteByte(b);
    public void WriteRaw(byte[] data) => _ms.Write(data);

    public void WriteString(byte[] data)
    {
        WriteUInt32((uint)data.Length);
        _ms.Write(data);
    }

    public void WriteString(string s) => WriteString(Encoding.UTF8.GetBytes(s));

    /// <summary>Writes a non-negative big integer in SSH mpint encoding.</summary>
    public void WriteMpint(byte[] bigEndianUnsigned)
    {
        int start = 0;
        while (start < bigEndianUnsigned.Length && bigEndianUnsigned[start] == 0) start++;
        var trimmed = bigEndianUnsigned.AsSpan(start);
        var needsPad = trimmed.Length > 0 && (trimmed[0] & 0x80) != 0;
        WriteUInt32((uint)(trimmed.Length + (needsPad ? 1 : 0)));
        if (needsPad) _ms.WriteByte(0);
        _ms.Write(trimmed);
    }

    public byte[] ToArray() => _ms.ToArray();
}
