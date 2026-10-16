using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Exceptions;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Implements business rules for ticket priority determination (BR-1) and escalation increment (BR-7).
/// </summary>
public static class PriorityPolicy
{
    /// <summary>
    /// Determines the priority for a ticket based on category rules and requested priority.
    /// Category with ForcesCriticalPriority is always Critical (BR-1).
    /// </summary>
    public static (TicketPriority Priority, string Reason) DeterminePriority(
        TicketPriority? requestedPriority,
        Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (category.ForcesCriticalPriority)
        {
            return (TicketPriority.Critical, $"Category '{category.Name}' forces Critical priority.");
        }

        if (requestedPriority.HasValue)
        {
            return (requestedPriority.Value, $"Priority set to {requestedPriority.Value} as requested.");
        }

        return (TicketPriority.Medium, "Default priority Medium applied as no priority was requested.");
    }

    /// <summary>
    /// Computes the next priority level during ticket escalation (BR-7).
    /// Raises priority by one level.
    /// </summary>
    /// <exception cref="BusinessRuleViolationException">Ticket is already Critical.</exception>
    public static TicketPriority GetEscalatedPriority(TicketPriority currentPriority)
    {
        return currentPriority switch
        {
            TicketPriority.Low => TicketPriority.Medium,
            TicketPriority.Medium => TicketPriority.High,
            TicketPriority.High => TicketPriority.Critical,
            TicketPriority.Critical => throw new BusinessRuleViolationException(
                "Ticket is already at Critical priority and cannot be escalated."),
            _ => throw new ArgumentOutOfRangeException(nameof(currentPriority), currentPriority, "Invalid ticket priority.")
        };
    }
}
