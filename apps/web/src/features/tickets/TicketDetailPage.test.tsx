import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../api/client';
import { ticketsApi } from '../../api/tickets';
import { useReferenceData } from '../../hooks/useReferenceData';
import type { EscalationResponse, TicketDetail, TicketEscalation } from '../../types/api';
import { TicketDetailPage } from './TicketDetailPage';

vi.mock('../../hooks/useReferenceData', () => ({
  useReferenceData: vi.fn(),
}));

vi.mock('../../api/tickets', () => ({
  ticketsApi: {
    getTicket: vi.fn(),
    getEscalations: vi.fn(),
    escalate: vi.fn(),
    updateStatus: vi.fn(),
    assignAgent: vi.fn(),
  },
}));

const mockUseReferenceData = vi.mocked(useReferenceData);
const mockGetTicket = vi.mocked(ticketsApi.getTicket);
const mockGetEscalations = vi.mocked(ticketsApi.getEscalations);
const mockEscalate = vi.mocked(ticketsApi.escalate);

describe('TicketDetailPage', () => {
  const initialTicket: TicketDetail = {
    id: 15,
    reference: 'TCK-0015',
    title: 'VPN gateway latency spike',
    description: 'High latency observed across all tunnels',
    status: 'InProgress',
    priority: 'Medium',
    customer: { id: 1, name: 'Northwind Traders', email: 'northwind@example.com', phone: null, tier: 'Premium' },
    category: { id: 2, name: 'Networking', requiresSpecialist: false },
    assignedAgent: { id: 1, fullName: 'Bob Network' },
    createdAtUtc: '2026-10-16T08:00:00Z',
    updatedAtUtc: '2026-10-16T09:00:00Z',
    dueAtUtc: '2026-10-17T08:00:00Z',
    resolvedAtUtc: null,
    slaStatus: 'WithinSla',
  };

  const initialEscalations: TicketEscalation[] = [];

  beforeEach(() => {
    vi.clearAllMocks();
    mockUseReferenceData.mockReturnValue({
      data: {
        customers: [],
        categories: [],
        agents: [
          {
            id: 1,
            fullName: 'Bob Network',
            email: 'bob@example.com',
            isActive: true,
            maxOpenTickets: 5,
            openTicketCount: 1,
            specializations: [],
          },
        ],
      },
      isLoading: false,
      error: undefined,
      reload: vi.fn(),
      setData: vi.fn(),
    });

    mockGetTicket.mockResolvedValue(initialTicket);
    mockGetEscalations.mockResolvedValue(initialEscalations);
  });

  it('renders ticket details, SLA indicator, escalation panel, and empty escalation history', async () => {
    render(
      <MemoryRouter initialEntries={['/tickets/15']}>
        <Routes>
          <Route path="/tickets/:id" element={<TicketDetailPage />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'VPN gateway latency spike' })).toBeInTheDocument();
    expect(screen.getByText(/TCK-0015/)).toBeInTheDocument();

    // Summary panel contains SLA indicator
    expect(screen.getByText('Within SLA')).toBeInTheDocument();

    // Escalation panel
    expect(screen.getByRole('heading', { level: 2, name: 'Escalate Ticket' })).toBeInTheDocument();

    // Escalation history
    expect(screen.getByRole('heading', { level: 2, name: 'Escalation History' })).toBeInTheDocument();
    expect(screen.getByText('No escalation history for this ticket.')).toBeInTheDocument();
  });

  it('refreshes ticket details and escalation history after successful escalation without full-page reload', async () => {
    const updatedTicket: TicketDetail = {
      ...initialTicket,
      priority: 'High',
      updatedAtUtc: '2026-10-16T10:00:00Z',
      dueAtUtc: '2026-10-16T16:00:00Z',
    };

    const newEscalation: TicketEscalation = {
      id: 101,
      ticketId: 15,
      escalatedBy: 'Support Lead',
      reason: 'Outage escalating across branch offices',
      escalatedAtUtc: '2026-10-16T10:00:00Z',
      fromPriority: 'Medium',
      toPriority: 'High',
      fromAgentId: 1,
      toAgentId: 1,
      fromDueAtUtc: '2026-10-17T08:00:00Z',
      toDueAtUtc: '2026-10-16T16:00:00Z',
    };

    const escalationResponse: EscalationResponse = {
      ticket: updatedTicket,
      escalation: newEscalation,
    };

    mockEscalate.mockResolvedValue(escalationResponse);
    // When history is reloaded after escalation
    mockGetEscalations
      .mockResolvedValueOnce(initialEscalations)
      .mockResolvedValueOnce([newEscalation]);

    render(
      <MemoryRouter initialEntries={['/tickets/15']}>
        <Routes>
          <Route path="/tickets/:id" element={<TicketDetailPage />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'VPN gateway latency spike' })).toBeInTheDocument();

    // Fill escalation form
    fireEvent.change(screen.getByLabelText(/escalated by/i), {
      target: { value: 'Support Lead' },
    });
    fireEvent.change(screen.getByLabelText(/reason for escalation/i), {
      target: { value: 'Outage escalating across branch offices' },
    });

    fireEvent.click(screen.getByRole('button', { name: /escalate to high/i }));

    await waitFor(() => {
      expect(mockEscalate).toHaveBeenCalledWith(15, {
        escalatedBy: 'Support Lead',
        reason: 'Outage escalating across branch offices',
      });
    });

    // History is refreshed with the new entry
    await waitFor(() => {
      expect(screen.getByText('Outage escalating across branch offices')).toBeInTheDocument();
    });

    expect(screen.getByText('Support Lead')).toBeInTheDocument();
    // Priority updated in summary
    expect(screen.getAllByText('High').length).toBeGreaterThan(0);
  });

  it('rejects an escalation reason shorter than the server minimum client-side without calling API or modifying ticket', async () => {
    render(
      <MemoryRouter initialEntries={['/tickets/15']}>
        <Routes>
          <Route path="/tickets/:id" element={<TicketDetailPage />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'VPN gateway latency spike' })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/escalated by/i), {
      target: { value: 'Support Lead' },
    });
    fireEvent.change(screen.getByLabelText(/reason for escalation/i), {
      target: { value: 'help' }, // 4 chars, minimum is 5
    });

    fireEvent.click(screen.getByRole('button', { name: /escalate to high/i }));

    expect(
      await screen.findByText('Escalation reason must be at least 5 characters.'),
    ).toBeInTheDocument();
    expect(mockEscalate).not.toHaveBeenCalled();
    // Ticket priority remains Medium in summary
    const priorityDd1 = screen.getByText('Priority').closest('div')?.querySelector('dd');
    expect(priorityDd1).toHaveTextContent('Medium');
    expect(priorityDd1).not.toHaveTextContent('High');
  });

  it('displays a 409 response clearly and does not falsely update the ticket as successfully escalated', async () => {
    mockEscalate.mockRejectedValue(
      new ApiError(409, 'Ticket cannot be escalated because it is already Critical.'),
    );

    render(
      <MemoryRouter initialEntries={['/tickets/15']}>
        <Routes>
          <Route path="/tickets/:id" element={<TicketDetailPage />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'VPN gateway latency spike' })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/escalated by/i), {
      target: { value: 'Support Lead' },
    });
    fireEvent.change(screen.getByLabelText(/reason for escalation/i), {
      target: { value: 'Escalate to higher tier for urgent resolution' },
    });

    fireEvent.click(screen.getByRole('button', { name: /escalate to high/i }));

    // 409 error message is clearly displayed
    expect(
      await screen.findByText('Ticket cannot be escalated because it is already Critical.'),
    ).toBeInTheDocument();

    // Priority remains Medium in summary (not updated to High)
    const priorityDd2 = screen.getByText('Priority').closest('div')?.querySelector('dd');
    expect(priorityDd2).toHaveTextContent('Medium');
    expect(priorityDd2).not.toHaveTextContent('High');

    // Escalation history still shows empty state
    expect(screen.getByText('No escalation history for this ticket.')).toBeInTheDocument();
  });
});
