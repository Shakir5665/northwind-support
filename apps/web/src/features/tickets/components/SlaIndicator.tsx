import { Badge, type BadgeTone } from '../../../components/Badge';
import type { SlaStatus } from '../../../types/api';

const slaConfig: Record<SlaStatus, { text: string; tone: BadgeTone; icon: string }> = {
  WithinSla: { text: 'Within SLA', tone: 'info', icon: '✓' },
  AtRisk: { text: 'At risk', tone: 'warning', icon: '⚠' },
  Breached: { text: 'Breached', tone: 'danger', icon: '✕' },
  Met: { text: 'Met', tone: 'success', icon: '✓' },
  NotApplicable: { text: 'No SLA', tone: 'neutral', icon: '—' },
};

export interface SlaIndicatorProps {
  status: SlaStatus;
}

/**
 * Reusable, accessible SLA indicator representing the five domain SLA states.
 * Uses both explicit text labels, shapes/symbols, and badge tones to avoid relying solely on color.
 */
export function SlaIndicator({ status }: SlaIndicatorProps) {
  const config = slaConfig[status] ?? {
    text: status,
    tone: 'neutral' as BadgeTone,
    icon: '•',
  };

  return (
    <span className="sla-indicator" aria-label={`SLA status: ${config.text}`}>
      <Badge tone={config.tone}>
        <span aria-hidden="true" className="sla-indicator__icon">{config.icon}</span>{' '}
        {config.text}
      </Badge>
    </span>
  );
}
