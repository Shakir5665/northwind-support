using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class SlaEvaluatorTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4); // 240 min, 25% threshold = 60 min

    [Fact]
    public void Evaluate_WithoutADueDate_IsNotApplicable() =>
        Assert.Equal(SlaStatus.NotApplicable, SlaEvaluator.Evaluate(null, null, Now));

    [Fact]
    public void Evaluate_ResolvedOnTime_IsMet() =>
        Assert.Equal(SlaStatus.Met, SlaEvaluator.Evaluate(Now, Now, Now.AddDays(1)));

    [Fact]
    public void Evaluate_ResolvedBeforeDue_IsMet() =>
        Assert.Equal(SlaStatus.Met, SlaEvaluator.Evaluate(Now, Now.AddMinutes(-5), Now.AddDays(1)));

    [Fact]
    public void Evaluate_ResolvedLate_IsBreached() =>
        Assert.Equal(SlaStatus.Breached, SlaEvaluator.Evaluate(Now, Now.AddMinutes(1), Now));

    [Fact]
    public void Evaluate_OpenAndPastDue_IsBreached() =>
        Assert.Equal(SlaStatus.Breached, SlaEvaluator.Evaluate(Now.AddMinutes(-1), null, Now));

    [Fact]
    public void Evaluate_OpenAndBeforeDue_WhenWindowNotSpecified_IsWithinSla() =>
        Assert.Equal(SlaStatus.WithinSla, SlaEvaluator.Evaluate(Now.AddHours(1), null, Now));

    [Fact]
    public void Evaluate_WhenRemainingTimeIsMoreThan25PercentOfWindow_IsWithinSla()
    {
        // Due in 61 minutes with a 4-hour window (threshold is 60 minutes). 61 > 60 -> WithinSla
        var dueAtUtc = Now.AddMinutes(61);
        var status = SlaEvaluator.Evaluate(dueAtUtc, null, Now, FourHours);

        Assert.Equal(SlaStatus.WithinSla, status);
    }

    [Fact]
    public void Evaluate_WhenRemainingTimeIsExactly25PercentOfWindow_IsAtRisk()
    {
        // Due in exactly 60 minutes with a 4-hour window (threshold is 60 minutes). 60 <= 60 -> AtRisk
        var dueAtUtc = Now.AddMinutes(60);
        var status = SlaEvaluator.Evaluate(dueAtUtc, null, Now, FourHours);

        Assert.Equal(SlaStatus.AtRisk, status);
    }

    [Fact]
    public void Evaluate_WhenRemainingTimeIsLessThan25PercentOfWindow_IsAtRisk()
    {
        // Due in 30 minutes with a 4-hour window. 30 <= 60 -> AtRisk
        var dueAtUtc = Now.AddMinutes(30);
        var status = SlaEvaluator.Evaluate(dueAtUtc, null, Now, FourHours);

        Assert.Equal(SlaStatus.AtRisk, status);
    }

    [Fact]
    public void Evaluate_WhenNowIsExactlyAtDueDate_IsAtRisk()
    {
        // Due right now (0 seconds remaining). 0 <= 60 -> AtRisk (not yet breached since nowUtc <= dueAtUtc)
        var dueAtUtc = Now;
        var status = SlaEvaluator.Evaluate(dueAtUtc, null, Now, FourHours);

        Assert.Equal(SlaStatus.AtRisk, status);
    }

    [Fact]
    public void Evaluate_WhenNowIsJustPastDueDate_IsBreached()
    {
        // Due 1 millisecond ago
        var dueAtUtc = Now.AddMilliseconds(-1);
        var status = SlaEvaluator.Evaluate(dueAtUtc, null, Now, FourHours);

        Assert.Equal(SlaStatus.Breached, status);
    }

    [Fact]
    public void Evaluate_UsingWindowStartUtc_DerivesAtRiskCorrectly()
    {
        var startUtc = Now.AddHours(-3); // Created 3 hours ago
        var dueAtUtc = Now.AddHours(1);  // Due in 1 hour -> Total window = 4 hours. Remaining = 1 hour (25%) -> AtRisk
        var status = SlaEvaluator.Evaluate(dueAtUtc, null, Now, startUtc);

        Assert.Equal(SlaStatus.AtRisk, status);
    }

    [Fact]
    public void Evaluate_ResolvedAtExactDueDate_ReturnsMet()
    {
        var dueAtUtc = Now;
        var resolvedAtUtc = Now; // Exactly on due date
        var status = SlaEvaluator.Evaluate(dueAtUtc, resolvedAtUtc, Now.AddDays(1), FourHours);

        Assert.Equal(SlaStatus.Met, status);
    }

    [Fact]
    public void Evaluate_ResolvedOneTickAfterDueDate_ReturnsBreached()
    {
        var dueAtUtc = Now;
        var resolvedAtUtc = Now.AddTicks(1); // 1 tick late
        var status = SlaEvaluator.Evaluate(dueAtUtc, resolvedAtUtc, Now.AddDays(1), FourHours);

        Assert.Equal(SlaStatus.Breached, status);
    }

    [Fact]
    public void Evaluate_UnresolvedOneTickPastDueDate_ReturnsBreached()
    {
        var dueAtUtc = Now;
        var nowUtc = Now.AddTicks(1); // 1 tick past due
        var status = SlaEvaluator.Evaluate(dueAtUtc, null, nowUtc, FourHours);

        Assert.Equal(SlaStatus.Breached, status);
    }

    [Fact]
    public void Evaluate_AtRiskThreshold_ExactSubSecondBoundaries()
    {
        var window = TimeSpan.FromSeconds(1000); // 25% threshold is 250s
        var baseTime = Now;

        // Just above 25% boundary (250.001s remaining) -> WithinSla
        var dueJustAbove = baseTime.AddSeconds(250.001);
        Assert.Equal(SlaStatus.WithinSla, SlaEvaluator.Evaluate(dueJustAbove, null, baseTime, window));

        // Exact 25% boundary (250.000s remaining) -> AtRisk
        var dueExact = baseTime.AddSeconds(250);
        Assert.Equal(SlaStatus.AtRisk, SlaEvaluator.Evaluate(dueExact, null, baseTime, window));

        // Just below 25% boundary (249.999s remaining) -> AtRisk
        var dueJustBelow = baseTime.AddSeconds(249.999);
        Assert.Equal(SlaStatus.AtRisk, SlaEvaluator.Evaluate(dueJustBelow, null, baseTime, window));
    }

    [Fact]
    public void Evaluate_NullDueDateWithResolvedDate_ReturnsNotApplicable()
    {
        var status = SlaEvaluator.Evaluate(null, Now.AddHours(-1), Now);
        Assert.Equal(SlaStatus.NotApplicable, status);
    }

    [Fact]
    public void Evaluate_ZeroOrNegativeWindow_ReturnsWithinSlaIfBeforeDue()
    {
        var dueAtUtc = Now.AddHours(2);
        // Zero window
        Assert.Equal(SlaStatus.WithinSla, SlaEvaluator.Evaluate(dueAtUtc, null, Now, TimeSpan.Zero));
        // Negative window
        Assert.Equal(SlaStatus.WithinSla, SlaEvaluator.Evaluate(dueAtUtc, null, Now, TimeSpan.FromHours(-1)));
    }
}
