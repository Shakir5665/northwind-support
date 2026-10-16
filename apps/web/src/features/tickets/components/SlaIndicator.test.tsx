import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SlaStatus } from '../../../types/api';
import { SlaIndicator } from './SlaIndicator';

describe('SlaIndicator', () => {
  const testCases: { status: SlaStatus; expectedText: string; expectedTone: string }[] = [
    { status: 'WithinSla', expectedText: 'Within SLA', expectedTone: 'badge--info' },
    { status: 'AtRisk', expectedText: 'At risk', expectedTone: 'badge--warning' },
    { status: 'Breached', expectedText: 'Breached', expectedTone: 'badge--danger' },
    { status: 'Met', expectedText: 'Met', expectedTone: 'badge--success' },
    { status: 'NotApplicable', expectedText: 'No SLA', expectedTone: 'badge--neutral' },
  ];

  testCases.forEach(({ status, expectedText, expectedTone }) => {
    it(`renders correctly for status "${status}" with label "${expectedText}" and tone "${expectedTone}"`, () => {
      const { container } = render(<SlaIndicator status={status} />);

      // Visible text
      expect(screen.getByText(expectedText)).toBeInTheDocument();

      // Accessible aria-label
      const indicator = screen.getByLabelText(`SLA status: ${expectedText}`);
      expect(indicator).toBeInTheDocument();

      // Badge tone class
      const badge = container.querySelector('.badge');
      expect(badge).toHaveClass(expectedTone);
    });
  });

  it('renders all five states with unique textual labels so state is not conveyed by color alone', () => {
    const labels = new Set<string>();
    const ariaLabels = new Set<string>();

    testCases.forEach(({ status }) => {
      const { container, unmount } = render(<SlaIndicator status={status} />);
      const indicator = container.querySelector('.sla-indicator');
      expect(indicator).not.toBeNull();

      // Must have visible text
      const visibleText = indicator?.textContent?.trim();
      expect(visibleText).toBeTruthy();
      labels.add(visibleText!);

      // Must have accessible label
      const ariaLabel = indicator?.getAttribute('aria-label');
      expect(ariaLabel).toBeTruthy();
      ariaLabels.add(ariaLabel!);

      unmount();
    });

    // All 5 states must produce distinct textual labels
    expect(labels.size).toBe(5);
    expect(ariaLabels.size).toBe(5);
  });
});
