using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Tickets;

/// <summary>
/// Explanations for automatic triage decisions made when creating a ticket (BR-1 to BR-6).
/// </summary>
public sealed class TicketTriageDto
{
    public string PriorityReason { get; set; } = string.Empty;

    public string SlaReason { get; set; } = string.Empty;

    public string AssignmentReason { get; set; } = string.Empty;
}

/// <summary>
/// Body of POST /api/tickets/{id}/escalate.
/// </summary>
public sealed record EscalateTicketRequest(string Reason, string EscalatedBy);

/// <summary>
/// A single ticket escalation history record.
/// </summary>
public sealed class TicketEscalationDto
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public TicketPriority FromPriority { get; set; }

    public TicketPriority ToPriority { get; set; }

    public int? FromAgentId { get; set; }

    public int? ToAgentId { get; set; }

    public DateTime? FromDueAtUtc { get; set; }

    public DateTime? ToDueAtUtc { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string EscalatedBy { get; set; } = string.Empty;

    public DateTime EscalatedAtUtc { get; set; }
}

/// <summary>
/// Response of POST /api/tickets/{id}/escalate: the updated ticket and recorded escalation.
/// </summary>
public sealed class EscalationResponseDto
{
    public TicketDetailDto Ticket { get; set; } = null!;

    public TicketEscalationDto Escalation { get; set; } = null!;
}
