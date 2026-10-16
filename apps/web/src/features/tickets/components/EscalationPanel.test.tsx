import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { EscalationResponse, TicketDetail } from '../../../types/api';
import { EscalationPanel } from './EscalationPanel';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: {
    escalate: vi.fn(),
  },
}));

const escalateMock = vi.mocked(ticketsApi.escalate);

describe('EscalationPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  const baseTicket: TicketDetail = {
    id: 10,
    reference: 'TCK-0010',
    title: 'Database connection timeouts',
    description: 'Pool exhausted during peak load',
    status: 'InProgress',
    priority: 'High',
    customer: { id: 1, name: 'Northwind Traders', email: 'northwind@example.com', phone: null, tier: 'Premium' },
    category: { id: 1, name: 'Infrastructure', requiresSpecialist: true },
    assignedAgent: { id: 5, fullName: 'Alice Smith' },
    createdAtUtc: '2026-10-15T10:00:00Z',
    updatedAtUtc: '2026-10-15T12:00:00Z',
    dueAtUtc: '2026-10-16T10:00:00Z',
    resolvedAtUtc: null,
    slaStatus: 'AtRisk',
  };

  it('disallows escalation when priority is Critical', () => {
    render(
      <EscalationPanel
        ticket={{ ...baseTicket, priority: 'Critical' }}
        onEscalated={vi.fn()}
      />,
    );

    expect(
      screen.getByText('Tickets at Critical priority cannot be escalated further.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /escalate to/i })).not.toBeInTheDocument();
  });

  it('disallows escalation when status is Resolved', () => {
    render(
      <EscalationPanel
        ticket={{ ...baseTicket, status: 'Resolved' }}
        onEscalated={vi.fn()}
      />,
    );

    expect(
      screen.getByText('Resolved tickets cannot be escalated.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /escalate to/i })).not.toBeInTheDocument();
  });

  it('disallows escalation when status is Closed', () => {
    render(
      <EscalationPanel
        ticket={{ ...baseTicket, status: 'Closed' }}
        onEscalated={vi.fn()}
      />,
    );

    expect(
      screen.getByText('Closed tickets cannot be escalated.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /escalate to/i })).not.toBeInTheDocument();
  });

  it('validates reason shorter than 5 characters and does not call API', async () => {
    render(<EscalationPanel ticket={baseTicket} onEscalated={vi.fn()} />);

    fireEvent.change(screen.getByLabelText(/escalated by/i), {
      target: { value: 'Manager Bob' },
    });
    fireEvent.change(screen.getByLabelText(/reason for escalation/i), {
      target: { value: 'help' }, // 4 chars
    });

    fireEvent.click(screen.getByRole('button', { name: /escalate to critical/i }));

    expect(
      await screen.findByText('Escalation reason must be at least 5 characters.'),
    ).toBeInTheDocument();
    expect(escalateMock).not.toHaveBeenCalled();
  });

  it('validates reason longer than 500 characters and does not call API', async () => {
    render(<EscalationPanel ticket={baseTicket} onEscalated={vi.fn()} />);

    fireEvent.change(screen.getByLabelText(/escalated by/i), {
      target: { value: 'Manager Bob' },
    });
    fireEvent.change(screen.getByLabelText(/reason for escalation/i), {
      target: { value: 'a'.repeat(501) },
    });

    fireEvent.click(screen.getByRole('button', { name: /escalate to critical/i }));

    expect(
      await screen.findByText('Escalation reason cannot exceed 500 characters.'),
    ).toBeInTheDocument();
    expect(escalateMock).not.toHaveBeenCalled();
  });

  it('validates empty actor and does not call API', async () => {
    render(<EscalationPanel ticket={baseTicket} onEscalated={vi.fn()} />);

    fireEvent.change(screen.getByLabelText(/escalated by/i), {
      target: { value: '   ' },
    });
    fireEvent.change(screen.getByLabelText(/reason for escalation/i), {
      target: { value: 'Customer outage requiring critical fix' },
    });

    fireEvent.click(screen.getByRole('button', { name: /escalate to critical/i }));

    expect(
      await screen.findByText('Please provide the name of the person escalating.'),
    ).toBeInTheDocument();
    expect(escalateMock).not.toHaveBeenCalled();
  });

  it('submits valid escalation, disables button while pending, and resets form on success', async () => {
    const onEscalated = vi.fn();
    let resolveEscalate: (val: EscalationResponse) => void = () => {};
    const escalatePromise = new Promise<EscalationResponse>((resolve) => {
      resolveEscalate = resolve;
    });
    escalateMock.mockReturnValue(escalatePromise);

    render(<EscalationPanel ticket={baseTicket} onEscalated={onEscalated} />);

    const actorInput = screen.getByLabelText(/escalated by/i) as HTMLInputElement;
    const reasonInput = screen.getByLabelText(/reason for escalation/i) as HTMLTextAreaElement;
    const submitBtn = screen.getByRole('button', { name: /escalate to critical/i });

    fireEvent.change(actorInput, { target: { value: 'Support Lead' } });
    fireEvent.change(reasonInput, {
      target: { value: 'Customer SLA breach imminent, needs immediate escalation.' },
    });

    fireEvent.click(submitBtn);

    // Pending state
    expect(submitBtn).toBeDisabled();
    expect(screen.getByRole('button', { name: /escalating\.\.\./i })).toBeInTheDocument();

    // Rapid double click does not trigger another call
    fireEvent.click(submitBtn);
    expect(escalateMock).toHaveBeenCalledTimes(1);
    expect(escalateMock).toHaveBeenCalledWith(10, {
      escalatedBy: 'Support Lead',
      reason: 'Customer SLA breach imminent, needs immediate escalation.',
    });

    const successResponse: EscalationResponse = {
      ticket: {
        ...baseTicket,
        priority: 'Critical',
      },
      escalation: {
        id: 1,
        ticketId: 10,
        escalatedBy: 'Support Lead',
        reason: 'Customer SLA breach imminent, needs immediate escalation.',
        escalatedAtUtc: '2026-10-16T12:00:00Z',
        fromPriority: 'High',
        toPriority: 'Critical',
        fromAgentId: 5,
        toAgentId: 5,
        fromDueAtUtc: '2026-10-16T10:00:00Z',
        toDueAtUtc: '2026-10-16T14:00:00Z',
      },
    };

    resolveEscalate(successResponse);

    await waitFor(() => {
      expect(onEscalated).toHaveBeenCalledWith(successResponse);
    });

    // Form was reset
    expect(reasonInput.value).toBe('');
    expect(actorInput.value).toBe('');
  });

  it('handles server HTTP 409 conflict error (e.g. ticket already Critical or Resolved on server) and does not update ticket', async () => {
    escalateMock.mockRejectedValue(
      new ApiError(409, 'Ticket cannot be escalated because it is already Critical.'),
    );

    const onEscalated = vi.fn();
    render(<EscalationPanel ticket={baseTicket} onEscalated={onEscalated} />);

    fireEvent.change(screen.getByLabelText(/escalated by/i), {
      target: { value: 'Support Lead' },
    });
    fireEvent.change(screen.getByLabelText(/reason for escalation/i), {
      target: { value: 'Escalate to critical priority' },
    });

    fireEvent.click(screen.getByRole('button', { name: /escalate to critical/i }));

    expect(
      await screen.findByText('Ticket cannot be escalated because it is already Critical.'),
    ).toBeInTheDocument();
    expect(onEscalated).not.toHaveBeenCalled();
  });

  it('handles server HTTP 404 ticket not found', async () => {
    escalateMock.mockRejectedValue(new ApiError(404, 'Ticket not found.'));

    render(<EscalationPanel ticket={baseTicket} onEscalated={vi.fn()} />);

    fireEvent.change(screen.getByLabelText(/escalated by/i), {
      target: { value: 'Support Lead' },
    });
    fireEvent.change(screen.getByLabelText(/reason for escalation/i), {
      target: { value: 'Escalate to critical priority' },
    });

    fireEvent.click(screen.getByRole('button', { name: /escalate to critical/i }));

    expect(await screen.findByText('Ticket not found.')).toBeInTheDocument();
  });

  it('handles server HTTP 400 invalid request', async () => {
    escalateMock.mockRejectedValue(new ApiError(400, 'Invalid escalation reason.'));

    render(<EscalationPanel ticket={baseTicket} onEscalated={vi.fn()} />);

    fireEvent.change(screen.getByLabelText(/escalated by/i), {
      target: { value: 'Support Lead' },
    });
    fireEvent.change(screen.getByLabelText(/reason for escalation/i), {
      target: { value: 'Escalate to critical priority' },
    });

    fireEvent.click(screen.getByRole('button', { name: /escalate to critical/i }));

    expect(await screen.findByText('Invalid escalation reason.')).toBeInTheDocument();
  });

  it('handles network failure gracefully', async () => {
    escalateMock.mockRejectedValue(new Error('Network connection failed'));

    render(<EscalationPanel ticket={baseTicket} onEscalated={vi.fn()} />);

    fireEvent.change(screen.getByLabelText(/escalated by/i), {
      target: { value: 'Support Lead' },
    });
    fireEvent.change(screen.getByLabelText(/reason for escalation/i), {
      target: { value: 'Escalate to critical priority' },
    });

    fireEvent.click(screen.getByRole('button', { name: /escalate to critical/i }));

    expect(
      await screen.findByText(/Could not escalate the ticket\. \(Network connection failed\)/i),
    ).toBeInTheDocument();
  });
});
