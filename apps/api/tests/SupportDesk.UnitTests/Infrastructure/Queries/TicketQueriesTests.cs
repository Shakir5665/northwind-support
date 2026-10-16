using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;
using Xunit;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class TicketQueriesTests : IDisposable
{
    private readonly List<string> _sqlLogs = [];
    private readonly SqliteConnection _connection;
    private readonly SupportDbContext _db;
    private readonly TicketQueries _sut;

    private readonly Customer _customer1;
    private readonly Customer _customer2;
    private readonly Category _category1;
    private readonly Category _category2;
    private readonly Agent _agent1;
    private readonly Agent _agent2;

    public TicketQueriesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(_connection)
            .LogTo(s => _sqlLogs.Add(s), Microsoft.Extensions.Logging.LogLevel.Information)
            .Options;

        _db = new SupportDbContext(options);
        _db.Database.EnsureCreated();
        _sqlLogs.Clear();

        var now = FixedClock.DefaultNow;

        _customer1 = new Customer("Contoso Ltd", "contact@contoso.com", null, CustomerTier.Premium, now);
        _customer2 = new Customer("Fabrikam Inc", "contact@fabrikam.com", null, CustomerTier.Standard, now);

        _category1 = new Category("Hardware", false, false);
        _category2 = new Category("Network", false, true);

        _agent1 = new Agent("Alice Smith", "alice@northwind.com", 10, now);
        _agent2 = new Agent("Bob Jones", "bob@northwind.com", 10, now);

        _db.AddRange(_customer1, _customer2, _category1, _category2, _agent1, _agent2);
        _db.SaveChanges();

        // 1: Hardware, Customer 1, High, New, Unassigned
        var t1 = Ticket.Raise("TCK-0001", "Printer broken", "Desc", _customer1.Id, _category1.Id, TicketPriority.High, now);

        // 2: Network, Customer 1, Critical, Open, Agent 1
        var t2 = Ticket.Raise("TCK-0002", "VPN connection issue", "Desc", _customer1.Id, _category2.Id, TicketPriority.Critical, now);
        t2.AssignTo(_agent1.Id, now);
        t2.ChangeStatus(TicketStatus.Open, now);

        // 3: Hardware, Customer 2, Low, Open, Agent 2
        var t3 = Ticket.Raise("TCK-0003", "Monitor flickering", "Desc", _customer2.Id, _category1.Id, TicketPriority.Low, now);
        t3.AssignTo(_agent2.Id, now);
        t3.ChangeStatus(TicketStatus.Open, now);

        // 4: Network, Customer 2, Critical, InProgress, Agent 1
        var t4 = Ticket.Raise("TCK-0004", "Network outage in branch", "Desc", _customer2.Id, _category2.Id, TicketPriority.Critical, now);
        t4.AssignTo(_agent1.Id, now);
        t4.ChangeStatus(TicketStatus.InProgress, now);

        // 5: Network, Customer 1, Medium, Resolved, Unassigned
        var t5 = Ticket.Raise("TCK-0005", "Email delivery failure", "Desc", _customer1.Id, _category2.Id, TicketPriority.Medium, now);
        t5.ChangeStatus(TicketStatus.Resolved, now);

        _db.AddRange(t1, t2, t3, t4, t5);
        _db.SaveChanges();

        _sut = new TicketQueries(_db, new FixedClock());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetPagedAsync_WithoutFilters_ReturnsAllTicketsAndCorrectCount()
    {
        var result = await _sut.GetPagedAsync(new TicketQuery(), CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task GetPagedAsync_FilterByStatus_ExactMatch()
    {
        var query = new TicketQuery { Status = TicketStatus.Open };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(TicketStatus.Open, item.Status));
        Assert.Contains(result.Items, item => item.Reference == "TCK-0002");
        Assert.Contains(result.Items, item => item.Reference == "TCK-0003");
    }

    [Fact]
    public async Task GetPagedAsync_FilterByPriority_ExactMatch()
    {
        var query = new TicketQuery { Priority = TicketPriority.Critical };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(TicketPriority.Critical, item.Priority));
        Assert.Contains(result.Items, item => item.Reference == "TCK-0002");
        Assert.Contains(result.Items, item => item.Reference == "TCK-0004");
    }

    [Fact]
    public async Task GetPagedAsync_FilterByCategoryId_ExactMatch()
    {
        var query = new TicketQuery { CategoryId = _category1.Id };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(_category1.Id, item.Category.Id));
        Assert.Contains(result.Items, item => item.Reference == "TCK-0001");
        Assert.Contains(result.Items, item => item.Reference == "TCK-0003");
    }

    [Fact]
    public async Task GetPagedAsync_FilterByCustomerId_ExactMatch()
    {
        var query = new TicketQuery { CustomerId = _customer2.Id };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(_customer2.Id, item.Customer.Id));
        Assert.Contains(result.Items, item => item.Reference == "TCK-0003");
        Assert.Contains(result.Items, item => item.Reference == "TCK-0004");
    }

    [Fact]
    public async Task GetPagedAsync_FilterByAssignedAgentId_ExactMatch()
    {
        var query = new TicketQuery { AssignedAgentId = _agent1.Id };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(_agent1.Id, item.AssignedAgent?.Id));
        Assert.Contains(result.Items, item => item.Reference == "TCK-0002");
        Assert.Contains(result.Items, item => item.Reference == "TCK-0004");
    }

    [Fact]
    public async Task GetPagedAsync_FilterByUnassignedOnly_ReturnsOnlyTicketsWithoutAgent()
    {
        var query = new TicketQuery { UnassignedOnly = true };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Null(item.AssignedAgent));
        Assert.Contains(result.Items, item => item.Reference == "TCK-0001");
        Assert.Contains(result.Items, item => item.Reference == "TCK-0005");
    }

    [Fact]
    public async Task GetPagedAsync_FilterByUnassignedOnlyAndAssignedAgentId_ReturnsEmpty()
    {
        // When unassignedOnly=true AND assignedAgentId is set, AND semantics must yield 0 results
        var query = new TicketQuery
        {
            UnassignedOnly = true,
            AssignedAgentId = _agent1.Id
        };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetPagedAsync_SearchByTitle_MatchesCorrectly()
    {
        var query = new TicketQuery { Search = "Printer" };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0001", result.Items[0].Reference);
    }

    [Fact]
    public async Task GetPagedAsync_SearchByReference_MatchesCorrectly()
    {
        var query = new TicketQuery { Search = "TCK-0003" };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0003", result.Items[0].Reference);
    }

    [Fact]
    public async Task GetPagedAsync_SearchByCustomerName_MatchesCorrectly()
    {
        var query = new TicketQuery { Search = "Fabrikam" };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, item => item.Reference == "TCK-0003");
        Assert.Contains(result.Items, item => item.Reference == "TCK-0004");
    }

    [Fact]
    public async Task GetPagedAsync_SearchWithWhitespace_IsTrimmed()
    {
        var query = new TicketQuery { Search = "   VPN   " };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0002", result.Items[0].Reference);
    }

    [Fact]
    public async Task GetPagedAsync_BlankSearch_IsIgnored()
    {
        var query = new TicketQuery { Search = "    " };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task GetPagedAsync_CombinedFilters_ApplyAndSemantics()
    {
        var query = new TicketQuery
        {
            Priority = TicketPriority.Critical,
            CustomerId = _customer1.Id
        };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0002", result.Items[0].Reference);
    }

    [Fact]
    public async Task GetPagedAsync_PagingOnFilteredResults_ComputesTotalCountAndPagesCorrectly()
    {
        // 3 tickets for Category 2 (TCK-0002, TCK-0004, TCK-0005)
        var queryPage1 = new TicketQuery
        {
            CategoryId = _category2.Id,
            Page = 1,
            PageSize = 2
        };

        var page1 = await _sut.GetPagedAsync(queryPage1, CancellationToken.None);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(1, page1.Page);
        Assert.Equal(2, page1.PageSize);
        Assert.Equal(2, page1.Items.Count);

        var queryPage2 = new TicketQuery
        {
            CategoryId = _category2.Id,
            Page = 2,
            PageSize = 2
        };

        var page2 = await _sut.GetPagedAsync(queryPage2, CancellationToken.None);

        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(2, page2.TotalPages);
        Assert.Equal(2, page2.Page);
        Assert.Single(page2.Items);

        // Ensure distinct items across pages
        var page1Ids = page1.Items.Select(x => x.Id).ToHashSet();
        Assert.DoesNotContain(page2.Items[0].Id, page1Ids);
    }

    [Fact]
    public async Task GetPagedAsync_GeneratesSqlWithWhereClausesAndPaging()
    {
        _sqlLogs.Clear();

        var query = new TicketQuery
        {
            Status = TicketStatus.Open,
            Priority = TicketPriority.Critical,
            Search = "VPN"
        };

        var result = await _sut.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);

        var executedCommands = _sqlLogs
            .Where(l => l.Contains("Executed DbCommand"))
            .ToList();

        // 1 count query + 1 paged items query
        Assert.Equal(2, executedCommands.Count);

        // Verify both queries execute SQL WHERE filtering on database
        Assert.All(executedCommands, cmd =>
        {
            Assert.Contains("WHERE", cmd);
            Assert.Contains("Status", cmd);
            Assert.Contains("Priority", cmd);
        });
    }
}
