using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicketEscalations;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Queries;

public class GetTicketEscalationsQueryHandlerTests
{
    private readonly Mock<ITicketQueries> _queries = new();
    private readonly GetTicketEscalationsQueryHandler _handler;

    public GetTicketEscalationsQueryHandlerTests()
    {
        _handler = new GetTicketEscalationsQueryHandler(_queries.Object);
    }

    [Fact]
    public async Task HandleAsync_UnknownTicket_ThrowsNotFoundException()
    {
        _queries.Setup(q => q.GetEscalationsAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TicketEscalationDto>?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.HandleAsync(999, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ExistingTicketWithNoEscalations_ReturnsEmptyList()
    {
        _queries.Setup(q => q.GetEscalationsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TicketEscalationDto>());

        var result = await _handler.HandleAsync(42, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_TicketWithEscalations_ReturnsEscalations()
    {
        var escalations = new List<TicketEscalationDto>
        {
            new()
            {
                Id = 2,
                TicketId = 42,
                FromPriority = TicketPriority.Medium,
                ToPriority = TicketPriority.High,
                FromAgentId = 1,
                ToAgentId = 2,
                FromDueAtUtc = DateTime.UtcNow.AddHours(20),
                ToDueAtUtc = DateTime.UtcNow.AddHours(8),
                Reason = "Second escalation",
                EscalatedBy = "Alice",
                EscalatedAtUtc = DateTime.UtcNow
            },
            new()
            {
                Id = 1,
                TicketId = 42,
                FromPriority = TicketPriority.Low,
                ToPriority = TicketPriority.Medium,
                FromAgentId = 1,
                ToAgentId = 1,
                FromDueAtUtc = DateTime.UtcNow.AddHours(48),
                ToDueAtUtc = DateTime.UtcNow.AddHours(20),
                Reason = "First escalation",
                EscalatedBy = "Bob",
                EscalatedAtUtc = DateTime.UtcNow.AddHours(-2)
            }
        };

        _queries.Setup(q => q.GetEscalationsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(escalations);

        var result = await _handler.HandleAsync(42, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Id);
        Assert.Equal("Second escalation", result[0].Reason);
        Assert.Equal(1, result[1].Id);
        Assert.Equal("First escalation", result[1].Reason);
    }
}
