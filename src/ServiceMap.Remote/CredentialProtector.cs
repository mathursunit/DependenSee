using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace ServiceMap.Remote;

/// <summary>
/// Protects saved credentials with Windows DPAPI (per-user), so passwords are
/// never written to disk in plaintext. Encryption is tied to the current
/// Windows user account. Only used on the Windows host that runs the viewer.
/// </summary>
public static class CredentialProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Carrier.DependenSee.Remote.v1");

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>DPAPI-encrypt a secret and return base64. Throws off Windows.</summary>
    [SupportedOSPlatform("windows")]
    public static string Protect(string plaintext)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Credential encryption requires Windows (DPAPI).");
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Decrypt a base64 DPAPI blob produced by <see cref="Protect"/>.</summary>
    [SupportedOSPlatform("windows")]
    public static string Unprotect(string protectedBase64)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Credential decryption requires Windows (DPAPI).");
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
