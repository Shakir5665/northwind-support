namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Works out where a ticket stands against its due date (BR-8). SLA status is always derived from
/// the ticket's dates and the current time - never stored.
/// </summary>
public static class SlaEvaluator
{
    public const double DefaultAtRiskThresholdPercent = 25.0;

    /// <summary>
    /// Backward-compatible evaluation when total window duration is not specified.
    /// </summary>
    public static SlaStatus Evaluate(DateTime? dueAtUtc, DateTime? resolvedAtUtc, DateTime nowUtc) =>
        Evaluate(dueAtUtc, resolvedAtUtc, nowUtc, (TimeSpan?)null, DefaultAtRiskThresholdPercent);

    /// <summary>
    /// Evaluates SLA status using the window start timestamp (e.g. CreatedAtUtc or EscalatedAtUtc).
    /// </summary>
    public static SlaStatus Evaluate(
        DateTime? dueAtUtc,
        DateTime? resolvedAtUtc,
        DateTime nowUtc,
        DateTime? windowStartUtc,
        double atRiskThresholdPercent = DefaultAtRiskThresholdPercent)
    {
        TimeSpan? windowDuration = dueAtUtc.HasValue && windowStartUtc.HasValue
            ? dueAtUtc.Value - windowStartUtc.Value
            : null;

        return Evaluate(dueAtUtc, resolvedAtUtc, nowUtc, windowDuration, atRiskThresholdPercent);
    }

    /// <summary>
    /// Evaluates SLA status using the total SLA window duration.
    /// Unresolved tickets with atRiskThresholdPercent (default 25%) or less of the window remaining are AtRisk (BR-8).
    /// </summary>
    public static SlaStatus Evaluate(
        DateTime? dueAtUtc,
        DateTime? resolvedAtUtc,
        DateTime nowUtc,
        TimeSpan? windowDuration,
        double atRiskThresholdPercent = DefaultAtRiskThresholdPercent)
    {
        if (dueAtUtc is null)
        {
            return SlaStatus.NotApplicable;
        }

        if (resolvedAtUtc is not null)
        {
            return resolvedAtUtc <= dueAtUtc ? SlaStatus.Met : SlaStatus.Breached;
        }

        if (nowUtc > dueAtUtc)
        {
            return SlaStatus.Breached;
        }

        if (windowDuration.HasValue && windowDuration.Value > TimeSpan.Zero)
        {
            var remaining = dueAtUtc.Value - nowUtc;
            var thresholdSeconds = windowDuration.Value.TotalSeconds * (atRiskThresholdPercent / 100.0);

            if (remaining.TotalSeconds <= thresholdSeconds)
            {
                return SlaStatus.AtRisk;
            }
        }

        return SlaStatus.WithinSla;
    }
}
