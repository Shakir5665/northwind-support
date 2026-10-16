using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class TicketEscalationTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;
    private static readonly DateTime Later = Now.AddHours(2);

    [Fact]
    public void Escalate_RaisesPriorityAndCreatesImmutableHistoryRow()
    {
        var ticket = new TicketBuilder()
            .WithPriority(TicketPriority.Medium)
            .AssignedTo(1)
            .Build();

        var escalation = ticket.Escalate(
            newPriority: TicketPriority.High,
            newDueAtUtc: Later,
            newAssignedAgentId: 2,
            reason: "Customer reported multiple users blocked.",
            escalatedBy: "lead@northwind-support.example",
            nowUtc: Now);

        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(Later, ticket.DueAtUtc);
        Assert.Equal(2, ticket.AssignedAgentId);
        Assert.Equal(Now, ticket.UpdatedAtUtc);

        Assert.Single(ticket.Escalations);
        Assert.Same(escalation, ticket.Escalations.First());
        Assert.Equal(TicketPriority.Medium, escalation.FromPriority);
        Assert.Equal(TicketPriority.High, escalation.ToPriority);
        Assert.Equal(1, escalation.FromAgentId);
        Assert.Equal(2, escalation.ToAgentId);
        Assert.Equal("Customer reported multiple users blocked.", escalation.Reason);
        Assert.Equal("lead@northwind-support.example", escalation.EscalatedBy);
        Assert.Equal(Now, escalation.EscalatedAtUtc);
    }

    [Fact]
    public void Escalate_WhenTicketIsCritical_ThrowsBusinessRuleViolation()
    {
        var ticket = new TicketBuilder()
            .WithReference("TCK-0099")
            .WithPriority(TicketPriority.Critical)
            .Build();

        var ex = Assert.Throws<BusinessRuleViolationException>(
            () => ticket.Escalate(TicketPriority.Critical, Later, 1, "Valid reason here", "lead", Now));

        Assert.Contains("TCK-0099", ex.Message);
        Assert.Contains("Critical", ex.Message);
    }

    [Fact]
    public void Escalate_WhenTicketIsResolved_ThrowsBusinessRuleViolation()
    {
        var ticket = new TicketBuilder()
            .WithReference("TCK-0088")
            .WithStatus(TicketStatus.Resolved)
            .Build();

        var ex = Assert.Throws<BusinessRuleViolationException>(
            () => ticket.Escalate(TicketPriority.High, Later, 1, "Valid reason here", "lead", Now));

        Assert.Contains("TCK-0088", ex.Message);
        Assert.Contains("Resolved", ex.Message);
    }

    [Fact]
    public void Escalate_WhenTicketIsClosed_ThrowsBusinessRuleViolation()
    {
        var ticket = new TicketBuilder()
            .WithReference("TCK-0077")
            .WithStatus(TicketStatus.Closed)
            .Build();

        var ex = Assert.Throws<BusinessRuleViolationException>(
            () => ticket.Escalate(TicketPriority.High, Later, 1, "Valid reason here", "lead", Now));

        Assert.Contains("TCK-0077", ex.Message);
        Assert.Contains("Closed", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("four")]        // 4 characters (min is 5)
    public void Escalate_WhenReasonIsTooShort_ThrowsBusinessRuleViolation(string shortReason)
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.Low).Build();

        var ex = Assert.Throws<BusinessRuleViolationException>(
            () => ticket.Escalate(TicketPriority.Medium, Later, 1, shortReason, "lead", Now));

        Assert.Contains("5 and 500 characters", ex.Message);
    }

    [Fact]
    public void Escalate_WhenReasonIsTooLong_ThrowsBusinessRuleViolation()
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.Low).Build();
        var longReason = new string('x', 501);

        var ex = Assert.Throws<BusinessRuleViolationException>(
            () => ticket.Escalate(TicketPriority.Medium, Later, 1, longReason, "lead", Now));

        Assert.Contains("5 and 500 characters", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Escalate_WhenEscalatedByIsMissing_ThrowsBusinessRuleViolation(string emptyEscalatedBy)
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.Low).Build();

        var ex = Assert.Throws<BusinessRuleViolationException>(
            () => ticket.Escalate(TicketPriority.Medium, Later, 1, "Valid reason here", emptyEscalatedBy, Now));

        Assert.Contains("EscalatedBy is required", ex.Message);
    }

    [Fact]
    public void Escalate_WhenTicketIsNewStatus_Succeeds()
    {
        var ticket = new TicketBuilder()
            .WithStatus(TicketStatus.New)
            .WithPriority(TicketPriority.Low)
            .Build();

        ticket.Escalate(TicketPriority.Medium, Later, 1, "Early escalation requested", "Supervisor", Now);

        Assert.Equal(TicketPriority.Medium, ticket.Priority);
        Assert.Single(ticket.Escalations);
    }

    [Fact]
    public void Escalate_MultipleSequentialEscalations_RecordsFullAuditTrailInOrder()
    {
        var ticket = new TicketBuilder()
            .WithPriority(TicketPriority.Low)
            .AssignedTo(1)
            .Build();

        var t1 = Now;
        var t2 = Now.AddHours(2);
        var t3 = Now.AddHours(4);

        // 1st escalation: Low -> Medium
        ticket.Escalate(TicketPriority.Medium, t1.AddHours(24), 1, "First escalation reason", "Lead 1", t1);
        // 2nd escalation: Medium -> High
        ticket.Escalate(TicketPriority.High, t2.AddHours(8), 2, "Second escalation reason", "Lead 2", t2);
        // 3rd escalation: High -> Critical
        ticket.Escalate(TicketPriority.Critical, t3.AddHours(4), 3, "Third escalation reason", "Lead 3", t3);

        Assert.Equal(TicketPriority.Critical, ticket.Priority);
        Assert.Equal(3, ticket.AssignedAgentId);
        Assert.Equal(3, ticket.Escalations.Count);

        var escalations = ticket.Escalations.ToList();
        Assert.Equal(TicketPriority.Low, escalations[0].FromPriority);
        Assert.Equal(TicketPriority.Medium, escalations[0].ToPriority);
        Assert.Equal(t1, escalations[0].EscalatedAtUtc);

        Assert.Equal(TicketPriority.Medium, escalations[1].FromPriority);
        Assert.Equal(TicketPriority.High, escalations[1].ToPriority);
        Assert.Equal(t2, escalations[1].EscalatedAtUtc);

        Assert.Equal(TicketPriority.High, escalations[2].FromPriority);
        Assert.Equal(TicketPriority.Critical, escalations[2].ToPriority);
        Assert.Equal(t3, escalations[2].EscalatedAtUtc);

        // Cannot escalate further once Critical
        Assert.Throws<BusinessRuleViolationException>(
            () => ticket.Escalate(TicketPriority.Critical, t3.AddHours(4), 3, "Fourth escalation attempt", "Lead 4", t3.AddHours(1)));
    }
}
