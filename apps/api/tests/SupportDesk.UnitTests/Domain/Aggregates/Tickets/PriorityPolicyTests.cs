using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class PriorityPolicyTests
{
    [Theory]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.Medium)]
    [InlineData(TicketPriority.High)]
    [InlineData(TicketPriority.Critical)]
    [InlineData(null)]
    public void DeterminePriority_WhenCategoryForcesCritical_AlwaysReturnsCritical(TicketPriority? requested)
    {
        var category = new Category("Security Incident", requiresSpecialist: true, forcesCriticalPriority: true);

        var (priority, reason) = PriorityPolicy.DeterminePriority(requested, category);

        Assert.Equal(TicketPriority.Critical, priority);
        Assert.Contains("forces Critical priority", reason);
        Assert.Contains("Security Incident", reason);
    }

    [Fact]
    public void DeterminePriority_WhenCategoryDoesNotForceCritical_UsesRequestedPriority()
    {
        var category = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);

        var (priority, reason) = PriorityPolicy.DeterminePriority(TicketPriority.High, category);

        Assert.Equal(TicketPriority.High, priority);
        Assert.Contains("High", reason);
    }

    [Fact]
    public void DeterminePriority_WhenPriorityNotRequested_DefaultsToMedium()
    {
        var category = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);

        var (priority, reason) = PriorityPolicy.DeterminePriority(null, category);

        Assert.Equal(TicketPriority.Medium, priority);
        Assert.Contains("Default priority Medium applied", reason);
    }

    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Medium)]
    [InlineData(TicketPriority.Medium, TicketPriority.High)]
    [InlineData(TicketPriority.High, TicketPriority.Critical)]
    public void GetEscalatedPriority_RaisesPriorityOneLevel(TicketPriority current, TicketPriority expected)
    {
        var next = PriorityPolicy.GetEscalatedPriority(current);

        Assert.Equal(expected, next);
    }

    [Fact]
    public void GetEscalatedPriority_WhenAlreadyCritical_ThrowsBusinessRuleViolation()
    {
        var ex = Assert.Throws<BusinessRuleViolationException>(
            () => PriorityPolicy.GetEscalatedPriority(TicketPriority.Critical));

        Assert.Contains("Critical", ex.Message);
    }
}
