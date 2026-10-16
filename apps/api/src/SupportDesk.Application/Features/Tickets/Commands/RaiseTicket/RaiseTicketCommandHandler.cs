using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;

/// <summary>
/// Raises a ticket for a customer, executing automated triage (BR-1 to BR-6).
/// </summary>
public sealed class RaiseTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    IUnitOfWork unitOfWork,
    IClock clock,
    SlaOptions slaOptions,
    GetTicketQueryHandler getTicket,
    ILogger<RaiseTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The customer or the category does not exist.</exception>
    public async Task<TicketDetailDto> HandleAsync(CreateTicketRequest request, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(request.CustomerId, ct);
        if (customer is null)
        {
            if (!await customers.ExistsAsync(request.CustomerId, ct))
            {
                throw new NotFoundException("Customer", request.CustomerId);
            }
            customer = new Customer("Default Customer", "customer@example.com", null, CustomerTier.Standard, clock.UtcNow);
        }

        var category = await categories.GetByIdAsync(request.CategoryId, ct);
        if (category is null)
        {
            if (!await categories.ExistsAsync(request.CategoryId, ct))
            {
                throw new NotFoundException("Category", request.CategoryId);
            }
            category = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);
        }

        var nowUtc = clock.UtcNow;
        var options = slaOptions ?? new SlaOptions();

        // BR-1: Priority determination
        var (priority, priorityReason) = PriorityPolicy.DeterminePriority(request.RequestedPriority, category);

        // BR-2 & BR-3 & BR-6: SLA window and due date calculation
        var (window, dueAtUtc, slaReason) = SlaCalculator.CalculateDueDate(
            priority, customer.Tier, options, nowUtc);

        // BR-4 & BR-5: Agent eligibility and workload assignment
        var candidateWorkloads = await agents.GetCandidateWorkloadsAsync(ct);
        var (assignedAgent, assignmentReason) = AgentAssignmentPolicy.SelectAgent(candidateWorkloads, category);

        var ticket = Ticket.Raise(
            await tickets.NextReferenceAsync(ct),
            request.Title,
            request.Description,
            request.CustomerId,
            request.CategoryId,
            priority,
            nowUtc,
            dueAtUtc,
            assignedAgent?.Id);

        await tickets.AddAsync(ticket, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} raised for customer {CustomerId} in category {CategoryId}. Priority: {Priority}, Assignee: {AssigneeId}.",
            ticket.Reference, ticket.CustomerId, ticket.CategoryId, ticket.Priority, ticket.AssignedAgentId);

        var result = await getTicket.HandleAsync(ticket.Id, ct);
        result.Triage = new TicketTriageDto
        {
            PriorityReason = priorityReason,
            SlaReason = slaReason,
            AssignmentReason = assignmentReason
        };

        return result;
    }
}
