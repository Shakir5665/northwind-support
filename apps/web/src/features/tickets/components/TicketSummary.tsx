import { formatDateTime, formatRelativeToNow } from '../../../lib/format';
import type { TicketDetail } from '../../../types/api';
import { SlaIndicator } from './SlaIndicator';
import { PriorityBadge, StatusBadge } from './TicketBadges';

interface TicketSummaryProps {
  ticket: TicketDetail;
}

/** The read-only facts about a ticket. */
export function TicketSummary({ ticket }: TicketSummaryProps) {
  return (
    <div className="card">
      <dl className="summary">
        <div>
          <dt>Priority</dt>
          <dd>
            <PriorityBadge priority={ticket.priority} />
          </dd>
        </div>

        <div>
          <dt>Status</dt>
          <dd>
            <StatusBadge status={ticket.status} />
          </dd>
        </div>

        <div>
          <dt>Assigned agent</dt>
          <dd>{ticket.assignedAgent?.fullName ?? 'Unassigned'}</dd>
        </div>

        <div>
          <dt>SLA</dt>
          <dd>
            <SlaIndicator status={ticket.slaStatus} />
            {ticket.dueAtUtc && (
              <span className="summary__note">Due {formatDateTime(ticket.dueAtUtc)} ({formatRelativeToNow(ticket.dueAtUtc)})</span>
            )}
          </dd>
        </div>

        <div>
          <dt>Customer</dt>
          <dd>
            {ticket.customer.name}
            {ticket.customer.tier === 'Premium' && <span className="tier-tag">Premium</span>}
            <span className="summary__note">{ticket.customer.email}</span>
            {ticket.customer.phone && <span className="summary__note">{ticket.customer.phone}</span>}
          </dd>
        </div>

        <div>
          <dt>Category</dt>
          <dd>
            {ticket.category.name}
            {ticket.category.requiresSpecialist && (
              <span className="summary__note">Needs a specialist</span>
            )}
          </dd>
        </div>

        <div>
          <dt>Created</dt>
          <dd>{formatDateTime(ticket.createdAtUtc)}</dd>
        </div>

        <div>
          <dt>Last updated</dt>
          <dd>{formatDateTime(ticket.updatedAtUtc)}</dd>
        </div>

        {ticket.resolvedAtUtc && (
          <div>
            <dt>Resolved</dt>
            <dd>{formatDateTime(ticket.resolvedAtUtc)}</dd>
          </div>
        )}
      </dl>
    </div>
  );
}
