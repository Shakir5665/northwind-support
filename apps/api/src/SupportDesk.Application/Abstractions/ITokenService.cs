using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Application.Abstractions;

/// <summary>
/// Generates signed JWT access tokens for authenticated agents.
/// </summary>
public interface ITokenService
{
    /// <summary>Generates a signed JWT token and its UTC expiration timestamp.</summary>
    (string Token, DateTime ExpiresAtUtc) GenerateToken(Agent agent);
}
