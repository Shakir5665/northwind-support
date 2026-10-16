using SupportDesk.Domain.Aggregates.Customers;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Calculates SLA response windows and due dates based on priority, customer tier, and configurable options (BR-2, BR-3, BR-6).
/// </summary>
public static class SlaCalculator
{
    /// <summary>
    /// Calculates the SLA window duration, the resulting due date from the specified start time,
    /// and a human-readable explanation of how the window was determined.
    /// </summary>
    public static (TimeSpan Window, DateTime DueAtUtc, string Reason) CalculateDueDate(
        TicketPriority priority,
        CustomerTier customerTier,
        SlaOptions options,
        DateTime startUtc)
    {
        ArgumentNullException.ThrowIfNull(options);

        var baseHours = options.GetBaseWindowHours(priority);
        double finalHours;
        string reason;

        if (customerTier == CustomerTier.Premium)
        {
            var reducedHours = baseHours * options.PremiumCustomerMultiplier;
            if (reducedHours < options.MinimumWindowHours)
            {
                finalHours = options.MinimumWindowHours;
                reason = $"Base window of {baseHours}h reduced by {options.PremiumCustomerMultiplier:P0} for Premium customer tier, floored to minimum {options.MinimumWindowHours}h.";
            }
            else
            {
                finalHours = reducedHours;
                reason = $"Base window of {baseHours}h reduced by {options.PremiumCustomerMultiplier:P0} to {finalHours}h for Premium customer tier.";
            }
        }
        else
        {
            finalHours = baseHours;
            reason = $"Base window of {baseHours}h applied for {priority} priority.";
        }

        var window = TimeSpan.FromHours(finalHours);
        var dueAtUtc = startUtc.Add(window);

        return (window, dueAtUtc, reason);
    }
}
