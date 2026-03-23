using System.Security.Cryptography;
using System.Text;

namespace SteamServerTool;

/// <summary>Shared helper for generating secure random passwords.</summary>
internal static class PasswordHelper
{
    private const string Chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*";

    /// <summary>Generates a cryptographically secure random password of the specified length.</summary>
    public static string Generate(int length)
    {
        var bytes  = RandomNumberGenerator.GetBytes(length);
        var result = new StringBuilder(length);
        foreach (var b in bytes)
            result.Append(Chars[b % Chars.Length]);
        return result.ToString();
    }
}
