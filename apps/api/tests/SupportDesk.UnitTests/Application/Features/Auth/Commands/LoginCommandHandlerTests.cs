using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Auth.Commands.Login;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.UnitTests.Application.Features.Auth.Commands;

public class LoginCommandHandlerTests
{
    private readonly Mock<IAgentRepository> _agentRepository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _handler = new LoginCommandHandler(
            _agentRepository.Object,
            _passwordHasher.Object,
            _tokenService.Object,
            NullLogger<LoginCommandHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_CorrectPassword_SucceedsAndReturnsJwtToken()
    {
        var agent = new Agent("Alex Turner", "alex.turner@northwind-support.example", 10, DateTime.UtcNow);
        agent.SetPasswordHash("HashedPassword123");
        // Use reflection to set Id for test
        typeof(Agent).BaseType!.GetProperty("Id")!.SetValue(agent, 1);

        _agentRepository.Setup(r => r.GetByEmailAsync("alex.turner@northwind-support.example", It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        _passwordHasher.Setup(p => p.VerifyPassword("HashedPassword123", "ValidPass123!"))
            .Returns(true);

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(30);
        _tokenService.Setup(t => t.GenerateToken(agent))
            .Returns(("signed.jwt.token", expiresAtUtc));

        var request = new LoginRequest("alex.turner@northwind-support.example", "ValidPass123!");
        var response = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("signed.jwt.token", response.Token);
        Assert.Equal(expiresAtUtc, response.ExpiresAtUtc);
        Assert.Equal(1, response.AgentId);
        Assert.Equal("Alex Turner", response.Name);
        Assert.Equal("alex.turner@northwind-support.example", response.Email);
    }

    [Fact]
    public async Task HandleAsync_IncorrectPassword_ThrowsAuthenticationException()
    {
        var agent = new Agent("Alex Turner", "alex.turner@northwind-support.example", 10, DateTime.UtcNow);
        agent.SetPasswordHash("HashedPassword123");

        _agentRepository.Setup(r => r.GetByEmailAsync("alex.turner@northwind-support.example", It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        _passwordHasher.Setup(p => p.VerifyPassword("HashedPassword123", "WrongPass"))
            .Returns(false);

        var request = new LoginRequest("alex.turner@northwind-support.example", "WrongPass");

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            _handler.HandleAsync(request, CancellationToken.None));

        Assert.Equal("Invalid email or password.", ex.Message);
        _tokenService.Verify(t => t.GenerateToken(It.IsAny<Agent>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_UnknownEmail_ThrowsSameGenericAuthenticationException()
    {
        _agentRepository.Setup(r => r.GetByEmailAsync("nonexistent@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        var request = new LoginRequest("nonexistent@example.com", "AnyPassword");

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            _handler.HandleAsync(request, CancellationToken.None));

        Assert.Equal("Invalid email or password.", ex.Message);
        _tokenService.Verify(t => t.GenerateToken(It.IsAny<Agent>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InactiveAgent_ThrowsSameGenericAuthenticationException()
    {
        var agent = new Agent("Yuki Tanaka", "yuki.tanaka@northwind-support.example", 8, DateTime.UtcNow);
        agent.SetPasswordHash("HashedPassword123");
        agent.Deactivate();

        _agentRepository.Setup(r => r.GetByEmailAsync("yuki.tanaka@northwind-support.example", It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        var request = new LoginRequest("yuki.tanaka@northwind-support.example", "ValidPass123!");

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            _handler.HandleAsync(request, CancellationToken.None));

        Assert.Equal("Invalid email or password.", ex.Message);
        _tokenService.Verify(t => t.GenerateToken(It.IsAny<Agent>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AgentWithoutPasswordHash_ThrowsSameGenericAuthenticationException()
    {
        var agent = new Agent("Alex Turner", "alex.turner@northwind-support.example", 10, DateTime.UtcNow);

        _agentRepository.Setup(r => r.GetByEmailAsync("alex.turner@northwind-support.example", It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        var request = new LoginRequest("alex.turner@northwind-support.example", "ValidPass123!");

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            _handler.HandleAsync(request, CancellationToken.None));

        Assert.Equal("Invalid email or password.", ex.Message);
        _tokenService.Verify(t => t.GenerateToken(It.IsAny<Agent>()), Times.Never);
    }
}
