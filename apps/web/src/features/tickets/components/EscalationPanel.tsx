import { useState } from 'react';
import { toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { Field } from '../../../components/Field';
import type { EscalationResponse, TicketDetail, TicketPriority } from '../../../types/api';
import { PriorityBadge } from './TicketBadges';

const nextPriorityMap: Record<TicketPriority, TicketPriority | null> = {
  Low: 'Medium',
  Medium: 'High',
  High: 'Critical',
  Critical: null,
};

interface EscalationPanelProps {
  ticket: TicketDetail;
  onEscalated: (response: EscalationResponse) => void;
}

/**
 * Panel on the ticket detail page to escalate a ticket's priority.
 * Enforces client-side reason/actor constraints and eligibility rules (BR-7).
 */
export function EscalationPanel({ ticket, onEscalated }: EscalationPanelProps) {
  const [reason, setReason] = useState('');
  const [escalatedBy, setEscalatedBy] = useState('');
  const [errors, setErrors] = useState<{ reason?: string; escalatedBy?: string }>({});
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string>();

  const nextPriority = nextPriorityMap[ticket.priority];

  // Invariant checks (Requirement F)
  let unavailableReason: string | null = null;
  if (ticket.status === 'Resolved') {
    unavailableReason = 'Resolved tickets cannot be escalated.';
  } else if (ticket.status === 'Closed') {
    unavailableReason = 'Closed tickets cannot be escalated.';
  } else if (ticket.priority === 'Critical' || !nextPriority) {
    unavailableReason = 'Tickets at Critical priority cannot be escalated further.';
  }

  const isAvailable = unavailableReason === null;

  const validate = () => {
    const newErrors: { reason?: string; escalatedBy?: string } = {};

    const trimmedReason = reason.trim();
    if (trimmedReason.length < 5) {
      newErrors.reason = 'Escalation reason must be at least 5 characters.';
    } else if (trimmedReason.length > 500) {
      newErrors.reason = 'Escalation reason cannot exceed 500 characters.';
    }

    const trimmedActor = escalatedBy.trim();
    if (!trimmedActor) {
      newErrors.escalatedBy = 'Please provide the name of the person escalating.';
    } else if (trimmedActor.length > 100) {
      newErrors.escalatedBy = 'Name cannot exceed 100 characters.';
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!isAvailable || isSubmitting) {
      return;
    }

    if (!validate()) {
      return;
    }

    setIsSubmitting(true);
    setServerError(undefined);

    try {
      const response = await ticketsApi.escalate(ticket.id, {
        reason: reason.trim(),
        escalatedBy: escalatedBy.trim(),
      });

      setReason('');
      setEscalatedBy('');
      setErrors({});
      onEscalated(response);
    } catch (caught) {
      setServerError(toErrorMessage(caught, 'Could not escalate the ticket.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="card escalation-panel">
      <h2>Escalate Ticket</h2>
      {isAvailable && nextPriority ? (
        <p className="field__hint">
          Escalating will raise priority from <PriorityBadge priority={ticket.priority} /> to{' '}
          <PriorityBadge priority={nextPriority} />, recompute the SLA response window, and re-evaluate agent eligibility.
        </p>
      ) : (
        <p className="field__hint" role="note">
          {unavailableReason}
        </p>
      )}

      {isAvailable && (
        <form onSubmit={handleSubmit} noValidate>
          <Field
            id="escalate-reason"
            label="Reason for escalation"
            error={errors.reason}
            hint="5 to 500 characters explaining the urgency."
          >
            {(fieldProps) => (
              <textarea
                {...fieldProps}
                rows={3}
                value={reason}
                maxLength={500}
                disabled={isSubmitting}
                onChange={(e) => {
                  setReason(e.target.value);
                  if (errors.reason) {
                    setErrors((prev) => ({ ...prev, reason: undefined }));
                  }
                }}
              />
            )}
          </Field>

          <Field
            id="escalate-by"
            label="Escalated by"
            error={errors.escalatedBy}
            hint="Your name or team role (max 100 characters)."
          >
            {(fieldProps) => (
              <input
                {...fieldProps}
                type="text"
                value={escalatedBy}
                maxLength={100}
                disabled={isSubmitting}
                onChange={(e) => {
                  setEscalatedBy(e.target.value);
                  if (errors.escalatedBy) {
                    setErrors((prev) => ({ ...prev, escalatedBy: undefined }));
                  }
                }}
              />
            )}
          </Field>

          {serverError && (
            <p className="field__error" role="alert">
              {serverError}
            </p>
          )}

          <div className="button-row">
            <button
              type="submit"
              className="button button--primary"
              disabled={isSubmitting}
            >
              {isSubmitting ? 'Escalating...' : `Escalate to ${nextPriority}`}
            </button>
          </div>
        </form>
      )}
    </div>
  );
}
