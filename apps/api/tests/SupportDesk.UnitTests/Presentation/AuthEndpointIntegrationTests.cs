using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.UnitTests.Presentation;

public class AuthEndpointIntegrationTests : IAsyncLifetime
{
    private SqliteConnection _sqliteConnection = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private const string DevPassword = "DevPassword123!";
    private const string JwtSecret = "test-secret-key-at-least-32-chars-long-for-jwt-signing!";

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("Jwt__SigningKey", JwtSecret);
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Server=localhost,1433;Database=SupportDesk;User Id=sa;Password=Local_Dev_Password1;TrustServerCertificate=True;Encrypt=False");

        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureTestServices(services =>
            {
                // Remove all existing EF Core registrations to prevent provider conflict
                var efDescriptors = services
                    .Where(d => d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                                d.ServiceType == typeof(DbContextOptions) ||
                                d.ServiceType == typeof(DbContextOptions<SupportDbContext>) ||
                                d.ServiceType == typeof(SupportDbContext))
                    .ToList();

                foreach (var d in efDescriptors)
                {
                    services.Remove(d);
                }

                services.AddDbContext<SupportDbContext>(options =>
                {
                    options.UseSqlite(_sqliteConnection);
                });
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SupportDbContext>());
            });
        });

        _client = _factory.CreateClient();

        // Seed SQLite database for test
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await db.Database.EnsureCreatedAsync();

        var hash = hasher.HashPassword(DevPassword);

        var activeAgent = new Agent("Alex Turner", "alex.turner@northwind-support.example", 10, DateTime.UtcNow);
        activeAgent.SetPasswordHash(hash);

        var inactiveAgent = new Agent("Yuki Tanaka", "yuki.tanaka@northwind-support.example", 8, DateTime.UtcNow);
        inactiveAgent.SetPasswordHash(hash);
        inactiveAgent.Deactivate();

        var customer = new Customer("Contoso Ltd", "support@contoso.example", null, CustomerTier.Standard, DateTime.UtcNow);
        var category = new Category("General", false, false);

        db.AddRange(activeAgent, inactiveAgent, customer, category);
        await db.SaveChangesAsync();

        var ticket = Ticket.Raise(
            "TCK-TEST-001",
            "Sample Test Ticket",
            "Description of the test ticket with ample detail",
            customer.Id,
            category.Id,
            TicketPriority.Low,
            DateTime.UtcNow,
            DateTime.UtcNow.AddHours(24),
            activeAgent.Id);

        db.Add(ticket);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _sqliteConnection.DisposeAsync();
    }

    [Fact]
    public async Task Login_WithCorrectPassword_Returns200AndValidJwt()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            "alex.turner@northwind-support.example",
            DevPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.Token));
        Assert.True(body.ExpiresAtUtc > DateTime.UtcNow);
        Assert.Equal("Alex Turner", body.Name);
        Assert.Equal("alex.turner@northwind-support.example", body.Email);
    }

    [Theory]
    [InlineData("alex.turner@northwind-support.example", "WrongPassword!")] // Incorrect password
    [InlineData("unknown@example.com", "DevPassword123!")]                  // Unknown email
    [InlineData("yuki.tanaka@northwind-support.example", "DevPassword123!")] // Inactive agent
    public async Task Login_WithWrongEmailWrongPasswordOrInactiveAgent_ReturnsGeneric401(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Verify generic error message is identical for all three failure cases
        Assert.True(root.TryGetProperty("detail", out var detail));
        Assert.Equal("Invalid email or password.", detail.GetString());
    }

    [Fact]
    public async Task ProtectedEndpoint_Returns401WithoutToken_AndSucceedsWithValidToken()
    {
        // 1. Calling protected endpoint without token returns 401
        var unauthenticatedResponse = await _client.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);

        // 2. Log in to get token
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            "alex.turner@northwind-support.example",
            DevPassword));
        var authData = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(authData);

        // 3. Calling protected endpoint with valid Bearer token succeeds
        var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/tickets");
        authenticatedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.Token);

        var authenticatedResponse = await _client.SendAsync(authenticatedRequest);
        Assert.Equal(HttpStatusCode.OK, authenticatedResponse.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsAuthenticatedAgent_AndRejectsAnonymous()
    {
        // 1. Anonymous request rejected with 401
        var anonymousResponse = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        // 2. Authenticated request returns agent claims
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            "alex.turner@northwind-support.example",
            DevPassword));
        var authData = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(authData);

        var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        authenticatedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.Token);

        var meResponse = await _client.SendAsync(authenticatedRequest);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var meData = await meResponse.Content.ReadFromJsonAsync<CurrentUserDto>();
        Assert.NotNull(meData);
        Assert.Equal("Alex Turner", meData.Name);
        Assert.Equal("alex.turner@northwind-support.example", meData.Email);
    }

    [Fact]
    public async Task EscalateEndpoint_RecordsAuthenticatedAgent_IgnoringForgedActorInBody()
    {
        // 1. Authenticate as Alex Turner
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            "alex.turner@northwind-support.example",
            DevPassword));
        var authData = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(authData);

        // 2. Submit escalation request with forged "escalatedBy"
        var escalateRequest = new HttpRequestMessage(HttpMethod.Post, "/api/tickets/1/escalate")
        {
            Content = JsonContent.Create(new EscalateTicketRequest(
                "Urgent escalation request for testing",
                "Forged Attacker Name"))
        };
        escalateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.Token);

        var response = await _client.SendAsync(escalateRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        var result = await response.Content.ReadFromJsonAsync<EscalationResponseDto>(jsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Escalation);

        // Escalation history must record Alex Turner (from JWT), not "Forged Attacker Name"
        Assert.Equal("Alex Turner", result.Escalation.EscalatedBy);
        Assert.NotEqual("Forged Attacker Name", result.Escalation.EscalatedBy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short-key")]
    public void Startup_FailsClearly_WhenJwtKeyIsMissingOrTooShort(string invalidKey)
    {
        Environment.SetEnvironmentVariable("Jwt__SigningKey", invalidKey);

        try
        {
            using var invalidFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
            });

            var ex = Assert.Throws<InvalidOperationException>(() => invalidFactory.CreateClient());
            Assert.Contains("JWT signing key is missing or invalid", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Jwt__SigningKey", JwtSecret);
        }
    }
}
