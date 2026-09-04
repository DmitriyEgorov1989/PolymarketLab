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
} from '../../../api/collectorsApi';
import { ApiError } from '../../../api/apiError';
import { ACTIVE_COLLECTOR_POLL_INTERVAL_MS } from '../model/collectorStatus';
import { createCollectorSession } from '../testing/createCollectorSession';
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
    vi.restoreAllMocks();
  });

  it('starts a collector for a market without sessions', async () => {
    getCollectorByMarketIdMock.mockResolvedValue({ session: null });
    startCollectorMock.mockResolvedValue({
      sessionId: 'session-id',
      marketId: 'market-id',
      status: 'Running',
    });
    getCollectorByIdMock.mockResolvedValue({
      session: createCollectorSession({ status: 'Running' }),
    });
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

  it('shows the destructive Stop transition from Invalidating to Failed', async () => {
    vi.useFakeTimers();
    const session = createCollectorSession({ status: 'Running' });
    const invalidating = { ...session, status: 'Invalidating' as const, phase: 'Cleaning' };
    const failed = {
      ...invalidating,
      status: 'Failed' as const,
      phase: null,
      stoppedAt: '2026-08-06T12:10:02Z',
    };
    getCollectorByMarketIdMock.mockResolvedValue({ session });
    getCollectorByIdMock
      .mockResolvedValueOnce({ session })
      .mockResolvedValue({ session: failed });
    stopCollectorMock.mockResolvedValue({ session: invalidating });
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderPanel(session.marketId);

    await vi.waitFor(() => expect(screen.getByText('Running')).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: 'Stop collector' }));

    await vi.waitFor(() => {
      expect(stopCollectorMock).toHaveBeenCalledWith(session.sessionId);
    });
    await vi.waitFor(() => expect(screen.getByText('Invalidating')).toBeTruthy());
    expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining('аннулирует dataset'));

    await act(() => vi.advanceTimersByTimeAsync(ACTIVE_COLLECTOR_POLL_INTERVAL_MS));
    await vi.waitFor(() => expect(screen.getByText('Failed')).toBeTruthy());
  });

  it('does not stop when the user cancels confirmation', async () => {
    const session = createCollectorSession({ status: 'Scheduled' });
    getCollectorByMarketIdMock.mockResolvedValue({ session });
    getCollectorByIdMock.mockResolvedValue({ session });
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    renderPanel(session.marketId);

    await screen.findByText('Scheduled');
    fireEvent.click(screen.getByRole('button', { name: 'Stop collector' }));

    expect(window.confirm).toHaveBeenCalledOnce();
    expect(stopCollectorMock).not.toHaveBeenCalled();
  });

  it('updates counters while polling an active session', async () => {
    vi.useFakeTimers();
    const running = createCollectorSession({ status: 'Running' });
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
    expect(screen.getAllByText('120').length).toBeGreaterThan(0);
    expect(screen.getAllByText('118').length).toBeGreaterThan(0);

    await act(() => vi.advanceTimersByTimeAsync(ACTIVE_COLLECTOR_POLL_INTERVAL_MS));

    await vi.waitFor(() => expect(getCollectorByIdMock).toHaveBeenCalledTimes(2));
    expect(screen.getAllByText('125').length).toBeGreaterThan(0);
    expect(screen.getAllByText('124').length).toBeGreaterThan(0);
  });

  it('polls Stopping until terminal status and then stops polling', async () => {
    vi.useFakeTimers();
    const stopping = createCollectorSession({ status: 'Stopping' });
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
      ...createCollectorSession({ status: 'Running' }),
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
        ...createCollectorSession({ status: 'Failed' }),
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
    startCollectorMock.mockRejectedValue(new ApiError('Collector start failed.', 409, {
      errors: [{
        errorCode: 'collector.start.global_session_conflict',
        errorMessage: 'Collector start failed.',
        invalidField: null,
      }],
    }));
    renderPanel('market-id');
    await screen.findByText(/ещё нет collector sessions/);

    fireEvent.click(screen.getByRole('button', { name: 'Start collector' }));

    expect((await screen.findByRole('alert')).textContent).toContain('Collector start failed.');
    expect(screen.getByRole('alert').textContent).toContain('HTTP 409');
    expect(screen.getByRole('alert').textContent)
      .toContain('collector.start.global_session_conflict');
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

    view.rerender(<CollectorPanel marketId="market-b" registeredMarketIds={['market-a', 'market-b']} />);

    await waitFor(() => {
      expect((screen.getByRole('button', { name: 'Start collector' }) as HTMLButtonElement).disabled)
        .toBe(true);
    });
  });
  it('shows an early Start transition from scheduled preparation into connecting', async () => {
    vi.useFakeTimers();
    const scheduled = createCollectorSession({
      status: 'Scheduled', phase: 'WaitingForPreparation',
    });
    const starting = createCollectorSession({ status: 'Starting', phase: 'Connecting' });
    getCollectorByMarketIdMock.mockResolvedValue({ session: null });
    startCollectorMock.mockResolvedValue({
      sessionId: scheduled.sessionId,
      marketId: scheduled.marketId,
      status: 'Scheduled',
    });
    getCollectorByIdMock
      .mockResolvedValueOnce({ session: scheduled })
      .mockResolvedValue({ session: starting });
    renderPanel(scheduled.marketId);

    await vi.waitFor(() => expect(screen.getByText(/ещё нет collector sessions/)).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: 'Start collector' }));
    await vi.waitFor(() => expect(screen.getByText('WaitingForPreparation')).toBeTruthy());
    await act(() => vi.advanceTimersByTimeAsync(ACTIVE_COLLECTOR_POLL_INTERVAL_MS));

    await vi.waitFor(() => expect(screen.getByText('Starting')).toBeTruthy());
    expect(screen.getByText('Connecting')).toBeTruthy();
  });

  it('polls Invalidating until Failed', async () => {
    vi.useFakeTimers();
    const invalidating = createCollectorSession({ status: 'Invalidating', phase: 'Cleaning' });
    const failed = createCollectorSession({ status: 'Failed', phase: null });
    getCollectorByMarketIdMock.mockResolvedValue({ session: invalidating });
    getCollectorByIdMock
      .mockResolvedValueOnce({ session: invalidating })
      .mockResolvedValue({ session: failed });
    renderPanel(invalidating.marketId);

    await vi.waitFor(() => expect(screen.getByText('Invalidating')).toBeTruthy());
    await act(() => vi.advanceTimersByTimeAsync(ACTIVE_COLLECTOR_POLL_INTERVAL_MS));

    await vi.waitFor(() => expect(screen.getByText('Failed')).toBeTruthy());
  });

  it('blocks Start when another market owns the known global slot', async () => {
    const other = createCollectorSession({ marketId: 'market-b', status: 'Running' });
    getCollectorByMarketIdMock.mockImplementation(async (marketId) => ({
      session: marketId === 'market-b' ? other : null,
    }));
    renderPanel('market-a', ['market-a', 'market-b']);

    await screen.findByText(/занят рынком market-b/);
    const button = screen.getByRole('button', { name: 'Start collector' }) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    fireEvent.click(button);
    expect(startCollectorMock).not.toHaveBeenCalled();
  });

  it('shows every failed global slot read', async () => {
    getCollectorByMarketIdMock.mockImplementation(async (marketId) => {
      throw new ApiError(`Read ${marketId} failed.`, 503, {
        errors: [{
          errorCode: `collector.read.${marketId}`,
          errorMessage: `Read ${marketId} failed.`,
          invalidField: null,
        }],
      });
    });
    renderPanel('market-a', ['market-a', 'market-b']);

    expect(await screen.findByText('collector.read.market-a')).toBeTruthy();
    expect(screen.getByText('collector.read.market-b')).toBeTruthy();
    expect(screen.getAllByText('HTTP 503')).toHaveLength(2);
  });

  it('allows Start only after a failed global slot read succeeds on retry', async () => {
    getCollectorByMarketIdMock.mockImplementation(async (marketId) => {
      if (marketId === 'market-b') {
        throw new ApiError('Discovery failed.', 500, {
          errors: [{
            errorCode: 'collector.read.failed',
            errorMessage: 'Discovery failed.',
            invalidField: null,
          }],
        });
      }

      return { session: null };
    });
    startCollectorMock.mockResolvedValue({
      sessionId: 'session-id', marketId: 'market-a', status: 'Scheduled',
    });
    renderPanel('market-a', ['market-a', 'market-b']);

    const retry = await screen.findByRole('button', { name: 'Повторить проверку slot' });
    expect(screen.getByText('HTTP 500')).toBeTruthy();
    expect(screen.getByText('collector.read.failed')).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Start collector' }) as HTMLButtonElement).disabled)
      .toBe(true);
    getCollectorByMarketIdMock.mockResolvedValue({ session: null });
    fireEvent.click(retry);

    await waitFor(() => {
      expect((screen.getByRole('button', { name: 'Start collector' }) as HTMLButtonElement).disabled)
        .toBe(false);
    });
    fireEvent.click(screen.getByRole('button', { name: 'Start collector' }));
    await waitFor(() => expect(startCollectorMock).toHaveBeenCalledOnce());
  });
});

function renderPanel(
  marketId: string | null,
  registeredMarketIds = marketId === null ? [] : [marketId],
) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(<CollectorPanel marketId={marketId} registeredMarketIds={registeredMarketIds} />, {
    wrapper: function Wrapper({ children }: PropsWithChildren) {
      return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    },
  });
}
