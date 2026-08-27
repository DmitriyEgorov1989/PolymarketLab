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
    expect(screen.getByText('Event identity')).toBeTruthy();
    expect(screen.getByText('Market identity')).toBeTruthy();
    expect(screen.getByText('Schedule')).toBeTruthy();
    expect(screen.getByText(market.eventSlug)).toBeTruthy();
    expect(screen.getByText(market.externalEventId)).toBeTruthy();
    expect(screen.getByText(market.marketSlug)).toBeTruthy();
    expect(screen.getByText(market.marketId)).toBeTruthy();
    expect(screen.getByText(market.externalMarketId)).toBeTruthy();
    expect(screen.getByText(market.conditionId)).toBeTruthy();
    expect(screen.getByText(formatLocalDate(market.discoveredAt))).toBeTruthy();
    expect(screen.getByText(formatLocalDate(market.ordersOpenedAt))).toBeTruthy();
    expect(screen.getByText(formatLocalDate(market.gammaStartDate))).toBeTruthy();
    expect(screen.getByText(formatLocalDate(market.eventStartsAt))).toBeTruthy();
    expect(screen.getByText(formatLocalDate(market.eventEndsAt))).toBeTruthy();
    expect(screen.getByText(formatLocalDate(market.scheduleRefreshedAt))).toBeTruthy();
    expect(screen.getAllByText('-')).toHaveLength(2);
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
    externalEventId: 'external-event-id',
    eventSlug: 'example-event',
    externalMarketId: 'external-market-id',
    marketSlug: 'example-market',
    conditionId: 'condition-id',
    question: 'Will it happen?',
    discoveredAt: '2026-08-01T09:00:00Z',
    externalCreatedAt: null,
    ordersOpenedAt: '2026-08-01T09:30:00Z',
    gammaStartDate: '2026-08-01T09:45:00Z',
    eventStartsAt: '2026-08-01T10:00:00Z',
    eventEndsAt: '2026-08-02T10:00:00Z',
    externalClosedAt: null,
    scheduleRefreshedAt: '2026-08-01T10:15:00Z',
    tokens: [
      { outcome: 'Yes', outcomeIndex: 0, tokenId: 'token-yes' },
    ],
  };
}
