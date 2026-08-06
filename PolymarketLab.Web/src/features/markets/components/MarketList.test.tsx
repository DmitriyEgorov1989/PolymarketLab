// @vitest-environment jsdom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/apiError';
import type { Market } from '../model/market';
import { MarketList } from './MarketList';

describe('MarketList', () => {
  it('renders loading state', () => {
    renderList({ isPending: true });

    expect(screen.getByRole('status').textContent).toContain('Загружаем рынки');
  });

  it('renders empty state', () => {
    renderList({ markets: [] });

    expect(screen.getByText(/Зарегистрированных рынков пока нет/)).toBeTruthy();
  });

  it('renders backend error and retries', () => {
    const onRetry = vi.fn();
    renderList({
      error: new ApiError('Backend is unavailable.', 500),
      onRetry,
    });

    expect(screen.getByRole('alert').textContent).toContain('Backend is unavailable.');
    fireEvent.click(screen.getByRole('button', { name: 'Повторить' }));
    expect(onRetry).toHaveBeenCalledOnce();
  });

  it('renders markets and reports selection', () => {
    const first = createMarket('first', 'First question?');
    const second = createMarket('second', 'Second question?');
    const onSelectMarket = vi.fn();
    renderList({
      markets: [first, second],
      selectedMarketId: first.marketId,
      onSelectMarket,
    });

    const firstButton = screen.getByRole('button', { name: /First question/ });
    const secondButton = screen.getByRole('button', { name: /Second question/ });
    expect(firstButton.getAttribute('aria-pressed')).toBe('true');
    expect(firstButton.textContent).toContain('Выбран');
    expect(secondButton.getAttribute('aria-pressed')).toBe('false');

    fireEvent.click(secondButton);
    expect(onSelectMarket).toHaveBeenCalledWith(second.marketId);
  });

  it('keeps stale markets visible during a background refetch error', () => {
    const market = createMarket('first', 'First question?');
    renderList({
      markets: [market],
      isFetching: true,
      error: new ApiError('Refresh failed.', 500),
    });

    expect(screen.getByRole('button', { name: /First question/ })).toBeTruthy();
    expect(screen.getByRole('alert').textContent).toContain('Refresh failed.');
  });
});

interface RenderListOptions {
  markets?: Market[];
  isPending?: boolean;
  isFetching?: boolean;
  error?: ApiError | null;
  selectedMarketId?: string | null;
  onSelectMarket?: (marketId: string) => void;
  onRetry?: () => void;
}

function renderList(options: RenderListOptions = {}) {
  return render(
    <MarketList
      markets={options.markets}
      isPending={options.isPending ?? false}
      isFetching={options.isFetching ?? false}
      error={options.error ?? null}
      selectedMarketId={options.selectedMarketId ?? null}
      onSelectMarket={options.onSelectMarket ?? vi.fn()}
      onRetry={options.onRetry ?? vi.fn()}
    />,
  );
}

function createMarket(marketId: string, question: string): Market {
  return {
    marketId,
    externalMarketId: `external-${marketId}`,
    slug: `${marketId}-slug`,
    conditionId: `condition-${marketId}`,
    question,
    startsAt: null,
    endsAt: null,
    tokens: [],
  };
}
