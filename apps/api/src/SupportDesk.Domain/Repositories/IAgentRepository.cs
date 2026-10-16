using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Domain.Repositories;

/// <summary>
/// Loads <see cref="Agent"/> aggregates.
/// </summary>
public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>Loads all candidate agents with their current count of open (unresolved, unclosed) tickets.</summary>
    Task<IReadOnlyList<AgentWorkload>> GetCandidateWorkloadsAsync(CancellationToken ct);
}
