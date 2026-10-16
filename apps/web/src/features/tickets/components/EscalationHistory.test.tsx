import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { Agent, TicketEscalation } from '../../../types/api';
import { EscalationHistory } from './EscalationHistory';

describe('EscalationHistory', () => {
  const agents: Agent[] = [
    {
      id: 1,
      fullName: 'Alice Specialist',
      email: 'alice@example.com',
      isActive: true,
      maxOpenTickets: 5,
      openTicketCount: 2,
      specializations: [],
    },
    {
      id: 2,
      fullName: 'Bob Generalist',
      email: 'bob@example.com',
      isActive: true,
      maxOpenTickets: 5,
      openTicketCount: 1,
      specializations: [],
    },
  ];

  it('renders loading spinner when isLoading is true and escalations is null/empty', () => {
    render(
      <EscalationHistory
        escalations={undefined}
        agents={agents}
        isLoading={true}
        error={undefined}
        onRetry={vi.fn()}
      />,
    );

    expect(screen.getByRole('status')).toHaveTextContent(/loading escalation history/i);
  });

  it('renders error message and retry button on failure', () => {
    const onRetry = vi.fn();
    render(
      <EscalationHistory
        escalations={undefined}
        agents={agents}
        isLoading={false}
        error="Failed to load escalation history."
        onRetry={onRetry}
      />,
    );

    expect(screen.getByText('Failed to load escalation history.')).toBeInTheDocument();
    const retryBtn = screen.getByRole('button', { name: /try again/i });
    expect(retryBtn).toBeInTheDocument();

    fireEvent.click(retryBtn);
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('renders empty message when no escalations exist', () => {
    render(
      <EscalationHistory
        escalations={[]}
        agents={agents}
        isLoading={false}
        error={undefined}
        onRetry={vi.fn()}
      />,
    );

    expect(screen.getByText('No escalation history for this ticket.')).toBeInTheDocument();
  });

  it('renders escalation history entries with before/after priority, agent, due dates, actor, and reason', () => {
    const records: TicketEscalation[] = [
      {
        id: 2,
        ticketId: 10,
        escalatedBy: 'Operations Manager',
        reason: 'Client requested emergency priority escalation',
        escalatedAtUtc: '2026-10-16T14:30:00Z',
        fromPriority: 'High',
        toPriority: 'Critical',
        fromAgentId: 1,
        toAgentId: 2,
        fromDueAtUtc: '2026-10-16T18:00:00Z',
        toDueAtUtc: '2026-10-16T16:00:00Z',
      },
      {
        id: 1,
        ticketId: 10,
        escalatedBy: 'Support Agent',
        reason: 'Initial escalation from Medium to High',
        escalatedAtUtc: '2026-10-16T10:00:00Z',
        fromPriority: 'Medium',
        toPriority: 'High',
        fromAgentId: null,
        toAgentId: 1,
        fromDueAtUtc: null,
        toDueAtUtc: '2026-10-16T18:00:00Z',
      },
    ];

    render(
      <EscalationHistory
        escalations={records}
        agents={agents}
        isLoading={false}
        error={undefined}
        onRetry={vi.fn()}
      />,
    );

    // Entry 2 (newest)
    expect(screen.getByText('Operations Manager')).toBeInTheDocument();
    expect(
      screen.getByText('Client requested emergency priority escalation'),
    ).toBeInTheDocument();
    expect(screen.getByText(/Alice Specialist\s+→\s+Bob Generalist/)).toBeInTheDocument();

    // Entry 1
    expect(screen.getByText('Support Agent')).toBeInTheDocument();
    expect(
      screen.getByText('Initial escalation from Medium to High'),
    ).toBeInTheDocument();
    // Null agent representation
    expect(screen.getByText(/Unassigned\s+→\s+Alice Specialist/)).toBeInTheDocument();
    // Null due date representation
    expect(screen.getByText(/No due date\s+→/)).toBeInTheDocument();
  });
});
