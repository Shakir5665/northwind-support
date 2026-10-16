using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

/// <summary>
/// Escalates an open ticket by bumping its priority, recomputing its SLA,
/// re-evaluating its owner, and recording an immutable history entry (BR-7).
/// </summary>
public sealed class EscalateTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    IUnitOfWork unitOfWork,
    IClock clock,
    SlaOptions slaOptions,
    GetTicketQueryHandler getTicket,
    ILogger<EscalateTicketCommandHandler> logger,
    ICurrentUser? currentUser = null)
{
    /// <exception cref="NotFoundException">Ticket does not exist.</exception>
    /// <exception cref="BusinessRuleViolationException">Ticket is Critical, Resolved, or Closed.</exception>
    public async Task<EscalationResponseDto> HandleAsync(int id, EscalateTicketRequest request, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ticket", id);

        var nowUtc = clock.UtcNow;

        // BR-7 invariant checks on status and priority
        if (ticket.Status is TicketStatus.Resolved)
        {
            throw new BusinessRuleViolationException($"Ticket {ticket.Reference} is Resolved and cannot be escalated.");
        }

        if (ticket.Status is TicketStatus.Closed)
        {
            throw new BusinessRuleViolationException($"Ticket {ticket.Reference} is Closed and cannot be escalated.");
        }

        if (ticket.Priority == TicketPriority.Critical)
        {
            throw new BusinessRuleViolationException($"Ticket {ticket.Reference} is already at Critical priority and cannot be escalated.");
        }

        // BR-7: Step priority one level
        var newPriority = PriorityPolicy.GetEscalatedPriority(ticket.Priority);

        // Load customer for customer tier SLA adjustment
        var customer = await customers.GetByIdAsync(ticket.CustomerId, ct);
        var tier = customer?.Tier ?? CustomerTier.Standard;

        // BR-7: Recomputes due date from moment of escalation (nowUtc) using BR-2 / BR-3
        var options = slaOptions ?? new SlaOptions();
        var (_, newDueAtUtc, _) = SlaCalculator.CalculateDueDate(newPriority, tier, options, nowUtc);

        // Load category and candidate workloads for agent assignment re-evaluation
        var category = await categories.GetByIdAsync(ticket.CategoryId, ct)
            ?? new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);

        var candidateWorkloads = await agents.GetCandidateWorkloadsAsync(ct);

        // BR-7: Re-evaluate owner: keep current agent if still eligible, else reassign per BR-5
        var (newAgent, _) = AgentAssignmentPolicy.ReevaluateAgentForEscalation(
            ticket.AssignedAgentId, candidateWorkloads, category);

        // G. Escalation actor: obtained from the authenticated user's JWT claims, not trusted from the request body.
        var escalatedBy = (currentUser?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(currentUser.FullName))
            ? currentUser.FullName
            : (!string.IsNullOrWhiteSpace(request.EscalatedBy) ? request.EscalatedBy : "Unknown Agent");

        // Domain method validates invariants and appends immutable TicketEscalation
        var escalation = ticket.Escalate(
            newPriority,
            newDueAtUtc,
            newAgent?.Id,
            request.Reason,
            escalatedBy,
            nowUtc);

        // Persist ticket updates and escalation row atomically
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} escalated from {OldPriority} to {NewPriority} by {EscalatedBy}. Assignee: {OldAgent} -> {NewAgent}.",
            ticket.Reference, escalation.FromPriority, escalation.ToPriority, escalation.EscalatedBy, escalation.FromAgentId, escalation.ToAgentId);

        var updatedTicket = await getTicket.HandleAsync(ticket.Id, ct);

        var escalationDto = new TicketEscalationDto
        {
            Id = escalation.Id,
            TicketId = escalation.TicketId,
            FromPriority = escalation.FromPriority,
            ToPriority = escalation.ToPriority,
            FromAgentId = escalation.FromAgentId,
            ToAgentId = escalation.ToAgentId,
            FromDueAtUtc = escalation.FromDueAtUtc,
            ToDueAtUtc = escalation.ToDueAtUtc,
            Reason = escalation.Reason,
            EscalatedBy = escalation.EscalatedBy,
            EscalatedAtUtc = escalation.EscalatedAtUtc
        };

        return new EscalationResponseDto
        {
            Ticket = updatedTicket,
            Escalation = escalationDto
        };
    }
}
