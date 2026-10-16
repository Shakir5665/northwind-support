using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Data;

public class TicketEscalationPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SupportDbContext _db;

    public TicketEscalationPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new SupportDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CanPersistAndRetrieve_TicketEscalation_WithAllFields()
    {
        var now = FixedClock.DefaultNow;

        var customer = new Customer("Contoso Ltd", "contact@contoso.com", null, CustomerTier.Standard, now);
        var category = new Category("Hardware", false, false);
        var agentFrom = new Agent("Agent From", "from@example.com", 10, now);
        var agentTo = new Agent("Agent To", "to@example.com", 10, now);

        _db.AddRange(customer, category, agentFrom, agentTo);
        await _db.SaveChangesAsync();

        var initialDue = now.AddHours(24);
        var newDue = now.AddHours(8);

        var ticket = Ticket.Raise(
            "TCK-ESC-01",
            "Urgent Server Disk Failure",
            "Disk array has failed and requires escalation",
            customer.Id,
            category.Id,
            TicketPriority.Medium,
            now,
            initialDue,
            agentFrom.Id);

        _db.Add(ticket);
        await _db.SaveChangesAsync();

        // Escalate ticket
        var escalation = ticket.Escalate(
            TicketPriority.High,
            newDue,
            agentTo.Id,
            "Critical customer outage requiring senior engineer",
            "Lead Operator John",
            now.AddHours(1));

        await _db.SaveChangesAsync();

        // Read back in a fresh context
        _db.ChangeTracker.Clear();

        var savedTicket = await _db.Set<Ticket>()
            .Include(t => t.Escalations)
            .SingleAsync(t => t.Id == ticket.Id);

        Assert.Equal(TicketPriority.High, savedTicket.Priority);
        Assert.Equal(newDue, savedTicket.DueAtUtc);
        Assert.Equal(agentTo.Id, savedTicket.AssignedAgentId);

        var savedEscalation = Assert.Single(savedTicket.Escalations);
        Assert.True(savedEscalation.Id > 0);
        Assert.Equal(ticket.Id, savedEscalation.TicketId);
        Assert.Equal(TicketPriority.Medium, savedEscalation.FromPriority);
        Assert.Equal(TicketPriority.High, savedEscalation.ToPriority);
        Assert.Equal(agentFrom.Id, savedEscalation.FromAgentId);
        Assert.Equal(agentTo.Id, savedEscalation.ToAgentId);
        Assert.Equal(initialDue, savedEscalation.FromDueAtUtc);
        Assert.Equal(newDue, savedEscalation.ToDueAtUtc);
        Assert.Equal("Critical customer outage requiring senior engineer", savedEscalation.Reason);
        Assert.Equal("Lead Operator John", savedEscalation.EscalatedBy);
        Assert.Equal(now.AddHours(1), savedEscalation.EscalatedAtUtc);
    }

    [Fact]
    public async Task CanPersistAndRetrieve_TicketEscalation_WithNullableAgentsAndDueDates()
    {
        var now = FixedClock.DefaultNow;

        var customer = new Customer("Acme Corp", "contact@acme.com", null, CustomerTier.Standard, now);
        var category = new Category("Software", false, false);

        _db.AddRange(customer, category);
        await _db.SaveChangesAsync();

        // Ticket with no assigned agent and no initial due date
        var ticket = Ticket.Raise(
            "TCK-ESC-02",
            "Software question",
            "General inquiry without initial assignment",
            customer.Id,
            category.Id,
            TicketPriority.Low,
            now,
            dueAtUtc: null,
            assignedAgentId: null);

        _db.Add(ticket);
        await _db.SaveChangesAsync();

        var newDue = now.AddHours(24);

        // Escalate leaving agent unassigned
        ticket.Escalate(
            TicketPriority.Medium,
            newDue,
            newAssignedAgentId: null,
            "Escalating priority though still unassigned",
            "System Dispatcher",
            now.AddMinutes(15));

        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        var savedEscalation = await _db.Set<TicketEscalation>()
            .SingleAsync(e => e.TicketId == ticket.Id);

        Assert.Null(savedEscalation.FromAgentId);
        Assert.Null(savedEscalation.ToAgentId);
        Assert.Null(savedEscalation.FromDueAtUtc);
        Assert.Equal(newDue, savedEscalation.ToDueAtUtc);
    }

    [Fact]
    public async Task DeletingTicket_CascadesDeleteToEscalations()
    {
        var now = FixedClock.DefaultNow;

        var customer = new Customer("Demo Co", "contact@demo.com", null, CustomerTier.Standard, now);
        var category = new Category("Network", false, false);

        _db.AddRange(customer, category);
        await _db.SaveChangesAsync();

        var ticket = Ticket.Raise("TCK-ESC-03", "Test Ticket", "Description", customer.Id, category.Id, TicketPriority.Low, now);
        _db.Add(ticket);
        await _db.SaveChangesAsync();

        ticket.Escalate(TicketPriority.Medium, now.AddHours(24), null, "Valid reason text", "Actor", now.AddMinutes(5));
        await _db.SaveChangesAsync();

        Assert.NotEmpty(await _db.Set<TicketEscalation>().Where(e => e.TicketId == ticket.Id).ToListAsync());

        // Delete ticket
        _db.Remove(ticket);
        await _db.SaveChangesAsync();

        // Escalation records should be deleted via Cascade FK
        var remainingEscalations = await _db.Set<TicketEscalation>().Where(e => e.TicketId == ticket.Id).ToListAsync();
        Assert.Empty(remainingEscalations);
    }
}
