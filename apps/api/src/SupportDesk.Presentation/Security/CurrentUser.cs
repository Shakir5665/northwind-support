using System.Security.Claims;
using SupportDesk.Application.Abstractions;

namespace SupportDesk.Presentation.Security;

/// <summary>
/// Extracts the authenticated agent's identity and claims from the current HttpContext.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public int? AgentId
    {
        get
        {
            var value = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User?.FindFirst("sub")?.Value;

            return int.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Email =>
        User?.FindFirst(ClaimTypes.Email)?.Value
        ?? User?.FindFirst("email")?.Value;

    public string? FullName =>
        User?.FindFirst(ClaimTypes.Name)?.Value
        ?? User?.FindFirst("name")?.Value;
}
