using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Repositories;

public sealed class AgentRepository(SupportDbContext db) : IAgentRepository
{
    /// <summary>Loads the whole aggregate, specializations included.</summary>
    public Task<Agent?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Set<Agent>()
            .Include(a => a.Specializations)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Agent?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        db.Set<Agent>()
            .Include(a => a.Specializations)
            .FirstOrDefaultAsync(a => a.Email == email, ct);

    public async Task<IReadOnlyList<AgentWorkload>> GetCandidateWorkloadsAsync(CancellationToken ct)
    {
        var agents = await db.Set<Agent>()
            .Include(a => a.Specializations)
            .AsNoTracking()
            .ToListAsync(ct);

        var openCounts = await db.Set<Ticket>()
            .AsNoTracking()
            .Where(t => t.AssignedAgentId != null &&
                        t.Status != TicketStatus.Resolved &&
                        t.Status != TicketStatus.Closed)
            .GroupBy(t => t.AssignedAgentId!.Value)
            .Select(g => new { AgentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AgentId, x => x.Count, ct);

        return agents
            .Select(a => new AgentWorkload(a, openCounts.GetValueOrDefault(a.Id, 0)))
            .ToList();
    }
}
