import { ErrorMessage } from '../../../components/ErrorMessage';
import { Spinner } from '../../../components/Spinner';
import { formatDateTime } from '../../../lib/format';
import type { Agent, TicketEscalation } from '../../../types/api';
import { PriorityBadge } from './TicketBadges';

interface EscalationHistoryProps {
  escalations: TicketEscalation[] | undefined;
  agents: Agent[];
  isLoading: boolean;
  error?: string;
  onRetry: () => void;
}

function formatAgent(agentId: number | null, agents: Agent[]): string {
  if (agentId === null) {
    return 'Unassigned';
  }

  const agent = agents.find((a) => a.id === agentId);
  return agent ? agent.fullName : `Agent #${agentId}`;
}

function formatDueDate(dateUtc: string | null): string {
  if (!dateUtc) {
    return 'No due date';
  }

  return formatDateTime(dateUtc);
}

/**
 * Displays persistent audit history of ticket escalations (BR-7).
 * Renders records newest-first with before/after priority, agent, due dates, actor, and reason.
 */
export function EscalationHistory({
  escalations,
  agents,
  isLoading,
  error,
  onRetry,
}: EscalationHistoryProps) {
  return (
    <div className="card escalation-history">
      <h2>Escalation History</h2>

      {isLoading && (!escalations || escalations.length === 0) && (
        <Spinner label="Loading escalation history" />
      )}

      {error && <ErrorMessage message={error} onRetry={onRetry} />}

      {!isLoading && !error && (!escalations || escalations.length === 0) && (
        <p className="summary__note">No escalation history for this ticket.</p>
      )}

      {escalations && escalations.length > 0 && (
        <ol className="history-list" aria-label="Ticket escalation history">
          {escalations.map((entry) => (
            <li key={entry.id} className="history-item">
              <header className="history-item__header">
                <span className="history-item__transition">
                  <PriorityBadge priority={entry.fromPriority} /> &rarr;{' '}
                  <PriorityBadge priority={entry.toPriority} />
                </span>
                <time className="history-item__time" dateTime={entry.escalatedAtUtc}>
                  {formatDateTime(entry.escalatedAtUtc)}
                </time>
              </header>

              <p className="history-item__reason">
                <strong>Reason:</strong> {entry.reason}
              </p>

              <dl className="history-item__details">
                <div>
                  <dt>Escalated by</dt>
                  <dd>{entry.escalatedBy}</dd>
                </div>
                <div>
                  <dt>Assignment</dt>
                  <dd>
                    {formatAgent(entry.fromAgentId, agents)} &rarr;{' '}
                    {formatAgent(entry.toAgentId, agents)}
                  </dd>
                </div>
                <div>
                  <dt>SLA due date</dt>
                  <dd>
                    {formatDueDate(entry.fromDueAtUtc)} &rarr;{' '}
                    {formatDueDate(entry.toDueAtUtc)}
                  </dd>
                </div>
              </dl>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
