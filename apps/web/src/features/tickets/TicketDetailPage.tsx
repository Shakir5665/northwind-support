import { Link, useParams } from 'react-router-dom';
import { ErrorMessage } from '../../components/ErrorMessage';
import { Spinner } from '../../components/Spinner';
import { useReferenceData } from '../../hooks/useReferenceData';
import type { EscalationResponse } from '../../types/api';
import { AssignAgentControl } from './components/AssignAgentControl';
import { EscalationHistory } from './components/EscalationHistory';
import { EscalationPanel } from './components/EscalationPanel';
import { StatusActions } from './components/StatusActions';
import { TicketSummary } from './components/TicketSummary';
import { useTicket } from './hooks/useTicket';
import { useTicketEscalations } from './hooks/useTicketEscalations';

export function TicketDetailPage() {
  const { id } = useParams<{ id: string }>();
  const ticketId = Number(id);

  const { data: ticket, isLoading, error, reload, setData } = useTicket(ticketId);
  const escalations = useTicketEscalations(ticketId);
  const reference = useReferenceData();

  if (Number.isNaN(ticketId)) {
    return <ErrorMessage message="That is not a valid ticket reference." />;
  }

  if (isLoading && !ticket) {
    return <Spinner label="Loading ticket" />;
  }

  if (error && !ticket) {
    return <ErrorMessage message={error} onRetry={() => void reload()} />;
  }

  if (!ticket) {
    return null;
  }

  const handleEscalated = (response: EscalationResponse) => {
    setData(response.ticket);
    void escalations.reload();
  };

  return (
    <section>
      <header className="page-header">
        <div>
          <p className="breadcrumb">
            <Link to="/tickets">Tickets</Link> / {ticket.reference}
          </p>
          <h1>{ticket.title}</h1>
        </div>
      </header>

      {error && <ErrorMessage message={error} onRetry={() => void reload()} />}

      <div className="detail-layout">
        <div className="detail-layout__main">
          <div className="card">
            <h2>Description</h2>
            <p className="description">{ticket.description}</p>
          </div>

          <StatusActions ticket={ticket} onUpdated={setData} />

          <EscalationPanel ticket={ticket} onEscalated={handleEscalated} />

          <EscalationHistory
            escalations={escalations.data}
            agents={reference.data?.agents ?? []}
            isLoading={escalations.isLoading}
            error={escalations.error}
            onRetry={() => void escalations.reload()}
          />
        </div>

        <aside className="detail-layout__side">
          <TicketSummary ticket={ticket} />

          <AssignAgentControl
            ticket={ticket}
            agents={reference.data?.agents ?? []}
            onUpdated={setData}
          />
        </aside>
      </div>
    </section>
  );
}
