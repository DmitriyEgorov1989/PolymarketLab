// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { PropsWithChildren } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  getCollectorById,
  getCollectorByMarketId,
  startCollector,
  stopCollector,
  type CollectorSessionResponse,
} from '../../../api/collectorsApi';
import { collectorKeys } from '../model/collectorKeys';
import { useCollectorByIdQuery } from './useCollectorByIdQuery';
import { useCollectorByMarketQuery } from './useCollectorByMarketQuery';
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

  it('loads and unwraps a collector by market', async () => {
    const session = createSession();
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
    const session = createSession();
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

  it('invalidates collector queries after stop using the backend session', async () => {
    const session = createSession();
    stopCollectorMock.mockResolvedValue({ session: { ...session, status: 'Stopped' } });
    const queryClient = createQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useStopCollector(), {
      wrapper: createWrapper(queryClient),
    });
    act(() => result.current.mutate(session.sessionId));

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: collectorKeys.detail(session.sessionId),
    });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: collectorKeys.byMarket(session.marketId),
    });
  });
});

function createSession(): CollectorSessionResponse {
  return {
    sessionId: 'session-id',
    marketId: 'market-id',
    status: 'Running',
    createdAt: '2026-08-06T12:00:00Z',
    startedAt: '2026-08-06T12:00:01Z',
    stoppedAt: null,
    failureCode: null,
    failureMessage: null,
  };
}

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
