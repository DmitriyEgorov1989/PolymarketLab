import { ApiError } from '../../../api/apiError';
import { formatLocalDate } from '../../../shared/formatters/formatLocalDate';
import type { Market } from '../model/market';
import { MarketTokens } from './MarketTokens';
import './MarketDetails.css';

interface MarketDetailsProps {
  marketId: string | null;
  market: Market | undefined;
  isPending: boolean;
  isFetching: boolean;
  error: ApiError | null;
  onRetry: () => void;
}

export function MarketDetails({
  marketId,
  market,
  isPending,
  isFetching,
  error,
  onRetry,
}: MarketDetailsProps) {
  if (marketId === null) {
    return <p>Выберите рынок из списка.</p>;
  }

  if (isPending) {
    return <p role="status">Загружаем детали рынка...</p>;
  }

  if (market === undefined) {
    return (
      <div className="market-details-state" role="alert">
        <p>{error?.message ?? 'Не удалось загрузить детали рынка.'}</p>
        <button className="secondary-button" type="button" onClick={onRetry} disabled={isFetching}>
          {isFetching ? 'Повторяем...' : 'Повторить'}
        </button>
      </div>
    );
  }

  return (
    <div className="market-details-content">
      {error !== null ? (
        <div className="market-details-warning" role="alert">
          <span>{error.message}</span>
          <button className="secondary-button" type="button" onClick={onRetry} disabled={isFetching}>
            Повторить
          </button>
        </div>
      ) : isFetching ? (
        <p className="market-details-refresh" role="status">Обновляем детали...</p>
      ) : null}

      <div className="market-details-header">
        <span className="market-details-label">Question</span>
        <h3>{market.question}</h3>
      </div>

      <h4 className="market-details-section-title">Event identity</h4>
      <dl className="market-details-grid">
        <div>
          <dt>Event slug</dt>
          <dd>{market.eventSlug}</dd>
        </div>
        <div>
          <dt>External event ID</dt>
          <dd><code className="market-details-id">{market.externalEventId}</code></dd>
        </div>
      </dl>

      <h4 className="market-details-section-title">Market identity</h4>
      <dl className="market-details-grid">
        <div>
          <dt>Market slug</dt>
          <dd>{market.marketSlug}</dd>
        </div>
        <div>
          <dt>Market ID</dt>
          <dd><code className="market-details-id">{market.marketId}</code></dd>
        </div>
        <div>
          <dt>External market ID</dt>
          <dd><code className="market-details-id">{market.externalMarketId}</code></dd>
        </div>
        <div className="market-details-wide">
          <dt>Condition ID</dt>
          <dd><code className="market-details-id">{market.conditionId}</code></dd>
        </div>
      </dl>

      <h4 className="market-details-section-title">Schedule</h4>
      <dl className="market-details-grid">
        <div>
          <dt>Discovered at</dt>
          <dd>{formatLocalDate(market.discoveredAt)}</dd>
        </div>
        <div>
          <dt>External created at</dt>
          <dd>{formatLocalDate(market.externalCreatedAt)}</dd>
        </div>
        <div>
          <dt>Orders opened at</dt>
          <dd>{formatLocalDate(market.ordersOpenedAt)}</dd>
        </div>
        <div>
          <dt>Gamma start date</dt>
          <dd>{formatLocalDate(market.gammaStartDate)}</dd>
        </div>
        <div>
          <dt>Event starts at</dt>
          <dd>{formatLocalDate(market.eventStartsAt)}</dd>
        </div>
        <div>
          <dt>Event ends at</dt>
          <dd>{formatLocalDate(market.eventEndsAt)}</dd>
        </div>
        <div>
          <dt>External closed at</dt>
          <dd>{formatLocalDate(market.externalClosedAt)}</dd>
        </div>
        <div>
          <dt>Schedule refreshed at</dt>
          <dd>{formatLocalDate(market.scheduleRefreshedAt)}</dd>
        </div>
      </dl>

      <MarketTokens tokens={market.tokens} />
    </div>
  );
}
