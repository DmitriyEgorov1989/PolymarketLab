// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { PropsWithChildren } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/apiError';
import {
  getCollectorById,
  getCollectorByMarketId,
  startCollector,
  stopCollector,
  type CollectorSessionResponse,
} from '../../../api/collectorsApi';
import { collectorKeys } from '../model/collectorKeys';
import { ACTIVE_COLLECTOR_POLL_INTERVAL_MS } from '../model/collectorStatus';
import { createCollectorSession } from '../testing/createCollectorSession';
import { useCollectorByIdQuery } from './useCollectorByIdQuery';
import { useCollectorByMarketQuery } from './useCollectorByMarketQuery';
import { useCollectorSlotsQuery } from './useCollectorSlotsQuery';
import { useStartCollector } from './useStartCollector';
import { useStopCollector } from './useStopCollector';

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

describe('collectorKeys', () => {
  it('separates session and market lookups', () => {
    expect(collectorKeys.detail('id')).toEqual(['collectors', 'detail', 'id']);
    expect(collectorKeys.byMarket('id')).toEqual(['collectors', 'by-market', 'id']);
  });
});

describe('collector hooks', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('loads and unwraps a collector by market', async () => {
    const session = createCollectorSession();
    getCollectorByMarketIdMock.mockImplementation((marketId, signal) => {
      expect(marketId).toBe(session.marketId);
      expect(signal).toBeInstanceOf(AbortSignal);
      return Promise.resolve({ session });
    });
    const queryClient = createQueryClient();

    const { result } = renderHook(
      () => useCollectorByMarketQuery(session.marketId),
      { wrapper: createWrapper(queryClient) },
    );

    await waitFor(() => expect(result.current.data).toEqual(session));
  });

  it('returns null when a market has no sessions', async () => {
    getCollectorByMarketIdMock.mockResolvedValue({ session: null });
    const queryClient = createQueryClient();

    const { result } = renderHook(
      () => useCollectorByMarketQuery('market-id'),
      { wrapper: createWrapper(queryClient) },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it('does not report the exclusive slot as free until every market read succeeds', async () => {
    const exclusive = createCollectorSession({ marketId: 'market-b', status: 'Scheduled' });
    getCollectorByMarketIdMock.mockImplementation(async (marketId) => {
      if (marketId === 'market-a') {
        throw new ApiError('Temporary discovery failure.', 500);
      }

      return { session: exclusive };
    });
    const queryClient = createQueryClient();
    const { result } = renderHook(
      () => useCollectorSlotsQuery(['market-a', 'market-b']),
      { wrapper: createWrapper(queryClient) },
    );

    await waitFor(() => expect(result.current.errors).toHaveLength(1));
    expect(result.current.isResolved).toBe(false);
    expect(result.current.exclusiveSession).toEqual(exclusive);

    getCollectorByMarketIdMock.mockImplementation(async (marketId) => ({
      session: marketId === 'market-b' ? exclusive : null,
    }));
    await act(() => result.current.retry());

    await waitFor(() => expect(result.current.isResolved).toBe(true));
    expect(result.current.errors).toHaveLength(0);
  });

  it('reports a known free slot without confusing it with an unresolved session', async () => {
    getCollectorByMarketIdMock.mockResolvedValue({ session: null });
    const queryClient = createQueryClient();
    const { result } = renderHook(
      () => useCollectorSlotsQuery(['market-a', 'market-b']),
      { wrapper: createWrapper(queryClient) },
    );

    await waitFor(() => expect(result.current.isResolved).toBe(true));
    expect(result.current.exclusiveSession).toBeNull();
  });

  it('polls an active global slot every two seconds', async () => {
    vi.useFakeTimers();
    const scheduled = createCollectorSession({ status: 'Scheduled' });
    const starting = createCollectorSession({ status: 'Starting', phase: 'Connecting' });
    getCollectorByMarketIdMock
      .mockResolvedValueOnce({ session: scheduled })
      .mockResolvedValue({ session: starting });
    const queryClient = createQueryClient();
    const { result } = renderHook(
      () => useCollectorSlotsQuery([scheduled.marketId]),
      { wrapper: createWrapper(queryClient) },
    );

    await vi.waitFor(() => expect(result.current.exclusiveSession?.status).toBe('Scheduled'));
    await act(() => vi.advanceTimersByTimeAsync(ACTIVE_COLLECTOR_POLL_INTERVAL_MS));
    await vi.waitFor(() => expect(result.current.exclusiveSession?.status).toBe('Starting'));
    expect(getCollectorByMarketIdMock).toHaveBeenCalledTimes(2);
  });

  it.each(['Stopped', 'Failed', 'Interrupted', 'FutureStatus'] as const)(
    'stops polling after status %s',
    async (terminalStatus) => {
    vi.useFakeTimers();
    const running = createCollectorSession();
    const terminal = {
      ...running,
      status: terminalStatus as CollectorSessionResponse['status'],
    };
    getCollectorByIdMock
      .mockResolvedValueOnce({ session: running })
      .mockResolvedValue({ session: terminal });
    const queryClient = createQueryClient();

    const { result } = renderHook(
      () => useCollectorByIdQuery(running.sessionId),
      { wrapper: createWrapper(queryClient) },
    );

    await vi.waitFor(() => expect(result.current.data?.status).toBe('Running'));
    await act(() => vi.advanceTimersByTimeAsync(ACTIVE_COLLECTOR_POLL_INTERVAL_MS));
    await vi.waitFor(() => expect(result.current.data?.status).toBe(terminalStatus));
    expect(queryClient.getQueryData(collectorKeys.byMarket(running.marketId)))
      .toEqual({ session: terminal });
    const callsAfterStop = getCollectorByIdMock.mock.calls.length;

    await act(() => vi.advanceTimersByTimeAsync(ACTIVE_COLLECTOR_POLL_INTERVAL_MS * 2));
    expect(getCollectorByIdMock).toHaveBeenCalledTimes(callsAfterStop);
    },
  );

  it('continues detail polling when the first backend read fails', async () => {
    vi.useFakeTimers();
    const session = createCollectorSession();
    getCollectorByIdMock
      .mockRejectedValueOnce(new Error('Temporary read failure.'))
      .mockResolvedValue({ session });
    const queryClient = createQueryClient();

    const { result } = renderHook(
      () => useCollectorByIdQuery(session.sessionId),
      { wrapper: createWrapper(queryClient) },
    );

    await vi.waitFor(() => expect(result.current.isError).toBe(true));
    await act(() => vi.advanceTimersByTimeAsync(ACTIVE_COLLECTOR_POLL_INTERVAL_MS));
    await vi.waitFor(() => expect(result.current.data).toEqual(session));
    expect(getCollectorByIdMock).toHaveBeenCalledTimes(2);
  });

  it('does not replace a different active by-market session with a late detail response', async () => {
    const oldSession = {
      ...createCollectorSession(),
      sessionId: 'old-session-id',
      status: 'Stopped' as const,
    };
    const activeSession = {
      ...createCollectorSession(),
      sessionId: 'active-session-id',
      createdAt: '2026-08-06T13:00:00Z',
    };
    getCollectorByIdMock.mockResolvedValue({ session: oldSession });
    const queryClient = createQueryClient();
    queryClient.setQueryData(
      collectorKeys.byMarket(activeSession.marketId),
      { session: activeSession },
    );

    const { result } = renderHook(
      () => useCollectorByIdQuery(oldSession.sessionId),
      { wrapper: createWrapper(queryClient) },
    );

    await waitFor(() => expect(result.current.data).toEqual(oldSession));
    expect(queryClient.getQueryData(collectorKeys.byMarket(activeSession.marketId)))
      .toEqual({ session: activeSession });
  });

  it('does not fetch collector data without an id', () => {
    const queryClient = createQueryClient();

    const byMarket = renderHook(() => useCollectorByMarketQuery(null), {
      wrapper: createWrapper(queryClient),
    });
    const byId = renderHook(() => useCollectorByIdQuery(null), {
      wrapper: createWrapper(queryClient),
    });

    expect(byMarket.result.current.fetchStatus).toBe('idle');
    expect(byId.result.current.fetchStatus).toBe('idle');
    expect(getCollectorByMarketIdMock).not.toHaveBeenCalled();
    expect(getCollectorByIdMock).not.toHaveBeenCalled();
  });

  it('loads a collector by session id', async () => {
    const session = createCollectorSession();
    getCollectorByIdMock.mockResolvedValue({ session });
    const queryClient = createQueryClient();

    const { result } = renderHook(() => useCollectorByIdQuery(session.sessionId), {
      wrapper: createWrapper(queryClient),
    });

    await waitFor(() => expect(result.current.data).toEqual(session));
    expect(getCollectorByIdMock).toHaveBeenCalledWith(
      session.sessionId,
      expect.any(AbortSignal),
    );
  });

  it('invalidates collector queries after start', async () => {
    startCollectorMock.mockResolvedValue({
      sessionId: 'session-id',
      marketId: 'market-id',
      status: 'Running',
    });
    const queryClient = createQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useStartCollector(), {
      wrapper: createWrapper(queryClient),
    });
    act(() => result.current.mutate({ marketId: 'market-id' }));

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: collectorKeys.byMarket('market-id'),
    });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: collectorKeys.detail('session-id'),
    });
  });

  it('writes the backend stop response to detail and by-market caches immediately', async () => {
    const session = createCollectorSession();
    const invalidating = {
      ...session,
      status: 'Invalidating' as const,
      phase: 'Cleaning',
    };
    stopCollectorMock.mockResolvedValue({ session: invalidating });
    const queryClient = createQueryClient();

    const { result } = renderHook(() => useStopCollector(), {
      wrapper: createWrapper(queryClient),
    });
    act(() => result.current.mutate(session.sessionId));

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(queryClient.getQueryData(collectorKeys.detail(session.sessionId)))
      .toEqual({ session: invalidating });
    expect(queryClient.getQueryData(collectorKeys.byMarket(session.marketId)))
      .toEqual({ session: invalidating });
  });
});

function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
}

function createWrapper(queryClient: QueryClient) {
  return function Wrapper({ children }: PropsWithChildren) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  };
}
