using SupportDesk.Domain.Aggregates.Categories;

namespace SupportDesk.Domain.Aggregates.Agents;

/// <summary>
/// Implements business rules for agent eligibility (BR-4), workload assignment (BR-5), and escalation reassignment (BR-7).
/// </summary>
public static class AgentAssignmentPolicy
{
    /// <summary>
    /// Checks whether an agent is eligible to handle a ticket in the specified category.
    /// An agent is eligible if they are active, have the required category specialization if
    /// the category requires one, and their open ticket count is strictly below their capacity limit.
    /// </summary>
    public static bool IsAgentEligible(Agent agent, int openTicketCount, Category category)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(category);

        if (!agent.IsActive)
        {
            return false;
        }

        if (openTicketCount >= agent.MaxOpenTickets)
        {
            return false;
        }

        if (category.RequiresSpecialist && !agent.Specializations.Any(s => s.CategoryId == category.Id))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Selects the best eligible agent among candidates based on fewest open tickets,
    /// breaking ties deterministically by Agent.Id ascending (BR-5).
    /// If nobody is eligible, returns null with a human-readable explanation reason.
    /// </summary>
    public static (Agent? Agent, string Reason) SelectAgent(
        IEnumerable<AgentWorkload> candidates,
        Category category)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(category);

        var candidateList = candidates.ToList();

        var eligible = candidateList
            .Where(c => IsAgentEligible(c.Agent, c.OpenTicketCount, category))
            .OrderBy(c => c.OpenTicketCount)
            .ThenBy(c => c.Agent.Id)
            .ToList();

        if (eligible.Count == 0)
        {
            return (null, ExplainWhyNoEligibleAgent(candidateList, category));
        }

        var winner = eligible[0];
        var reason = $"Assigned to {winner.Agent.FullName} with fewest open tickets ({winner.OpenTicketCount}/{winner.Agent.MaxOpenTickets}).";

        return (winner.Agent, reason);
    }

    /// <summary>
    /// Re-evaluates ticket ownership during escalation (BR-7).
    /// Keeps the current agent if still eligible; otherwise reassigns per BR-5.
    /// </summary>
    public static (Agent? Agent, string Reason) ReevaluateAgentForEscalation(
        int? currentAgentId,
        IEnumerable<AgentWorkload> candidates,
        Category category)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(category);

        var candidateList = candidates.ToList();

        if (currentAgentId.HasValue)
        {
            var currentCandidate = candidateList.FirstOrDefault(c => c.Agent.Id == currentAgentId.Value);
            if (currentCandidate is not null)
            {
                // When re-evaluating an agent who already owns this ticket, their open ticket count
                // already includes this ticket. They remain eligible if active, qualified (if category
                // requires specialist), and not exceeding their max capacity.
                var agent = currentCandidate.Agent;
                var hasSpecializationIfRequired = !category.RequiresSpecialist ||
                                                  agent.Specializations.Any(s => s.CategoryId == category.Id);
                var withinCapacity = currentCandidate.OpenTicketCount <= agent.MaxOpenTickets;

                if (agent.IsActive && hasSpecializationIfRequired && withinCapacity)
                {
                    return (agent, $"Retained current owner {agent.FullName} who remains eligible.");
                }
            }
        }

        // Current agent is not eligible or none was assigned; select new owner per BR-5
        return SelectAgent(candidateList, category);
    }

    private static string ExplainWhyNoEligibleAgent(IReadOnlyList<AgentWorkload> candidates, Category category)
    {
        if (candidates.Count == 0 || candidates.All(c => !c.Agent.IsActive))
        {
            return "No active agents available for assignment.";
        }

        if (category.RequiresSpecialist)
        {
            var activeSpecialists = candidates
                .Where(c => c.Agent.IsActive && c.Agent.Specializations.Any(s => s.CategoryId == category.Id))
                .ToList();

            if (activeSpecialists.Count == 0)
            {
                return $"No active agents are specialized in category '{category.Name}'.";
            }

            if (activeSpecialists.All(c => c.OpenTicketCount >= c.Agent.MaxOpenTickets))
            {
                return $"All agents specialized in category '{category.Name}' are currently at maximum capacity.";
            }
        }

        var activeAgents = candidates.Where(c => c.Agent.IsActive).ToList();
        if (activeAgents.All(c => c.OpenTicketCount >= c.Agent.MaxOpenTickets))
        {
            return "All eligible agents are currently at maximum open ticket capacity.";
        }

        return $"No eligible agent available to handle tickets in category '{category.Name}'.";
    }
}
