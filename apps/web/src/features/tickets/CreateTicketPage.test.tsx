import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../api/tickets';
import { useReferenceData } from '../../hooks/useReferenceData';
import type { TicketDetail } from '../../types/api';
import { CreateTicketPage } from './CreateTicketPage';

vi.mock('../../hooks/useReferenceData', () => ({
  useReferenceData: vi.fn(),
}));

vi.mock('../../api/tickets', () => ({
  ticketsApi: {
    createTicket: vi.fn(),
  },
}));

const mockUseReferenceData = vi.mocked(useReferenceData);
const mockCreateTicket = vi.mocked(ticketsApi.createTicket);

describe('CreateTicketPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseReferenceData.mockReturnValue({
      data: {
        customers: [
          {
            id: 1,
            name: 'Northwind Traders',
            email: 'support@northwind.com',
            phone: null,
            tier: 'Premium',
            openTicketCount: 0,
          },
        ],
        categories: [
          {
            id: 10,
            name: 'Database Issues',
            isActive: true,
            requiresSpecialist: true,
            forcesCriticalPriority: false,
          },
        ],
        agents: [],
      },
      isLoading: false,
      error: undefined,
      reload: vi.fn(),
      setData: vi.fn(),
    });
  });

  const assignedResponse: TicketDetail = {
    id: 99,
    reference: 'TCK-0099',
    title: 'High CPU on production server',
    description: 'CPU utilization has spiked to 100% since noon',
    status: 'Open',
    priority: 'Critical',
    customer: { id: 1, name: 'Northwind Traders', email: 'support@northwind.com', phone: null, tier: 'Premium' },
    category: { id: 10, name: 'Database Issues', requiresSpecialist: true },
    assignedAgent: { id: 3, fullName: 'Grace Hopper' },
    createdAtUtc: '2026-10-16T12:00:00Z',
    updatedAtUtc: '2026-10-16T12:00:00Z',
    dueAtUtc: '2026-10-16T16:00:00Z',
    resolvedAtUtc: null,
    slaStatus: 'WithinSla',
    triage: {
      priorityReason: 'Category has high base priority and customer is Enterprise',
      slaReason: 'Critical tickets have a 4 hour SLA response window',
      assignmentReason: 'Grace Hopper is a Database specialist with lowest active workload (1 ticket)',
    },
  };

  const unassignedResponse: TicketDetail = {
    id: 100,
    reference: 'TCK-0100',
    title: 'Legacy system connectivity error',
    description: 'Cannot connect to legacy gateway from internal network',
    status: 'Open',
    priority: 'Medium',
    customer: { id: 1, name: 'Northwind Traders', email: 'support@northwind.com', phone: null, tier: 'Premium' },
    category: { id: 10, name: 'Database Issues', requiresSpecialist: true },
    assignedAgent: null,
    createdAtUtc: '2026-10-16T12:00:00Z',
    updatedAtUtc: '2026-10-16T12:00:00Z',
    dueAtUtc: '2026-10-17T12:00:00Z',
    resolvedAtUtc: null,
    slaStatus: 'WithinSla',
    triage: {
      priorityReason: 'Standard priority assigned for general issues',
      slaReason: 'Medium tickets have a 24 hour SLA window',
      assignmentReason: 'No qualified specialist was currently available or active',
    },
  };

  it('displays the triage decision after creating an assigned ticket', async () => {
    mockCreateTicket.mockResolvedValue(assignedResponse);

    render(
      <MemoryRouter>
        <CreateTicketPage />
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByLabelText(/title/i), {
      target: { value: 'High CPU on production server' },
    });
    fireEvent.change(screen.getByLabelText(/description/i), {
      target: { value: 'CPU utilization has spiked to 100% since noon' },
    });
    fireEvent.change(screen.getByLabelText(/customer/i), {
      target: { value: '1' },
    });
    fireEvent.change(screen.getByLabelText(/category/i), {
      target: { value: '10' },
    });

    fireEvent.click(screen.getByRole('button', { name: /raise ticket/i }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toBeInTheDocument();
    });

    // Confirmation header
    expect(screen.getByText(/Raised/)).toBeInTheDocument();
    expect(screen.getByText('TCK-0099')).toBeInTheDocument();

    // Triage decision details
    expect(screen.getByText('Critical')).toBeInTheDocument();
    expect(
      screen.getByText('Category has high base priority and customer is Enterprise'),
    ).toBeInTheDocument();

    expect(
      screen.getByText('Critical tickets have a 4 hour SLA response window'),
    ).toBeInTheDocument();

    expect(screen.getByText('Grace Hopper')).toBeInTheDocument();
    expect(
      screen.getByText(
        'Grace Hopper is a Database specialist with lowest active workload (1 ticket)',
      ),
    ).toBeInTheDocument();

    expect(screen.getByRole('button', { name: 'Open TCK-0099' })).toBeInTheDocument();
  });

  it('displays the triage decision for an unassigned ticket with explanation', async () => {
    mockCreateTicket.mockResolvedValue(unassignedResponse);

    render(
      <MemoryRouter>
        <CreateTicketPage />
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByLabelText(/title/i), {
      target: { value: 'Legacy system connectivity error' },
    });
    fireEvent.change(screen.getByLabelText(/description/i), {
      target: { value: 'Cannot connect to legacy gateway from internal network' },
    });
    fireEvent.change(screen.getByLabelText(/customer/i), {
      target: { value: '1' },
    });
    fireEvent.change(screen.getByLabelText(/category/i), {
      target: { value: '10' },
    });

    fireEvent.click(screen.getByRole('button', { name: /raise ticket/i }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toBeInTheDocument();
    });

    expect(screen.getByText('Unassigned')).toBeInTheDocument();
    expect(
      screen.getByText('No qualified specialist was currently available or active'),
    ).toBeInTheDocument();
  });

  it('allows raising another ticket after successful creation', async () => {
    mockCreateTicket.mockResolvedValue(assignedResponse);

    render(
      <MemoryRouter>
        <CreateTicketPage />
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByLabelText(/title/i), {
      target: { value: 'High CPU on production server' },
    });
    fireEvent.change(screen.getByLabelText(/description/i), {
      target: { value: 'CPU utilization has spiked to 100% since noon' },
    });
    fireEvent.change(screen.getByLabelText(/customer/i), {
      target: { value: '1' },
    });
    fireEvent.change(screen.getByLabelText(/category/i), {
      target: { value: '10' },
    });

    fireEvent.click(screen.getByRole('button', { name: /raise ticket/i }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /raise another/i }));

    // Back to form
    expect(screen.getByLabelText(/title/i)).toBeInTheDocument();
  });
});
