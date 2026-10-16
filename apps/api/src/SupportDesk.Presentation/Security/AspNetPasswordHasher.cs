using Microsoft.AspNetCore.Identity;
using SupportDesk.Application.Abstractions;

namespace SupportDesk.Presentation.Security;

/// <summary>
/// Hashes and verifies passwords using ASP.NET Core Identity's vetted PasswordHasher
/// (PBKDF2 with HMAC-SHA512 and per-hash cryptographically secure salts).
/// </summary>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<string> _hasher = new();

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _hasher.HashPassword(string.Empty, password);
    }

    public bool VerifyPassword(string passwordHash, string providedPassword)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrWhiteSpace(providedPassword))
        {
            return false;
        }

        var result = _hasher.VerifyHashedPassword(string.Empty, passwordHash, providedPassword);
        return result != PasswordVerificationResult.Failed;
    }
}
