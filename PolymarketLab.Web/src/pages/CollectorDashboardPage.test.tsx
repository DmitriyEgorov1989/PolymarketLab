// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { PropsWithChildren } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/apiError';
import {
  getCollectorByMarketId,
  startCollector,
  stopCollector,
} from '../api/collectorsApi';
import {
  getMarketById,
  getMarkets,
  registerMarket,
  type MarketResponse,
} from '../api/marketsApi';
import { marketKeys } from '../features/markets/model/marketKeys';
import { CollectorDashboardPage } from './CollectorDashboardPage';

vi.mock('../api/marketsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/marketsApi')>();

  return {
    ...actual,
    getMarketById: vi.fn(),
    getMarkets: vi.fn(),
    registerMarket: vi.fn(),
  };
});

vi.mock('../api/collectorsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/collectorsApi')>();

  return {
    ...actual,
    getCollectorByMarketId: vi.fn(),
    startCollector: vi.fn(),
    stopCollector: vi.fn(),
  };
});

const getCollectorByMarketIdMock = vi.mocked(getCollectorByMarketId);
const startCollectorMock = vi.mocked(startCollector);
const stopCollectorMock = vi.mocked(stopCollector);
const getMarketByIdMock = vi.mocked(getMarketById);
const getMarketsMock = vi.mocked(getMarkets);
const registerMarketMock = vi.mocked(registerMarket);

