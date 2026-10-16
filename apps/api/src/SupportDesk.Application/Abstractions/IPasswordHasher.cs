namespace SupportDesk.Application.Abstractions;

/// <summary>
/// Hashes and verifies agent passwords using a vetted cryptographic hashing algorithm.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Generates a salted, cryptographically secure hash for the given password.</summary>
    string HashPassword(string password);

    /// <summary>Verifies whether the provided plaintext password matches the stored hash.</summary>
    bool VerifyPassword(string passwordHash, string providedPassword);
}
