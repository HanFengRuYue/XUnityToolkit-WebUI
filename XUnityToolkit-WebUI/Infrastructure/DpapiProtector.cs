using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace XUnityToolkit_WebUI.Infrastructure;

[SupportedOSPlatform("windows")]
public static class DpapiProtector
{
    private const string Prefix = "ENC:DPAPI:";

    /// <summary>
    /// Encrypts a plaintext value with DPAPI (CurrentUser scope).
    /// Returns null/empty unchanged. Already-encrypted values are returned as-is.
    /// </summary>
    public static string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        if (plaintext.StartsWith(Prefix, StringComparison.Ordinal)) return plaintext;

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Decrypts a DPAPI-encrypted value. Returns the original string if no prefix is present
    /// (plaintext compatibility). Returns the original ciphertext on decryption failure
    /// to prevent data loss on subsequent saves.
    /// </summary>
    public static string? Unprotect(string? value, ILogger logger)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;

        try
        {
            var encrypted = Convert.FromBase64String(value[Prefix.Length..]);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DPAPI 解密失败（可能是在不同账户/机器上运行），保留加密值以防数据丢失");
            return value;
        }
    }
}
