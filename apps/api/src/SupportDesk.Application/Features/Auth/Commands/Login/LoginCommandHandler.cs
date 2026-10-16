using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Auth.Commands.Login;

/// <summary>
/// Authenticates an agent using email and password, returning a signed JWT access token.
/// </summary>
public sealed class LoginCommandHandler(
    IAgentRepository agents,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ILogger<LoginCommandHandler> logger)
{
    public async Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var agent = await agents.GetByEmailAsync(request.Email.Trim(), ct);

        // Unknown email, wrong password, or inactive agent return HTTP 401 with generic message
        if (agent is null)
        {
            logger.LogWarning("Login failed: Agent with email {Email} not found.", request.Email);
            throw new AuthenticationException();
        }

        if (!agent.IsActive)
        {
            logger.LogWarning("Login failed: Agent {Email} (ID {Id}) is inactive.", agent.Email, agent.Id);
            throw new AuthenticationException();
        }

        if (string.IsNullOrEmpty(agent.PasswordHash) || !passwordHasher.VerifyPassword(agent.PasswordHash, request.Password))
        {
            logger.LogWarning("Login failed: Invalid password for agent {Email} (ID {Id}).", agent.Email, agent.Id);
            throw new AuthenticationException();
        }

        var (token, expiresAtUtc) = tokenService.GenerateToken(agent);

        logger.LogInformation("Agent {Email} (ID {Id}) authenticated successfully.", agent.Email, agent.Id);

        return new LoginResponse
        {
            Token = token,
            ExpiresAtUtc = expiresAtUtc,
            AgentId = agent.Id,
            Name = agent.FullName,
            Email = agent.Email
        };
    }
}
