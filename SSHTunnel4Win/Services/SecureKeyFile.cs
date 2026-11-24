using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace SSHTunnel4Win.Services;

/// <summary>
/// Writes short-lived private key files that only the current user can read.
/// OpenSSH on Windows refuses keys that other accounts can access, and plaintext
/// key material shouldn't be readable by anyone else even briefly.
/// </summary>
public static class SecureKeyFile
{
    public static string Directory => Path.Combine(Path.GetTempPath(), "sshtunnel-keys");

    public static string Write(string fileName, string content)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, fileName);
        Delete(path);

        var user = WindowsIdentity.GetCurrent().User!;
        var security = new FileSecurity();
        security.SetOwner(user);
        // Protected: no inherited entries, so the file is never readable by other accounts
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));

        var bytes = Encoding.ASCII.GetBytes(content);
        try
        {
            using var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.ReadData,
                FileShare.None, 4096, FileOptions.None, security);
            stream.Write(bytes);
        }
        finally
        {
            Array.Clear(bytes);
        }
        return path;
    }

    public static void Delete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            // Overwrite before deleting so the plaintext doesn't linger in freed clusters
            var length = new FileInfo(path).Length;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                stream.Write(new byte[length]);
            File.Delete(path);
        }
        catch { }
    }
}
