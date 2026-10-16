import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../../api/tickets';
import { page, ticketListItem } from '../../../test/fixtures';
import { useTicketList } from './useTicketList';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: {
    getTickets: vi.fn(),
  },
}));

const getTickets = vi.mocked(ticketsApi.getTickets);

describe('useTicketList', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    getTickets.mockResolvedValue(page([ticketListItem()]));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('searches with the text the user typed, once the typing settles', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.updateFilters({ search: 'invoice' });
    });

    // Still inside the debounce window: no second request yet.
    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'invoice', page: 1 }),
    );

    expect(result.current.isLoading).toBe(false);
    expect(result.current.data?.items).toHaveLength(1);
  });

  it('refetches immediately when a non-search filter such as status changes', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await Promise.resolve();
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      result.current.updateFilters({ status: 'Open' });
    });

    // Triggers immediately without needing advanceTimersByTimeAsync
    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: 'Open', page: 1 }),
    );
  });

  it('refetches immediately and resets page to 1 when a filter changes from a later page', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await Promise.resolve();
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      result.current.updateFilters({ page: 2 });
    });

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 2 }),
    );

    await act(async () => {
      result.current.updateFilters({ priority: 'High' });
    });

    expect(getTickets).toHaveBeenCalledTimes(3);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ priority: 'High', page: 1 }),
    );
  });

  it('preserves debounced search while immediately refetching on non-search filter change', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await Promise.resolve();
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.updateFilters({ search: 'order' });
    });

    // Still debouncing search: no immediate request
    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'order' }),
    );

    // Changing status now triggers immediately and keeps the active search filter
    await act(async () => {
      result.current.updateFilters({ status: 'Resolved' });
    });

    expect(getTickets).toHaveBeenCalledTimes(3);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'order', status: 'Resolved', page: 1 }),
    );
  });

  it('refetches immediately when filters are reset', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await Promise.resolve();
    });

    await act(async () => {
      result.current.updateFilters({ status: 'Closed' });
    });

    expect(getTickets).toHaveBeenCalledTimes(2);

    await act(async () => {
      result.current.resetFilters();
    });

    expect(getTickets).toHaveBeenCalledTimes(3);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: undefined, search: '', page: 1 }),
    );
  });

  it('refetches immediately when slaStatus changes and resets page to 1', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await Promise.resolve();
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      result.current.updateFilters({ page: 3 });
    });

    expect(getTickets).toHaveBeenCalledTimes(2);

    await act(async () => {
      result.current.updateFilters({ slaStatus: 'AtRisk' });
    });

    expect(getTickets).toHaveBeenCalledTimes(3);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ slaStatus: 'AtRisk', page: 1 }),
    );
  });

  it('combines slaStatus with other filters and preserves them when clearing slaStatus', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await Promise.resolve();
    });

    await act(async () => {
      result.current.updateFilters({ priority: 'High', slaStatus: 'Breached' });
    });

    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ priority: 'High', slaStatus: 'Breached', page: 1 }),
    );

    // Clear slaStatus
    await act(async () => {
      result.current.updateFilters({ slaStatus: undefined });
    });

    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ priority: 'High', slaStatus: undefined, page: 1 }),
    );
  });
});
