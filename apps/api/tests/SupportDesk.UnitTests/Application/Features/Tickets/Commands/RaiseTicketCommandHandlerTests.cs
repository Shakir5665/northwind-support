using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class RaiseTicketCommandHandlerTests
{
    private static CreateTicketRequest Request(TicketPriority? priority = null, int customerId = 1, int categoryId = 3) =>
        new("Cannot log in to the portal", "Happens for every user since this morning.", customerId, categoryId, priority);

    [Fact]
    public async Task HandleAsync_StampsReferenceStatusAndTimestamps()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        var created = context.AddedTicket;
        Assert.NotNull(created);
        Assert.Equal("TCK-0041", created.Reference);
        Assert.Equal(TicketStatus.New, created.Status);
        Assert.Equal(TicketPriority.High, created.Priority);
        Assert.Equal(FixedClock.DefaultNow, created.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow, created.UpdatedAtUtc);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WithoutRequestedPriority_FallsBackToMedium()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Medium, context.AddedTicket.Priority);
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryForcesCritical_ForcesPriorityToCritical()
    {
        var context = new TicketCommandTestContext();
        var securityCategory = new Category("Security", requiresSpecialist: true, forcesCriticalPriority: true);
        context.Categories.Setup(c => c.GetByIdAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(securityCategory);

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.Low, categoryId: 9), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Critical, context.AddedTicket.Priority);
        // Standard customer Critical window is 4 hours, NOT Low window of 72 hours
        Assert.Equal(FixedClock.DefaultNow.AddHours(4), context.AddedTicket.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryForcesCriticalAndNoPriorityRequested_ForcesCriticalWithCriticalSla()
    {
        var context = new TicketCommandTestContext();
        var securityCategory = new Category("Security", requiresSpecialist: true, forcesCriticalPriority: true);
        context.Categories.Setup(c => c.GetByIdAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(securityCategory);

        await context.RaiseTicket.HandleAsync(Request(priority: null, categoryId: 9), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Critical, context.AddedTicket.Priority);
        Assert.Equal(FixedClock.DefaultNow.AddHours(4), context.AddedTicket.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_CalculatesSlaDueDate_StandardCustomer()
    {
        var context = new TicketCommandTestContext();
        var standardCustomer = new Customer("Standard Inc", "user@standard.example", null, CustomerTier.Standard, FixedClock.DefaultNow);
        context.Customers.Setup(c => c.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(standardCustomer);

        // Medium priority base window = 24h
        await context.RaiseTicket.HandleAsync(Request(TicketPriority.Medium), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(FixedClock.DefaultNow.AddHours(24), context.AddedTicket.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_CalculatesSlaDueDate_PremiumCustomerHalved()
    {
        var context = new TicketCommandTestContext();
        var premiumCustomer = new Customer("Premium Inc", "user@premium.example", null, CustomerTier.Premium, FixedClock.DefaultNow);
        context.Customers.Setup(c => c.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(premiumCustomer);

        // High priority base window = 8h -> Premium 0.5x = 4h
        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(FixedClock.DefaultNow.AddHours(4), context.AddedTicket.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_AssignsEligibleAgentWithFewestOpenTickets()
    {
        var context = new TicketCommandTestContext();
        var agent1 = new Agent("Alex", "alex@example.com", 10, FixedClock.DefaultNow).WithId(1);
        var agent2 = new Agent("Sara", "sara@example.com", 10, FixedClock.DefaultNow).WithId(2);

        context.Agents.Setup(a => a.GetCandidateWorkloadsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentWorkload>
            {
                new(agent1, OpenTicketCount: 6),
                new(agent2, OpenTicketCount: 2) // Lowest workload
            });

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(agent2.Id, context.AddedTicket.AssignedAgentId);
    }

    [Fact]
    public async Task HandleAsync_WhenNoAgentEligible_Returns201WithNullAssignmentAndReason()
    {
        var context = new TicketCommandTestContext();
        // Billing requires specialist
        var billingCategory = new Category("Billing", requiresSpecialist: true, forcesCriticalPriority: false).WithId(2);
        context.Categories.Setup(c => c.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(billingCategory);

        var nonSpecialist = new Agent("Alex", "alex@example.com", 10, FixedClock.DefaultNow).WithId(1);
        context.Agents.Setup(a => a.GetCandidateWorkloadsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentWorkload>
            {
                new(nonSpecialist, OpenTicketCount: 1)
            });

        var result = await context.RaiseTicket.HandleAsync(Request(categoryId: 2), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Null(context.AddedTicket.AssignedAgentId);
        Assert.NotNull(result.Triage);
        Assert.Contains("specialized", result.Triage.AssignmentReason);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.Customers
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        context.Customers
            .Setup(c => c.ExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Customer", exception.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.Categories
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);
        context.Categories
            .Setup(c => c.ExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Category", exception.Message);
        context.VerifySaved(Times.Never());
    }
}
