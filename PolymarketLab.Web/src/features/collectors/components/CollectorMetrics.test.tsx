// @vitest-environment jsdom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { formatCounter } from '../../../shared/formatters/formatCounter';
import { formatLocalDate } from '../../../shared/formatters/formatLocalDate';
import type { CollectorSession } from '../model/collectorSession';
import { CollectorMetrics } from './CollectorMetrics';

describe('CollectorMetrics', () => {
  it('renders formatted counters, last message time, and an unpersisted warning', () => {
    const session = createSession({
      messagesReceived: 1234,
      messagesPersisted: 1200,
      reconnectCount: 2,
      lastMessageAt: '2026-08-06T12:10:00Z',
    });

    render(<CollectorMetrics session={session} />);

    expect(metricValue('Messages received')).toBe(formatCounter(1234));
    expect(metricValue('Messages persisted')).toBe(formatCounter(1200));
    expect(metricValue('Unpersisted')).toBe('34');
    expect(metricValue('Reconnect count')).toBe('2');
    expect(metricValue('Последнее сообщение')).toBe(formatLocalDate(session.lastMessageAt));
    expect(screen.getByRole('status').textContent).toContain('Ожидают сохранения: 34');
  });

  it('confirms persistence after a correct stop', () => {
    render(<CollectorMetrics session={createSession({ status: 'Stopped' })} />);

    expect(screen.getByRole('status').textContent).toContain(
      'все полученные сообщения сохранены',
    );
  });

  it('keeps the warning and does not confirm an incomplete stop', () => {
    render(<CollectorMetrics session={createSession({
      status: 'Stopped',
      messagesReceived: 120,
      messagesPersisted: 118,
    })} />);

    expect(screen.getByRole('status').textContent).toContain('Ожидают сохранения: 2');
    expect(screen.queryByText(/Проверка завершения пройдена/)).toBeNull();
  });

  it('reports inconsistent counters instead of hiding the negative difference', () => {
    render(<CollectorMetrics session={createSession({
      messagesReceived: 118,
      messagesPersisted: 120,
    })} />);

    expect(metricValue('Unpersisted')).toBe('-2');
    expect(screen.getByRole('alert').textContent).toContain(
      'сохранено больше сообщений, чем получено',
    );
  });

  it('renders a missing last message as a dash without confirming a failed session', () => {
    render(<CollectorMetrics session={createSession({ status: 'Failed', lastMessageAt: null })} />);

    expect(screen.getByText('-')).toBeTruthy();
    expect(screen.queryByRole('status')).toBeNull();
  });
});

function metricValue(label: string): string | null | undefined {
  return screen.getByText(label).parentElement?.querySelector('dd')?.textContent;
}

function createSession(overrides: Partial<CollectorSession> = {}): CollectorSession {
  return {
    sessionId: 'session-id',
    marketId: 'market-id',
    status: 'Running',
    createdAt: '2026-08-06T12:00:00Z',
    startedAt: '2026-08-06T12:00:01Z',
    stoppedAt: null,
    failureCode: null,
    failureMessage: null,
    messagesReceived: 120,
    messagesPersisted: 120,
    lastMessageAt: '2026-08-06T12:09:59Z',
    reconnectCount: 0,
    ...overrides,
  };
}
