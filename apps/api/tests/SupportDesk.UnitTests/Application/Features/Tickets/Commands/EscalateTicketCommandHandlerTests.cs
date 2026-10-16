using FluentValidation;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class EscalateTicketCommandHandlerTests
{
    private readonly TicketCommandTestContext _context = new();

    private Ticket CreateTicket(
        TicketPriority priority = TicketPriority.Low,
        TicketStatus status = TicketStatus.Open,
        int? assignedAgentId = 10,
        DateTime? dueAtUtc = null)
    {
        var ticket = Ticket.Raise(
            "TCK-1001",
            "Sample Ticket Title",
            "Sample description with enough length",
            1,
            1,
            priority,
            _context.Clock.UtcNow,
            dueAtUtc ?? _context.Clock.UtcNow.AddHours(48),
            assignedAgentId);

        if (status != TicketStatus.New)
        {
            ticket.ChangeStatus(status, _context.Clock.UtcNow);
        }

        return ticket;
    }

    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Medium)]
    [InlineData(TicketPriority.Medium, TicketPriority.High)]
    [InlineData(TicketPriority.High, TicketPriority.Critical)]
    public async Task HandleAsync_ValidPriorityTransition_BumpsPriorityByOneStep(
        TicketPriority initialPriority,
        TicketPriority expectedPriority)
    {
        var ticket = CreateTicket(priority: initialPriority);
        _context.ExistingTicket(ticket);

        var request = new EscalateTicketRequest("Customer needs urgent update", "Manager Alice");
        var result = await _context.EscalateTicket.HandleAsync(ticket.Id, request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Escalation);
        Assert.Equal(initialPriority, result.Escalation.FromPriority);
        Assert.Equal(expectedPriority, result.Escalation.ToPriority);
        Assert.Equal(expectedPriority, ticket.Priority);
        _context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_RecomputesDueDate_FromEscalationTimeWithPremiumMultiplier()
    {
        var ticket = CreateTicket(priority: TicketPriority.Medium);
        _context.ExistingTicket(ticket);

        // Premium customer gets configured multiplier (0.5). For High priority base window = 8h, 8 * 0.5 = 4h
        _context.Customers.Setup(c => c.GetByIdAsync(ticket.CustomerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer("Premium Corp", "contact@premium.com", null, CustomerTier.Premium, _context.Clock.UtcNow));

        var request = new EscalateTicketRequest("Escalating due to delay", "Support Lead");
        var result = await _context.EscalateTicket.HandleAsync(ticket.Id, request, CancellationToken.None);

        var expectedDueAtUtc = _context.Clock.UtcNow.AddHours(8 * _context.SlaOptions.PremiumCustomerMultiplier);
        Assert.Equal(expectedDueAtUtc, result.Escalation.ToDueAtUtc);
        Assert.Equal(expectedDueAtUtc, ticket.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_CurrentAgentRemainsEligible_RetainsCurrentAgent()
    {
        var ticket = CreateTicket(priority: TicketPriority.Low, assignedAgentId: 10);
        _context.ExistingTicket(ticket);

        var agent10 = new Agent("Agent Ten", "ten@northwind.com", maxOpenTickets: 10, _context.Clock.UtcNow).WithId(10);
        var agent20 = new Agent("Agent Twenty", "twenty@northwind.com", maxOpenTickets: 10, _context.Clock.UtcNow).WithId(20);

        // Agent 10 is active, under capacity, has 3 open tickets. Agent 20 has 1 ticket.
        // Even though agent 20 has fewer tickets, BR-7 specifies: "Retain the current agent if they remain eligible."
        var workloads = new List<AgentWorkload>
        {
            new(agent10, 3),
            new(agent20, 1)
        };
        _context.Agents.Setup(a => a.GetCandidateWorkloadsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(workloads);

        var request = new EscalateTicketRequest("Valid escalation reason", "Lead Bob");
        var result = await _context.EscalateTicket.HandleAsync(ticket.Id, request, CancellationToken.None);

        Assert.Equal(10, result.Escalation.FromAgentId);
        Assert.Equal(10, result.Escalation.ToAgentId);
        Assert.Equal(10, ticket.AssignedAgentId);
    }

    [Fact]
    public async Task HandleAsync_CurrentAgentNoLongerEligible_ReassignsToBestEligibleAgent()
    {
        var ticket = CreateTicket(priority: TicketPriority.Low, assignedAgentId: 10);
        _context.ExistingTicket(ticket);

        // Category now requires specialist
        var specCategory = new Category("Security", requiresSpecialist: true, forcesCriticalPriority: false);
        _context.Categories.Setup(c => c.GetByIdAsync(ticket.CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(specCategory);

        var agent10NonSpec = new Agent("Agent Ten", "ten@northwind.com", maxOpenTickets: 10, _context.Clock.UtcNow).WithId(10);
        var agent20Spec = new Agent("Agent Twenty", "twenty@northwind.com", maxOpenTickets: 10, _context.Clock.UtcNow).WithId(20);
        agent20Spec.AddSpecialization(specCategory.Id);

        var workloads = new List<AgentWorkload>
        {
            new(agent10NonSpec, 1),
            new(agent20Spec, 2)
        };
        _context.Agents.Setup(a => a.GetCandidateWorkloadsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(workloads);

        var request = new EscalateTicketRequest("Category requires specialist", "Supervisor");
        var result = await _context.EscalateTicket.HandleAsync(ticket.Id, request, CancellationToken.None);

        Assert.Equal(10, result.Escalation.FromAgentId);
        Assert.Equal(20, result.Escalation.ToAgentId);
        Assert.Equal(20, ticket.AssignedAgentId);
    }

    [Fact]
    public async Task HandleAsync_NoAgentQualifies_SetsTicketUnassigned()
    {
        var ticket = CreateTicket(priority: TicketPriority.Low, assignedAgentId: 10);
        _context.ExistingTicket(ticket);

        // All agents at capacity
        var agent = new Agent("Agent Busy", "busy@northwind.com", maxOpenTickets: 5, _context.Clock.UtcNow);
        var workloads = new List<AgentWorkload>
        {
            new(agent, 5)
        };
        _context.Agents.Setup(a => a.GetCandidateWorkloadsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(workloads);

        var request = new EscalateTicketRequest("Escalation without available agents", "Ops Lead");
        var result = await _context.EscalateTicket.HandleAsync(ticket.Id, request, CancellationToken.None);

        Assert.Equal(10, result.Escalation.FromAgentId);
        Assert.Null(result.Escalation.ToAgentId);
        Assert.Null(ticket.AssignedAgentId);
    }

    [Fact]
    public async Task HandleAsync_UnknownTicket_ThrowsNotFoundException()
    {
        _context.Tickets.Setup(t => t.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Ticket?)null);

        var request = new EscalateTicketRequest("Escalation for missing ticket", "Alice");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _context.EscalateTicket.HandleAsync(999, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_CriticalTicket_ThrowsBusinessRuleViolationException()
    {
        var ticket = CreateTicket(priority: TicketPriority.Critical);
        _context.ExistingTicket(ticket);

        var request = new EscalateTicketRequest("Cannot escalate critical", "Alice");

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            _context.EscalateTicket.HandleAsync(ticket.Id, request, CancellationToken.None));

        Assert.Contains("Critical", ex.Message);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task HandleAsync_ResolvedOrClosedTicket_ThrowsBusinessRuleViolationException(TicketStatus status)
    {
        var ticket = CreateTicket(priority: TicketPriority.Medium, status: status);
        _context.ExistingTicket(ticket);

        var request = new EscalateTicketRequest("Cannot escalate resolved or closed", "Alice");

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            _context.EscalateTicket.HandleAsync(ticket.Id, request, CancellationToken.None));

        Assert.Contains(status.ToString(), ex.Message);
    }

    [Fact]
    public async Task HandleAsync_RecordsAccurateEscalationAuditData()
    {
        var initialDue = _context.Clock.UtcNow.AddHours(24);
        var ticket = CreateTicket(priority: TicketPriority.Low, assignedAgentId: 5, dueAtUtc: initialDue);
        _context.ExistingTicket(ticket);

        var request = new EscalateTicketRequest("Customer requested immediate supervisor review", "Support Lead Charlie");
        var result = await _context.EscalateTicket.HandleAsync(ticket.Id, request, CancellationToken.None);

        var escalation = result.Escalation;
        Assert.Equal(ticket.Id, escalation.TicketId);
        Assert.Equal(TicketPriority.Low, escalation.FromPriority);
        Assert.Equal(TicketPriority.Medium, escalation.ToPriority);
        Assert.Equal(5, escalation.FromAgentId);
        Assert.Equal(initialDue, escalation.FromDueAtUtc);
        Assert.Equal("Customer requested immediate supervisor review", escalation.Reason);
        Assert.Equal("Support Lead Charlie", escalation.EscalatedBy);
        Assert.Equal(_context.Clock.UtcNow, escalation.EscalatedAtUtc);
        Assert.Single(ticket.Escalations);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("four")] // 4 characters, minimum is 5
    public void Validator_InvalidReason_FailsValidation(string reason)
    {
        var validator = new EscalateTicketRequestValidator();
        var request = new EscalateTicketRequest(reason, "Valid Actor");
        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Reason));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validator_MissingActor_FailsValidation(string actor)
    {
        var validator = new EscalateTicketRequestValidator();
        var request = new EscalateTicketRequest("Valid escalation reason", actor);
        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.EscalatedBy));
    }

    [Fact]
    public void Validator_BoundaryLengths_EnforcesExactMinAndMax()
    {
        var validator = new EscalateTicketRequestValidator();

        // 5 characters -> valid
        var validMin = new EscalateTicketRequest("12345", "Valid Actor");
        Assert.True(validator.Validate(validMin).IsValid);

        // 4 characters -> invalid
        var invalidMin = new EscalateTicketRequest("1234", "Valid Actor");
        Assert.False(validator.Validate(invalidMin).IsValid);

        // 500 characters -> valid
        var validMax = new EscalateTicketRequest(new string('a', 500), "Valid Actor");
        Assert.True(validator.Validate(validMax).IsValid);

        // 501 characters -> invalid
        var invalidMax = new EscalateTicketRequest(new string('a', 501), "Valid Actor");
        Assert.False(validator.Validate(invalidMax).IsValid);
    }

    [Fact]
    public async Task HandleAsync_WhenClockAdvanced_CalculatesDueDateStrictlyFromCurrentInjectedTime()
    {
        var createdAt = _context.Clock.UtcNow;
        var initialDue = createdAt.AddHours(24);
        var ticket = CreateTicket(priority: TicketPriority.Medium, dueAtUtc: initialDue);
        _context.ExistingTicket(ticket);

        // Advance the injected clock by 10 hours
        _context.Clock.Advance(TimeSpan.FromHours(10));
        var escalationTime = _context.Clock.UtcNow;

        // Standard customer escalating Medium -> High (High base window = 8 hours)
        var request = new EscalateTicketRequest("Escalation after 10 hours delay", "Supervisor");
        var result = await _context.EscalateTicket.HandleAsync(ticket.Id, request, CancellationToken.None);

        var expectedDue = escalationTime.AddHours(8);
        Assert.Equal(expectedDue, result.Escalation.ToDueAtUtc);
        Assert.Equal(expectedDue, ticket.DueAtUtc);
        Assert.NotEqual(initialDue.AddHours(8), ticket.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_WhenAuthenticatedUserPresent_RecordsAuthenticatedUserInEscalationHistory_IgnoringForgedActor()
    {
        var ticket = CreateTicket(priority: TicketPriority.Low);
        _context.ExistingTicket(ticket);

        var currentUserMock = new Moq.Mock<SupportDesk.Application.Abstractions.ICurrentUser>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.FullName).Returns("Authenticated Agent Priya");

        var handlerWithAuth = new EscalateTicketCommandHandler(
            _context.Tickets.Object,
            _context.Customers.Object,
            _context.Categories.Object,
            _context.Agents.Object,
            _context.UnitOfWork.Object,
            _context.Clock,
            _context.SlaOptions,
            new SupportDesk.Application.Features.Tickets.Queries.GetTicket.GetTicketQueryHandler(_context.TicketQueries.Object),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EscalateTicketCommandHandler>.Instance,
            currentUserMock.Object);

        var request = new EscalateTicketRequest("Valid escalation reason", "Forged Hacker Name");
        var result = await handlerWithAuth.HandleAsync(ticket.Id, request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Escalation);
        Assert.Equal("Authenticated Agent Priya", result.Escalation.EscalatedBy);
        Assert.NotEqual("Forged Hacker Name", result.Escalation.EscalatedBy);
    }
}
