import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  EscalateTicketPayload,
  EscalationResponse,
  PagedResult,
  TicketEscalation,
  TicketFilters,
  TicketListItem,
} from '../types/api';
import * as client from './client';
import { ticketsApi } from './tickets';

describe('ticketsApi', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  const baseFilters: TicketFilters = {
    page: 1,
    pageSize: 20,
    search: '',
    status: undefined,
    priority: undefined,
    categoryId: undefined,
    customerId: undefined,
    assignedAgentId: undefined,
    unassignedOnly: false,
    slaStatus: undefined,
    sortBy: 'createdAtUtc',
    sortDirection: 'desc',
  };

  it('includes slaStatus query parameter when specified in getTickets', async () => {
    const requestSpy = vi
      .spyOn(client, 'request')
      .mockResolvedValue({} as PagedResult<TicketListItem>);

    await ticketsApi.getTickets({
      ...baseFilters,
      slaStatus: 'AtRisk',
    });

    expect(requestSpy).toHaveBeenCalledTimes(1);
    const calledUrl = requestSpy.mock.calls[0][0];
    expect(calledUrl).toContain('slaStatus=AtRisk');
  });

  it('omits slaStatus from query parameters when undefined or empty in getTickets', async () => {
    const requestSpy = vi
      .spyOn(client, 'request')
      .mockResolvedValue({} as PagedResult<TicketListItem>);

    await ticketsApi.getTickets({
      ...baseFilters,
      slaStatus: undefined,
    });

    expect(requestSpy).toHaveBeenCalledTimes(1);
    const calledUrl = requestSpy.mock.calls[0][0];
    expect(calledUrl).not.toContain('slaStatus');
  });

  it('calls POST /tickets/{id}/escalate with the correct payload in escalate()', async () => {
    const requestSpy = vi
      .spyOn(client, 'request')
      .mockResolvedValue({} as EscalationResponse);
    const payload: EscalateTicketPayload = {
      reason: 'Customer reported critical outage',
      escalatedBy: 'Support Lead',
    };

    await ticketsApi.escalate(42, payload);

    expect(requestSpy).toHaveBeenCalledWith('/tickets/42/escalate', {
      method: 'POST',
      body: JSON.stringify(payload),
    });
  });

  it('calls GET /tickets/{id}/escalations in getEscalations()', async () => {
    const requestSpy = vi
      .spyOn(client, 'request')
      .mockResolvedValue([] as TicketEscalation[]);

    await ticketsApi.getEscalations(42);

    expect(requestSpy).toHaveBeenCalledWith('/tickets/42/escalations');
  });
});
