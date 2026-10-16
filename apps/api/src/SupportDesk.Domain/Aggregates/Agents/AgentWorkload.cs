namespace SupportDesk.Domain.Aggregates.Agents;

/// <summary>
/// Pairs an <see cref="Agent"/> aggregate with their current count of open (unresolved, unclosed) tickets.
/// Used for workload-based ticket assignment (BR-5).
/// </summary>
public sealed record AgentWorkload(Agent Agent, int OpenTicketCount);
