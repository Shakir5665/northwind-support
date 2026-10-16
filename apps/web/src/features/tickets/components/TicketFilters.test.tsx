import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { Agent, Category, CustomerListItem, TicketFilters as TicketFiltersType } from '../../../types/api';
import { TicketFilters } from './TicketFilters';

describe('TicketFilters', () => {
  const defaultFilters: TicketFiltersType = {
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
    page: 1,
    pageSize: 20,
  };

  const categories: Category[] = [
    { id: 1, name: 'Billing', requiresSpecialist: false, isActive: true, forcesCriticalPriority: false },
    { id: 2, name: 'Technical Support', requiresSpecialist: true, isActive: true, forcesCriticalPriority: false },
  ];

  const customers: CustomerListItem[] = [
    { id: 1, name: 'Northwind Traders', email: 'support@northwind.com', phone: null, tier: 'Premium', openTicketCount: 1 },
  ];

  const agents: Agent[] = [
    { id: 1, fullName: 'Alice Specialist', email: 'alice@example.com', isActive: true, maxOpenTickets: 5, openTicketCount: 1, specializations: [] },
  ];

  it('renders all SLA status options with user-friendly labels', () => {
    render(
      <TicketFilters
        filters={defaultFilters}
        categories={categories}
        customers={customers}
        agents={agents}
        onChange={vi.fn()}
        onReset={vi.fn()}
      />,
    );

    const slaSelect = screen.getByLabelText('SLA status');
    expect(slaSelect).toBeInTheDocument();

    const options = Array.from((slaSelect as HTMLSelectElement).options).map(
      (opt) => opt.textContent,
    );

    expect(options).toEqual([
      'All SLA statuses',
      'Within SLA',
      'At risk',
      'Breached',
      'Met',
      'No SLA',
    ]);
  });

  it('calls onChange with the selected SLA status', () => {
    const onChange = vi.fn();
    render(
      <TicketFilters
        filters={defaultFilters}
        categories={categories}
        customers={customers}
        agents={agents}
        onChange={onChange}
        onReset={vi.fn()}
      />,
    );

    const slaSelect = screen.getByLabelText('SLA status');
    fireEvent.change(slaSelect, { target: { value: 'AtRisk' } });

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith({ slaStatus: 'AtRisk' });
  });

  it('calls onChange with undefined when clearing the SLA filter', () => {
    const onChange = vi.fn();
    render(
      <TicketFilters
        filters={{ ...defaultFilters, slaStatus: 'Breached' }}
        categories={categories}
        customers={customers}
        agents={agents}
        onChange={onChange}
        onReset={vi.fn()}
      />,
    );

    const slaSelect = screen.getByLabelText('SLA status');
    expect((slaSelect as HTMLSelectElement).value).toBe('Breached');

    fireEvent.change(slaSelect, { target: { value: '' } });

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith({ slaStatus: undefined });
  });

  it('combines SLA filter with existing filters', () => {
    const onChange = vi.fn();
    render(
      <TicketFilters
        filters={{ ...defaultFilters, status: 'Open', priority: 'High', slaStatus: 'WithinSla' }}
        categories={categories}
        customers={customers}
        agents={agents}
        onChange={onChange}
        onReset={vi.fn()}
      />,
    );

    const slaSelect = screen.getByLabelText('SLA status');
    expect((slaSelect as HTMLSelectElement).value).toBe('WithinSla');

    fireEvent.change(slaSelect, { target: { value: 'Met' } });

    expect(onChange).toHaveBeenCalledWith({ slaStatus: 'Met' });
  });
});
