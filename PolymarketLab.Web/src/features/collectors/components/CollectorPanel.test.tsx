// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { PropsWithChildren } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  getCollectorById,
  getCollectorByMarketId,
  startCollector,
  stopCollector,
  type CollectorSessionResponse,
} from '../../../api/collectorsApi';
import { ApiError } from '../../../api/apiError';
import { ACTIVE_COLLECTOR_POLL_INTERVAL_MS } from '../model/collectorStatus';
import { CollectorPanel } from './CollectorPanel';

vi.mock('../../../api/collectorsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/collectorsApi')>();

  return {
    ...actual,
    getCollectorById: vi.fn(),
    getCollectorByMarketId: vi.fn(),
    startCollector: vi.fn(),
    stopCollector: vi.fn(),
  };
});

const getCollectorByIdMock = vi.mocked(getCollectorById);
const getCollectorByMarketIdMock = vi.mocked(getCollectorByMarketId);
const startCollectorMock = vi.mocked(startCollector);
const stopCollectorMock = vi.mocked(stopCollector);

describe('CollectorPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts a collector for a market without sessions', async () => {
    getCollectorByMarketIdMock.mockResolvedValue({ session: null });
    startCollectorMock.mockResolvedValue({
      sessionId: 'session-id',
      marketId: 'market-id',
      status: 'Running',
    });
    getCollectorByIdMock.mockResolvedValue({ session: createSession('Running') });
    renderPanel('market-id');

    await screen.findByText(/ещё нет collector sessions/);
    fireEvent.click(screen.getByRole('button', { name: 'Start collector' }));

    await waitFor(() => {
      expect(startCollectorMock).toHaveBeenCalledWith({ marketId: 'market-id' });
    });
    await waitFor(() => {
      expect(getCollectorByIdMock).toHaveBeenCalledWith('session-id', expect.any(AbortSignal));
    });
  });

  it('stops an active collector using its session id', async () => {
    const session = createSession('Running');
    getCollectorByMarketIdMock.mockResolvedValue({ session });
    getCollectorByIdMock
      .mockResolvedValueOnce({ session })
      .mockResolvedValue({ session: { ...session, status: 'Stopped' } });
    stopCollectorMock.mockResolvedValue({ session: { ...session, status: 'Stopped' } });
    renderPanel(session.marketId);

    await screen.findByText('Running');
    fireEvent.click(screen.getByRole('button', { name: 'Stop collector' }));

    await waitFor(() => {
      expect(stopCollectorMock).toHaveBeenCalledWith(session.sessionId);
    });
    expect(await screen.findByText('Stopped')).toBeTruthy();
    expect(getCollectorByIdMock).toHaveBeenCalledTimes(2);
  });

  it('updates counters while polling an active session', async () => {
    vi.useFakeTimers();
    const running = createSession('Running');
    const updated = {
      ...running,
      messagesReceived: 125,
      messagesPersisted: 124,
    };
    getCollectorByMarketIdMock.mockResolvedValue({ session: running });
    getCollectorByIdMock
      .mockResolvedValueOnce({ session: running })
      .mockResolvedValue({ session: updated });
    renderPanel(running.marketId);

    await vi.waitFor(() => expect(getCollectorByIdMock).toHaveBeenCalledTimes(1));
    expect(screen.getByText('120')).toBeTruthy();
    expect(screen.getByText('118')).toBeTruthy();

    await act(() => vi.advanceTimersByTimeAsync(ACTIVE_COLLECTOR_POLL_INTERVAL_MS));

    await vi.waitFor(() => expect(getCollectorByIdMock).toHaveBeenCalledTimes(2));
    expect(screen.getByText('125')).toBeTruthy();
    expect(screen.getByText('124')).toBeTruthy();
  });

  it('polls Stopping until terminal status and then stops polling', async () => {
    vi.useFakeTimers();
    const stopping = createSession('Stopping');
    const stopped = {
      ...stopping,
      status: 'Stopped' as const,
      stoppedAt: '2026-08-06T12:10:00Z',
      messagesPersisted: stopping.messagesReceived,
    };
    getCollectorByMarketIdMock.mockResolvedValue({ session: stopping });
    getCollectorByIdMock
      .mockResolvedValueOnce({ session: stopping })
      .mockResolvedValue({ session: stopped });
    renderPanel(stopping.marketId);

    await vi.waitFor(() => expect(screen.getByText('Stopping')).toBeTruthy());
    expect(getCollectorByIdMock).toHaveBeenCalledTimes(1);

    await act(() => vi.advanceTimersByTimeAsync(ACTIVE_COLLECTOR_POLL_INTERVAL_MS));

    await vi.waitFor(() => expect(screen.getByText('Stopped')).toBeTruthy());
    expect(getCollectorByIdMock).toHaveBeenCalledTimes(2);

    await act(() => vi.advanceTimersByTimeAsync(ACTIVE_COLLECTOR_POLL_INTERVAL_MS * 2));
    expect(getCollectorByIdMock).toHaveBeenCalledTimes(2);
  });

  it('prefers the active session returned by a newer by-market read', async () => {
    const activeSession = {
      ...createSession('Running'),
      sessionId: 'active-session-id',
    };
    getCollectorByMarketIdMock
      .mockResolvedValueOnce({ session: null })
      .mockResolvedValue({ session: activeSession });
    startCollectorMock.mockResolvedValue({
      sessionId: 'stale-start-session-id',
      marketId: 'market-id',
      status: 'Stopped',
    });
    getCollectorByIdMock.mockResolvedValue({ session: activeSession });
    renderPanel('market-id');
    await screen.findByText(/ещё нет collector sessions/);

    fireEvent.click(screen.getByRole('button', { name: 'Start collector' }));

    await waitFor(() => {
      expect(getCollectorByIdMock).toHaveBeenCalledWith(
        activeSession.sessionId,
        expect.any(AbortSignal),
      );
    });
    expect(getCollectorByIdMock).not.toHaveBeenCalledWith(
      'stale-start-session-id',
      expect.any(AbortSignal),
    );
  });

  it('renders Failed and the backend failure', async () => {
    getCollectorByMarketIdMock.mockResolvedValue({
      session: {
        ...createSession('Failed'),
        failureCode: 'collector.websocket',
        failureMessage: 'Socket closed.',
      },
    });
    renderPanel('market-id');

    expect(await screen.findByText('Failed')).toBeTruthy();
    expect(screen.getByText('collector.websocket')).toBeTruthy();
    expect(screen.getByText('Socket closed.')).toBeTruthy();
  });

  it('keeps the backend mutation error visible', async () => {
    getCollectorByMarketIdMock.mockResolvedValue({ session: null });
    startCollectorMock.mockRejectedValue(new ApiError('Collector start failed.', 409));
    renderPanel('market-id');
    await screen.findByText(/ещё нет collector sessions/);

    fireEvent.click(screen.getByRole('button', { name: 'Start collector' }));

    expect((await screen.findByRole('alert')).textContent).toContain('Collector start failed.');
    expect((screen.getByRole('button', { name: 'Start collector' }) as HTMLButtonElement).disabled)
      .toBe(false);
  });

  it('blocks a new market while another market mutation is pending', async () => {
    getCollectorByMarketIdMock.mockResolvedValue({ session: null });
    startCollectorMock.mockReturnValue(new Promise(() => undefined));
    const view = renderPanel('market-a');
    await screen.findByText(/ещё нет collector sessions/);
    fireEvent.click(screen.getByRole('button', { name: 'Start collector' }));
    expect(await screen.findByRole('button', { name: 'Запускаем...' })).toBeTruthy();

    view.rerender(<CollectorPanel marketId="market-b" />);

    await waitFor(() => {
      expect((screen.getByRole('button', { name: 'Start collector' }) as HTMLButtonElement).disabled)
        .toBe(true);
    });
  });
});

function renderPanel(marketId: string | null) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(<CollectorPanel marketId={marketId} />, {
    wrapper: function Wrapper({ children }: PropsWithChildren) {
      return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    },
  });
}

function createSession(status: CollectorSessionResponse['status']): CollectorSessionResponse {
  return {
    sessionId: 'session-id',
    marketId: 'market-id',
    status,
    createdAt: '2026-08-06T12:00:00Z',
    startedAt: '2026-08-06T12:00:01Z',
    stoppedAt: null,
    failureCode: null,
    failureMessage: null,
    messagesReceived: 120,
    messagesPersisted: 118,
    lastMessageAt: '2026-08-06T12:09:59Z',
    reconnectCount: 0,
  };
}
