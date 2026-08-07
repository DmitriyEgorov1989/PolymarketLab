// @vitest-environment jsdom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/apiError';
import { formatLocalDate } from '../../../shared/formatters/formatLocalDate';
import type { Market } from '../model/market';
import { MarketDetails } from './MarketDetails';

describe('MarketDetails', () => {
  it('asks the user to select a market', () => {
    renderDetails({ marketId: null });

    expect(screen.getByText('Выберите рынок из списка.')).toBeTruthy();
  });

  it('renders loading state', () => {
    renderDetails({ isPending: true });

    expect(screen.getByRole('status').textContent).toContain('Загружаем детали рынка');
  });

  it('renders a backend error and retries', () => {
    const onRetry = vi.fn();
    renderDetails({
      error: new ApiError('Market details failed.', 500),
      onRetry,
    });

    expect(screen.getByRole('alert').textContent).toContain('Market details failed.');
    fireEvent.click(screen.getByRole('button', { name: 'Повторить' }));
    expect(onRetry).toHaveBeenCalledOnce();
  });

  it('renders market fields, local dates, null, and tokens', () => {
    const market = createMarket();
    renderDetails({ market });

    expect(screen.getByRole('heading', { name: market.question })).toBeTruthy();
    expect(screen.getByText(market.slug)).toBeTruthy();
    expect(screen.getByText(market.externalMarketId)).toBeTruthy();
    expect(screen.getByText(market.conditionId)).toBeTruthy();
    expect(screen.getByText(formatLocalDate(market.startsAt))).toBeTruthy();
    expect(screen.getByText('-')).toBeTruthy();
    expect(screen.getByText('Yes')).toBeTruthy();
    expect(screen.getByText('Outcome index: 0')).toBeTruthy();
    expect(screen.getByText('token-yes')).toBeTruthy();
  });
});

interface RenderDetailsOptions {
  marketId?: string | null;
  market?: Market;
  isPending?: boolean;
  isFetching?: boolean;
  error?: ApiError | null;
  onRetry?: () => void;
}

function renderDetails(options: RenderDetailsOptions = {}) {
  return render(
    <MarketDetails
      marketId={options.marketId === undefined ? 'market-id' : options.marketId}
      market={options.market}
      isPending={options.isPending ?? false}
      isFetching={options.isFetching ?? false}
      error={options.error ?? null}
      onRetry={options.onRetry ?? vi.fn()}
    />,
  );
}

function createMarket(): Market {
  return {
    marketId: 'market-id',
    externalMarketId: 'external-market-id',
    slug: 'example-market',
    conditionId: 'condition-id',
    question: 'Will it happen?',
    startsAt: '2026-08-01T10:00:00Z',
    endsAt: null,
    tokens: [
      { outcome: 'Yes', outcomeIndex: 0, tokenId: 'token-yes' },
    ],
  };
}
