namespace SupportDesk.Application.Abstractions;

/// <summary>
/// Provides access to the authenticated agent's identity extracted from validated JWT claims.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    int? AgentId { get; }

    string? Email { get; }

    string? FullName { get; }
}
