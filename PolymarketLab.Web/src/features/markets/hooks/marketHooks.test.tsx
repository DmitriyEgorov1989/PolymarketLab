// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { PropsWithChildren } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/apiError';
import {
  getMarketById,
  getMarkets,
  registerMarket,
  type MarketResponse,
} from '../../../api/marketsApi';
import { marketKeys } from '../model/marketKeys';
import { useMarketQuery } from './useMarketQuery';
import { useMarketsQuery } from './useMarketsQuery';
import { useRegisterMarketMutation } from './useRegisterMarketMutation';

vi.mock('../../../api/marketsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/marketsApi')>();

  return {
    ...actual,
    getMarketById: vi.fn(),
    getMarkets: vi.fn(),
    registerMarket: vi.fn(),
  };
});

const getMarketByIdMock = vi.mocked(getMarketById);
const getMarketsMock = vi.mocked(getMarkets);
const registerMarketMock = vi.mocked(registerMarket);

describe('marketKeys', () => {
  it('creates stable list and detail keys', () => {
    expect(marketKeys.all).toEqual(['markets']);
    expect(marketKeys.list()).toEqual(['markets', 'list']);
    expect(marketKeys.detail('market-id')).toEqual([
      'markets',
      'detail',
      'market-id',
    ]);
  });
});

describe('market hooks', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('loads and unwraps the market list', async () => {
    const market = createMarket();
    getMarketsMock.mockImplementation((signal) => {
      expect(signal).toBeInstanceOf(AbortSignal);
      return Promise.resolve({ markets: [market] });
    });
    const queryClient = createQueryClient();

    const { result } = renderHook(() => useMarketsQuery(), {
      wrapper: createWrapper(queryClient),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([market]);
    expect(queryClient.getQueryData(marketKeys.list())).toEqual({ markets: [market] });
  });

  it('keeps a backend error in query state', async () => {
    const apiError = new ApiError('Market request failed.', 500);
    getMarketsMock.mockRejectedValue(apiError);
    const queryClient = createQueryClient();

    const { result } = renderHook(() => useMarketsQuery(), {
      wrapper: createWrapper(queryClient),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toBe(apiError);
  });

  it('does not fetch market details without an id', () => {
    const queryClient = createQueryClient();

    const { result } = renderHook(() => useMarketQuery(null), {
      wrapper: createWrapper(queryClient),
    });

    expect(result.current.fetchStatus).toBe('idle');
    expect(getMarketByIdMock).not.toHaveBeenCalled();
  });

  it('loads market details by id', async () => {
    const market = createMarket();
    getMarketByIdMock.mockResolvedValue({ market });
    const queryClient = createQueryClient();

    const { result } = renderHook(() => useMarketQuery(market.marketId), {
      wrapper: createWrapper(queryClient),
    });

    await waitFor(() => expect(result.current.data).toEqual(market));
    expect(getMarketByIdMock).toHaveBeenCalledWith(
      market.marketId,
      expect.any(AbortSignal),
    );
  });

  it('invalidates the market list after registration', async () => {
    registerMarketMock.mockResolvedValue({ marketId: 'market-id', created: true });
    const queryClient = createQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useRegisterMarketMutation(), {
      wrapper: createWrapper(queryClient),
    });
    act(() => result.current.mutate({ marketUri: 'https://polymarket.com/event/example' }));

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: marketKeys.list() });
    expect(queryClient.getQueryData(marketKeys.detail('market-id'))).toBeUndefined();
  });

  it('does not keep registration pending while the list invalidates', async () => {
    registerMarketMock.mockResolvedValue({ marketId: 'market-id', created: true });
    const queryClient = createQueryClient();
    const pendingInvalidation = new Promise<void>(() => undefined);
    vi.spyOn(queryClient, 'invalidateQueries').mockReturnValue(pendingInvalidation);

    const { result } = renderHook(() => useRegisterMarketMutation(), {
      wrapper: createWrapper(queryClient),
    });
    act(() => result.current.mutate({ marketUri: 'https://polymarket.com/event/example' }));

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });
});

function createMarket(): MarketResponse {
  return {
    marketId: 'market-id',
    externalMarketId: 'external-id',
    slug: 'example-market',
    conditionId: 'condition-id',
    question: 'Will it happen?',
    startsAt: null,
    endsAt: null,
    tokens: [],
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
