// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { PropsWithChildren } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
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
    getMarkets: vi.fn(),
    registerMarket: vi.fn(),
  };
});

const getMarketsMock = vi.mocked(getMarkets);
const registerMarketMock = vi.mocked(registerMarket);

describe('CollectorDashboardPage market selection', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('selects the first market after initial loading', async () => {
    const first = createMarket('first');
    const second = createMarket('second');
    getMarketsMock.mockResolvedValue({ markets: [first, second] });
    renderPage();

    const firstButton = await screen.findByRole('button', { name: /Question first/ });
    await waitFor(() => expect(firstButton.getAttribute('aria-pressed')).toBe('true'));
    expect(screen.getByText(`Выбран рынок: ${first.marketId}`)).toBeTruthy();
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
    expect(screen.getByText(`Выбран рынок: ${second.marketId}`)).toBeTruthy();
  });

  it('does not auto-select after an initially empty result', async () => {
    const market = createMarket('later');
    getMarketsMock.mockResolvedValueOnce({ markets: [] });
    const queryClient = renderPage();
    await screen.findByText(/Зарегистрированных рынков пока нет/);

    getMarketsMock.mockResolvedValue({ markets: [market] });
    await act(() => queryClient.invalidateQueries({ queryKey: marketKeys.list() }));

    const button = await screen.findByRole('button', { name: /Question later/ });
    expect(button.getAttribute('aria-pressed')).toBe('false');
    expect(screen.getByText('Выберите рынок из списка.')).toBeTruthy();
  });

  it('selects the market returned by registration', async () => {
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

    await waitFor(() => {
      expect(screen.getByText(`Выбран рынок: ${registered.marketId}`)).toBeTruthy();
    });
    const registeredButton = await screen.findByRole('button', { name: /Question registered/ });
    expect(registeredButton.getAttribute('aria-pressed')).toBe('true');
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
    externalMarketId: `external-${marketId}`,
    slug: `${marketId}-slug`,
    conditionId: `condition-${marketId}`,
    question: `Question ${marketId}?`,
    startsAt: null,
    endsAt: null,
    tokens: [],
  };
}
