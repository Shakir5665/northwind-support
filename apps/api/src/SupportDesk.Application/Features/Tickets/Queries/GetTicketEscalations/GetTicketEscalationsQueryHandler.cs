using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;

namespace SupportDesk.Application.Features.Tickets.Queries.GetTicketEscalations;

/// <summary>
/// Returns a ticket's escalation history, newest first.
/// </summary>
public sealed class GetTicketEscalationsQueryHandler(ITicketQueries tickets)
{
    /// <exception cref="NotFoundException">Ticket does not exist.</exception>
    public async Task<IReadOnlyList<TicketEscalationDto>> HandleAsync(int id, CancellationToken ct)
    {
        var escalations = await tickets.GetEscalationsAsync(id, ct);
        if (escalations is null)
        {
            throw new NotFoundException("Ticket", id);
        }

        return escalations;
    }
}
