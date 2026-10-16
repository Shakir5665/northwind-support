import { useCallback } from 'react';
import { ticketsApi } from '../../../api/tickets';
import { useAsyncData } from '../../../hooks/useAsyncData';
import type { TicketEscalation } from '../../../types/api';

/** Loads escalation history for a ticket, refetching whenever ticketId changes. */
export function useTicketEscalations(ticketId: number) {
  const load = useCallback(() => ticketsApi.getEscalations(ticketId), [ticketId]);

  return useAsyncData<TicketEscalation[]>(load, 'Unable to load escalation history.');
}
