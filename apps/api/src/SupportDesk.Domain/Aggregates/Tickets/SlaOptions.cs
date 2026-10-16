namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Configuration options for SLA window and threshold calculations (BR-6).
/// Matches the "Sla" section in configuration.
/// </summary>
public sealed class SlaOptions
{
    public const string SectionName = "Sla";

    /// <summary>
    /// Base SLA window in hours by priority name (Low: 72, Medium: 24, High: 8, Critical: 4).
    /// </summary>
    public Dictionary<string, int> BaseWindowHours { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(TicketPriority.Low)] = 72,
        [nameof(TicketPriority.Medium)] = 24,
        [nameof(TicketPriority.High)] = 8,
        [nameof(TicketPriority.Critical)] = 4
    };

    /// <summary>
    /// Multiplier applied for Premium customer tier (default 0.5).
    /// </summary>
    public double PremiumCustomerMultiplier { get; set; } = 0.5;

    /// <summary>
    /// Minimum SLA window in hours (default 1 hour).
    /// </summary>
    public int MinimumWindowHours { get; set; } = 1;

    /// <summary>
    /// Percentage threshold of the window remaining to mark SLA status AtRisk (default 25%).
    /// </summary>
    public int AtRiskThresholdPercent { get; set; } = 25;

    /// <summary>
    /// Retrieves the base window in hours for the given priority, falling back to standard defaults if not mapped.
    /// </summary>
    public int GetBaseWindowHours(TicketPriority priority)
    {
        if (BaseWindowHours.TryGetValue(priority.ToString(), out var hours))
        {
            return hours;
        }

        return priority switch
        {
            TicketPriority.Critical => 4,
            TicketPriority.High => 8,
            TicketPriority.Medium => 24,
            TicketPriority.Low => 72,
            _ => 24
        };
    }
}
