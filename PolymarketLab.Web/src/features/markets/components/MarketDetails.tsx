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

      <dl className="market-details-grid">
        <div>
          <dt>Slug</dt>
          <dd>{market.slug}</dd>
        </div>
        <div>
          <dt>External market ID</dt>
          <dd><code className="market-details-id">{market.externalMarketId}</code></dd>
        </div>
        <div className="market-details-wide">
          <dt>Condition ID</dt>
          <dd><code className="market-details-id">{market.conditionId}</code></dd>
        </div>
        <div>
          <dt>Начало</dt>
          <dd>{formatLocalDate(market.startsAt)}</dd>
        </div>
        <div>
          <dt>Окончание</dt>
          <dd>{formatLocalDate(market.endsAt)}</dd>
        </div>
      </dl>

      <MarketTokens tokens={market.tokens} />
    </div>
  );
}
