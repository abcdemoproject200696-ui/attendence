using System.Security.Cryptography;
using System.Text;

namespace Attendance.Domain;

/// <summary>
/// Simple SHA-256 hex hasher for login passwords. No external package.
/// Note: basic/demo-level (no per-user salt) — see CONTRACT.md (internal/demo auth).
/// </summary>
public static class PasswordHasher
{
    /// <summary>Returns the lowercase hex SHA-256 of the input.</summary>
    public static string Hash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Constant-time-ish compare of a plaintext password against a stored hash.</summary>
    public static bool Verify(string password, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;
        return Hash(password).Equals(storedHash, StringComparison.OrdinalIgnoreCase);
    }
}