describe('CollectorDashboardPage market selection', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getCollectorByMarketIdMock.mockResolvedValue({ session: null });
    startCollectorMock.mockResolvedValue({
      sessionId: 'session-id',
      marketId: 'market-id',
      status: 'Running',
    });
    stopCollectorMock.mockResolvedValue({
      session: {
        sessionId: 'session-id',
        marketId: 'market-id',
        status: 'Stopped',
        createdAt: '2026-08-06T12:00:00Z',
        startedAt: '2026-08-06T12:00:01Z',
        stoppedAt: '2026-08-06T12:10:00Z',
        failureCode: null,
        failureMessage: null,
        messagesReceived: 10,
        messagesPersisted: 10,
        lastMessageAt: '2026-08-06T12:09:59Z',
        reconnectCount: 0,
      },
    });
    getMarketByIdMock.mockImplementation(async (marketId) => ({ market: createMarket(marketId) }));
  });

  it('selects the first market after initial loading', async () => {
    const first = createMarket('first');
    const second = createMarket('second');
    getMarketsMock.mockResolvedValue({ markets: [first, second] });
    renderPage();

    const firstButton = await screen.findByRole('button', { name: /Question first/ });
    await waitFor(() => expect(firstButton.getAttribute('aria-pressed')).toBe('true'));
    expect(await screen.findByRole('heading', { name: first.question })).toBeTruthy();
  });

  it('preserves manual selection after refetch changes the first market', async () => {
    const first = createMarket('first');
    const second = createMarket('second');
    const newFirst = createMarket('new-first');
    getMarketsMock.mockResolvedValueOnce({ markets: [first, second] });
    const queryClient = renderPage();
    const secondButton = await screen.findByRole('button', { name: /Question second/ });

    fireEvent.click(secondButton);
    expect(secondButton.getAttribute('aria-pressed')).toBe('true');
    getMarketsMock.mockResolvedValue({ markets: [newFirst, first, second] });
    await act(() => queryClient.invalidateQueries({ queryKey: marketKeys.list() }));

    await screen.findByRole('button', { name: /Question new-first/ });
    expect(screen.getByRole('button', { name: /Question second/ }).getAttribute('aria-pressed'))
      .toBe('true');
    expect(await screen.findByRole('heading', { name: second.question })).toBeTruthy();
  });

  it('clears selection when the selected market disappears', async () => {
    const first = createMarket('first');
    const second = createMarket('second');
    getMarketsMock.mockResolvedValueOnce({ markets: [first, second] });
    const queryClient = renderPage();
    const secondButton = await screen.findByRole('button', { name: /Question second/ });
    fireEvent.click(secondButton);

    getMarketsMock.mockResolvedValue({ markets: [first] });
    await act(() => queryClient.invalidateQueries({ queryKey: marketKeys.list() }));

    await waitFor(() => expect(screen.getByText('Выберите рынок из списка.')).toBeTruthy());
    expect(screen.getByRole('button', { name: /Question first/ }).getAttribute('aria-pressed'))
      .toBe('false');
    expect((screen.getByRole('button', { name: 'Start collector' }) as HTMLButtonElement).disabled)
      .toBe(true);
  });

  it('hides the selected market when its live refresh fails', async () => {
    const market = createMarket('market');
    getMarketsMock.mockResolvedValueOnce({ markets: [market] });
    const queryClient = renderPage();
    await screen.findByRole('heading', { name: market.question });

    getMarketsMock.mockRejectedValue(new ApiError('Gamma API is unavailable.', 500));
    await act(() => queryClient.invalidateQueries({ queryKey: marketKeys.list() }));

    expect((await screen.findByRole('alert')).textContent).toContain('Gamma API is unavailable.');
    await waitFor(() => expect(screen.getByText('Выберите рынок из списка.')).toBeTruthy());
    expect(screen.queryByRole('button', { name: /Question market/ })).toBeNull();
    expect((screen.getByRole('button', { name: 'Start collector' }) as HTMLButtonElement).disabled)
      .toBe(true);
  });

  it('does not auto-select after an initially empty result', async () => {
    const market = createMarket('later');
    getMarketsMock.mockResolvedValueOnce({ markets: [] });
    const queryClient = renderPage();
    await screen.findByText(/нет зарегистрированных рынков с активными торгами/);

    getMarketsMock.mockResolvedValue({ markets: [market] });
    await act(() => queryClient.invalidateQueries({ queryKey: marketKeys.list() }));

    const button = await screen.findByRole('button', { name: /Question later/ });
    expect(button.getAttribute('aria-pressed')).toBe('false');
    expect(screen.getByText('Выберите рынок из списка.')).toBeTruthy();
  });

  it('adds a registered market to the live list without bypassing selection', async () => {
    const first = createMarket('first');
    const registered = createMarket('registered');
    getMarketsMock
      .mockResolvedValueOnce({ markets: [first] })
      .mockResolvedValue({ markets: [first, registered] });
    registerMarketMock.mockResolvedValue({ marketId: registered.marketId, created: true });
    renderPage();
    await screen.findByRole('button', { name: /Question first/ });
    const input = screen.getByLabelText('Polymarket URL') as HTMLInputElement;

    fireEvent.change(input, { target: { value: 'https://polymarket.com/event/registered' } });
    fireEvent.submit(input.closest('form')!);

    const registeredButton = await screen.findByRole('button', { name: /Question registered/ });
    expect(registeredButton.getAttribute('aria-pressed')).toBe('false');
    expect(screen.getByRole('button', { name: /Question first/ }).getAttribute('aria-pressed'))
      .toBe('true');
  });

  it('loads outcomes and token ids for the selected market', async () => {
    const market = createMarket('tokens');
    const detail = {
      ...market,
      tokens: [
        { outcome: 'Yes', outcomeIndex: 0, tokenId: '123456789012345678901234567890' },
        { outcome: 'No', outcomeIndex: 1, tokenId: 'token-no' },
      ],
    };
    getMarketsMock.mockResolvedValue({ markets: [market] });
    getMarketByIdMock.mockResolvedValue({ market: detail });

    renderPage();

    expect(await screen.findByRole('heading', { name: detail.question })).toBeTruthy();
    expect(getMarketByIdMock).toHaveBeenCalledWith(detail.marketId, expect.any(AbortSignal));
    expect(screen.getByText('Yes')).toBeTruthy();
    expect(screen.getByText('Outcome index: 0')).toBeTruthy();
    expect(screen.getByText('123456789012345678901234567890')).toBeTruthy();
    expect(screen.getByText('No')).toBeTruthy();
    expect(screen.getByText('token-no')).toBeTruthy();
  });
});

function renderPage(): QueryClient {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  render(<CollectorDashboardPage />, {
    wrapper: function Wrapper({ children }: PropsWithChildren) {
      return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    },
  });

  return queryClient;
}

function createMarket(marketId: string): MarketResponse {
  return {
    marketId,
    externalEventId: `external-event-${marketId}`,
    eventSlug: `${marketId}-event-slug`,
    externalMarketId: `external-${marketId}`,
    marketSlug: `${marketId}-market-slug`,
    conditionId: `condition-${marketId}`,
    question: `Question ${marketId}?`,
    discoveredAt: '2026-08-01T09:00:00Z',
    externalCreatedAt: null,
    ordersOpenedAt: null,
    gammaStartDate: null,
    eventStartsAt: '2026-08-01T10:00:00Z',
    eventEndsAt: '2026-08-02T10:00:00Z',
    externalClosedAt: null,
    scheduleRefreshedAt: '2026-08-01T09:30:00Z',
    tokens: [],
  };
}
