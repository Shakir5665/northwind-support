using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class SlaCalculatorTests
{
    private static readonly DateTime Start = FixedClock.DefaultNow;
    private readonly SlaOptions _options = new();

    [Theory]
    [InlineData(TicketPriority.Low, 72)]
    [InlineData(TicketPriority.Medium, 24)]
    [InlineData(TicketPriority.High, 8)]
    [InlineData(TicketPriority.Critical, 4)]
    public void CalculateDueDate_StandardCustomer_AppliesFullBaseWindow(TicketPriority priority, int expectedHours)
    {
        var (window, dueAtUtc, reason) = SlaCalculator.CalculateDueDate(
            priority, CustomerTier.Standard, _options, Start);

        Assert.Equal(TimeSpan.FromHours(expectedHours), window);
        Assert.Equal(Start.AddHours(expectedHours), dueAtUtc);
        Assert.Contains($"{expectedHours}h", reason);
    }

    [Theory]
    [InlineData(TicketPriority.Low, 36)]      // 72 * 0.5 = 36
    [InlineData(TicketPriority.Medium, 12)]   // 24 * 0.5 = 12
    [InlineData(TicketPriority.High, 4)]      // 8 * 0.5 = 4
    [InlineData(TicketPriority.Critical, 2)]  // 4 * 0.5 = 2
    public void CalculateDueDate_PremiumCustomer_HalvesBaseWindow(TicketPriority priority, int expectedHours)
    {
        var (window, dueAtUtc, reason) = SlaCalculator.CalculateDueDate(
            priority, CustomerTier.Premium, _options, Start);

        Assert.Equal(TimeSpan.FromHours(expectedHours), window);
        Assert.Equal(Start.AddHours(expectedHours), dueAtUtc);
        Assert.Contains("Premium", reason);
        Assert.Contains($"{expectedHours}h", reason);
    }

    [Fact]
    public void CalculateDueDate_PremiumCustomer_EnforcesMinimumOneHourFloor()
    {
        var customOptions = new SlaOptions
        {
            BaseWindowHours = new Dictionary<string, int>
            {
                [nameof(TicketPriority.Critical)] = 1 // 1 hour base * 0.5 = 0.5 hours -> floored to 1 hour
            },
            PremiumCustomerMultiplier = 0.5,
            MinimumWindowHours = 1
        };

        var (window, dueAtUtc, reason) = SlaCalculator.CalculateDueDate(
            TicketPriority.Critical, CustomerTier.Premium, customOptions, Start);

        Assert.Equal(TimeSpan.FromHours(1), window);
        Assert.Equal(Start.AddHours(1), dueAtUtc);
        Assert.Contains("floored to minimum 1h", reason);
    }

    [Fact]
    public void CalculateDueDate_UsesConfigurableOptionsFromSettings()
    {
        var customOptions = new SlaOptions
        {
            BaseWindowHours = new Dictionary<string, int>
            {
                [nameof(TicketPriority.High)] = 10
            },
            PremiumCustomerMultiplier = 0.4
        };

        var (window, dueAtUtc, _) = SlaCalculator.CalculateDueDate(
            TicketPriority.High, CustomerTier.Premium, customOptions, Start);

        // 10 * 0.4 = 4.0 hours
        Assert.Equal(TimeSpan.FromHours(4), window);
        Assert.Equal(Start.AddHours(4), dueAtUtc);
    }

    [Fact]
    public void CalculateDueDate_WhenOptionsIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SlaCalculator.CalculateDueDate(TicketPriority.High, CustomerTier.Standard, null!, Start));
    }

    [Fact]
    public void CalculateDueDate_CustomMinimumFloor_FloorsToConfiguredHours()
    {
        var customOptions = new SlaOptions
        {
            BaseWindowHours = new Dictionary<string, int>
            {
                [nameof(TicketPriority.Critical)] = 4
            },
            PremiumCustomerMultiplier = 0.5, // 4 * 0.5 = 2 hours
            MinimumWindowHours = 3           // Configured minimum is 3 hours -> floored to 3 hours
        };

        var (window, dueAtUtc, reason) = SlaCalculator.CalculateDueDate(
            TicketPriority.Critical, CustomerTier.Premium, customOptions, Start);

        Assert.Equal(TimeSpan.FromHours(3), window);
        Assert.Equal(Start.AddHours(3), dueAtUtc);
        Assert.Contains("floored to minimum 3h", reason);
    }
}
