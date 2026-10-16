namespace SupportDesk.Application.Contracts.Auth;

/// <summary>
/// Credentials payload for POST /api/auth/login.
/// </summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>
/// Successful response for POST /api/auth/login.
/// </summary>
public sealed class LoginResponse
{
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public int AgentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Profile payload returned by GET /api/auth/me.
/// </summary>
public sealed class CurrentUserDto
{
    public int AgentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Options for configuring JWT signing and token validation.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "SupportDesk";

    public string Audience { get; set; } = "SupportDeskClient";

    public int LifetimeMinutes { get; set; } = 30;
}
