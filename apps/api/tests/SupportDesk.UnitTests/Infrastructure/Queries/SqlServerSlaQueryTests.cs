using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;
using Xunit;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class SqlServerSlaQueryTests
{
    private const string ConnectionString =
        "Server=localhost,1433;Database=SupportDesk;User Id=sa;Password=Local_Dev_Password1;TrustServerCertificate=True;Encrypt=False";

    [Fact]
    public async Task GetPagedAsync_WithinSla_TranslatesAndExecutesOnSqlServer()
    {
        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        using var db = new SupportDbContext(options);
        var clock = new FixedClock(DateTime.UtcNow);
        var sut = new TicketQueries(db, clock);

        var query = new TicketQuery
        {
            Page = 1,
            PageSize = 20,
            SlaStatus = SlaStatus.WithinSla,
            SortBy = "createdAtUtc",
            SortDirection = "desc"
        };

        var result = await sut.GetPagedAsync(query, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 0);
        Assert.All(result.Items, item => Assert.Equal(SlaStatus.WithinSla, item.SlaStatus));
    }

    [Fact]
    public async Task GetPagedAsync_AtRisk_TranslatesAndExecutesOnSqlServer()
    {
        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        using var db = new SupportDbContext(options);
        var clock = new FixedClock(DateTime.UtcNow);
        var sut = new TicketQueries(db, clock);

        var query = new TicketQuery
        {
            Page = 1,
            PageSize = 20,
            SlaStatus = SlaStatus.AtRisk,
            SortBy = "createdAtUtc",
            SortDirection = "desc"
        };

        var result = await sut.GetPagedAsync(query, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 0);
        Assert.All(result.Items, item => Assert.Equal(SlaStatus.AtRisk, item.SlaStatus));
    }
}
