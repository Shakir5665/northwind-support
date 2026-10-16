using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Agents;

public class AgentAssignmentPolicyTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;
    private readonly Category _generalCategory = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false).WithId(1);
    private readonly Category _billingSpecialistCategory = new Category("Billing", requiresSpecialist: true, forcesCriticalPriority: false).WithId(2);

    [Fact]
    public void SelectAgent_SelectsEligibleAgentWithFewestOpenTickets()
    {
        var agent1 = new Agent("Alice", "alice@example.com", 10, Now).WithId(1);
        var agent2 = new Agent("Bob", "bob@example.com", 10, Now).WithId(2);

        var candidates = new List<AgentWorkload>
        {
            new(agent1, OpenTicketCount: 5),
            new(agent2, OpenTicketCount: 2) // Fewest open tickets
        };

        var (selected, reason) = AgentAssignmentPolicy.SelectAgent(candidates, _generalCategory);

        Assert.NotNull(selected);
        Assert.Equal(agent2.Id, selected.Id);
        Assert.Contains("Bob", reason);
        Assert.Contains("2/10", reason);
    }

    [Fact]
    public void SelectAgent_BreaksTiesDeterministicallyByAgentIdAscending()
    {
        var agent1 = new Agent("Bob", "bob@example.com", 10, Now).WithId(2);
        var agent2 = new Agent("Alice", "alice@example.com", 10, Now).WithId(1);

        var candidates = new List<AgentWorkload>
        {
            new(agent1, OpenTicketCount: 3),
            new(agent2, OpenTicketCount: 3) // Tied open ticket count, but Agent.Id 1 < 2
        };

        var (selected, _) = AgentAssignmentPolicy.SelectAgent(candidates, _generalCategory);

        Assert.NotNull(selected);
        Assert.Equal(1, selected.Id);
        Assert.Equal("Alice", selected.FullName);
    }

    [Fact]
    public void SelectAgent_WhenCategoryRequiresSpecialist_OnlySelectsSpecializedAgent()
    {
        var nonSpecialist = new Agent("Alex", "alex@example.com", 10, Now).WithId(1);
        var specialist = new Agent("Sara", "sara@example.com", 10, Now).WithId(2);
        specialist.AddSpecialization(_billingSpecialistCategory.Id);

        var candidates = new List<AgentWorkload>
        {
            new(nonSpecialist, OpenTicketCount: 1), // Non-specialist has fewer open tickets
            new(specialist, OpenTicketCount: 4)     // Specialist has more open tickets
        };

        var (selected, _) = AgentAssignmentPolicy.SelectAgent(candidates, _billingSpecialistCategory);

        Assert.NotNull(selected);
        Assert.Equal(specialist.Id, selected.Id);
        Assert.Equal("Sara", selected.FullName);
    }

    [Fact]
    public void SelectAgent_ExcludesAgentsAtOrAboveMaxCapacity()
    {
        var fullAgent = new Agent("Marcus", "marcus@example.com", maxOpenTickets: 5, Now).WithId(1);
        var availableAgent = new Agent("Priya", "priya@example.com", maxOpenTickets: 8, Now).WithId(2);

        var candidates = new List<AgentWorkload>
        {
            new(fullAgent, OpenTicketCount: 5),     // At capacity (5/5)
            new(availableAgent, OpenTicketCount: 7) // Has 1 slot open (7/8)
        };

        var (selected, _) = AgentAssignmentPolicy.SelectAgent(candidates, _generalCategory);

        Assert.NotNull(selected);
        Assert.Equal(availableAgent.Id, selected.Id);
    }

    [Fact]
    public void SelectAgent_ExcludesInactiveAgents()
    {
        var inactive = new Agent("Yuki", "yuki@example.com", 8, Now).WithId(1);
        inactive.Deactivate();

        var active = new Agent("Ben", "ben@example.com", 8, Now).WithId(2);

        var candidates = new List<AgentWorkload>
        {
            new(inactive, OpenTicketCount: 0),
            new(active, OpenTicketCount: 3)
        };

        var (selected, _) = AgentAssignmentPolicy.SelectAgent(candidates, _generalCategory);

        Assert.NotNull(selected);
        Assert.Equal(active.Id, selected.Id);
    }

    [Fact]
    public void SelectAgent_WhenNobodyIsEligible_ReturnsNullWithReason()
    {
        var nonSpecialist = new Agent("Alex", "alex@example.com", 10, Now).WithId(1);

        var candidates = new List<AgentWorkload>
        {
            new(nonSpecialist, OpenTicketCount: 2)
        };

        var (selected, reason) = AgentAssignmentPolicy.SelectAgent(candidates, _billingSpecialistCategory);

        Assert.Null(selected);
        Assert.Contains("specialized", reason);
        Assert.Contains("Billing", reason);
    }

    [Fact]
    public void ReevaluateAgentForEscalation_WhenCurrentAgentRemainsEligible_RetainsCurrentAgent()
    {
        var currentAgent = new Agent("Alex", "alex@example.com", 10, Now).WithId(1);
        var otherAgent = new Agent("Bob", "bob@example.com", 10, Now).WithId(2);

        var candidates = new List<AgentWorkload>
        {
            new(currentAgent, OpenTicketCount: 4),
            new(otherAgent, OpenTicketCount: 1) // other agent has fewer tickets, but current agent is still eligible
        };

        var (selected, reason) = AgentAssignmentPolicy.ReevaluateAgentForEscalation(
            currentAgent.Id, candidates, _generalCategory);

        Assert.NotNull(selected);
        Assert.Equal(currentAgent.Id, selected.Id);
        Assert.Contains("Retained current owner", reason);
    }

    [Fact]
    public void ReevaluateAgentForEscalation_WhenCurrentAgentIsInactive_ReassignsToBestEligibleAgent()
    {
        var currentAgent = new Agent("Alex", "alex@example.com", 10, Now).WithId(1);
        currentAgent.Deactivate();

        var otherAgent = new Agent("Bob", "bob@example.com", 10, Now).WithId(2);

        var candidates = new List<AgentWorkload>
        {
            new(currentAgent, OpenTicketCount: 4),
            new(otherAgent, OpenTicketCount: 2)
        };

        var (selected, reason) = AgentAssignmentPolicy.ReevaluateAgentForEscalation(
            currentAgent.Id, candidates, _generalCategory);

        Assert.NotNull(selected);
        Assert.Equal(otherAgent.Id, selected.Id);
        Assert.Contains("Assigned to Bob", reason);
    }

    [Fact]
    public void SelectAgent_WhenCandidatesEmpty_ReturnsNullWithReason()
    {
        var (selected, reason) = AgentAssignmentPolicy.SelectAgent(new List<AgentWorkload>(), _generalCategory);

        Assert.Null(selected);
        Assert.Contains("No active agents available", reason);
    }

    [Fact]
    public void SelectAgent_WhenAllCandidatesInactive_ReturnsNullWithReason()
    {
        var agent1 = new Agent("Alex", "alex@example.com", 10, Now).WithId(1);
        agent1.Deactivate();
        var agent2 = new Agent("Bob", "bob@example.com", 10, Now).WithId(2);
        agent2.Deactivate();

        var candidates = new List<AgentWorkload>
        {
            new(agent1, 0),
            new(agent2, 0)
        };

        var (selected, reason) = AgentAssignmentPolicy.SelectAgent(candidates, _generalCategory);

        Assert.Null(selected);
        Assert.Contains("No active agents available", reason);
    }

    [Fact]
    public void SelectAgent_WhenAllEligibleAgentsAtCapacity_ReturnsNullWithReason()
    {
        var agent1 = new Agent("Alex", "alex@example.com", maxOpenTickets: 3, Now).WithId(1);
        var agent2 = new Agent("Bob", "bob@example.com", maxOpenTickets: 2, Now).WithId(2);

        var candidates = new List<AgentWorkload>
        {
            new(agent1, OpenTicketCount: 3), // At capacity
            new(agent2, OpenTicketCount: 2)  // At capacity
        };

        var (selected, reason) = AgentAssignmentPolicy.SelectAgent(candidates, _generalCategory);

        Assert.Null(selected);
        Assert.Contains("maximum open ticket capacity", reason);
    }

    [Fact]
    public void SelectAgent_WhenAllSpecialistsAtCapacity_ReturnsNullWithSpecificReason()
    {
        var specialist1 = new Agent("Sara", "sara@example.com", maxOpenTickets: 2, Now).WithId(1);
        specialist1.AddSpecialization(_billingSpecialistCategory.Id);
        var specialist2 = new Agent("Sam", "sam@example.com", maxOpenTickets: 3, Now).WithId(2);
        specialist2.AddSpecialization(_billingSpecialistCategory.Id);

        var candidates = new List<AgentWorkload>
        {
            new(specialist1, OpenTicketCount: 2), // At capacity
            new(specialist2, OpenTicketCount: 3)  // At capacity
        };

        var (selected, reason) = AgentAssignmentPolicy.SelectAgent(candidates, _billingSpecialistCategory);

        Assert.Null(selected);
        Assert.Contains("All agents specialized in category 'Billing' are currently at maximum capacity", reason);
    }

    [Fact]
    public void ReevaluateAgentForEscalation_WhenNoCurrentAgent_AssignsBestEligibleAgent()
    {
        var agent = new Agent("Sara", "sara@example.com", 10, Now).WithId(1);
        var candidates = new List<AgentWorkload> { new(agent, 2) };

        var (selected, reason) = AgentAssignmentPolicy.ReevaluateAgentForEscalation(
            null, candidates, _generalCategory);

        Assert.NotNull(selected);
        Assert.Equal(agent.Id, selected.Id);
        Assert.Contains("Sara", reason);
    }

    [Fact]
    public void ReevaluateAgentForEscalation_WhenCurrentAgentExceedsCapacity_Reassigns()
    {
        var currentAgent = new Agent("Alex", "alex@example.com", maxOpenTickets: 5, Now).WithId(1);
        var otherAgent = new Agent("Bob", "bob@example.com", maxOpenTickets: 10, Now).WithId(2);

        var candidates = new List<AgentWorkload>
        {
            new(currentAgent, OpenTicketCount: 6), // Strictly exceeds max capacity
            new(otherAgent, OpenTicketCount: 1)
        };

        var (selected, reason) = AgentAssignmentPolicy.ReevaluateAgentForEscalation(
            currentAgent.Id, candidates, _generalCategory);

        Assert.NotNull(selected);
        Assert.Equal(otherAgent.Id, selected.Id);
        Assert.Contains("Bob", reason);
    }

    [Fact]
    public void ReevaluateAgentForEscalation_WhenCurrentAgentLacksRequiredSpecialization_ReassignsToSpecialist()
    {
        var nonSpecialist = new Agent("Alex", "alex@example.com", 10, Now).WithId(1);
        var specialist = new Agent("Sara", "sara@example.com", 10, Now).WithId(2);
        specialist.AddSpecialization(_billingSpecialistCategory.Id);

        var candidates = new List<AgentWorkload>
        {
            new(nonSpecialist, OpenTicketCount: 1),
            new(specialist, OpenTicketCount: 3)
        };

        var (selected, reason) = AgentAssignmentPolicy.ReevaluateAgentForEscalation(
            nonSpecialist.Id, candidates, _billingSpecialistCategory);

        Assert.NotNull(selected);
        Assert.Equal(specialist.Id, selected.Id);
        Assert.Contains("Sara", reason);
    }

    [Fact]
    public void ReevaluateAgentForEscalation_WhenNoAgentIsEligible_ReturnsNull()
    {
        var currentAgent = new Agent("Alex", "alex@example.com", 10, Now).WithId(1);
        currentAgent.Deactivate();

        var candidates = new List<AgentWorkload>
        {
            new(currentAgent, OpenTicketCount: 2)
        };

        var (selected, reason) = AgentAssignmentPolicy.ReevaluateAgentForEscalation(
            currentAgent.Id, candidates, _generalCategory);

        Assert.Null(selected);
        Assert.Contains("No active agents available", reason);
    }
}
