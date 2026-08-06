import { ApiError } from '../../../api/apiError';
import type { Market } from '../model/market';
import { MarketListItem } from './MarketListItem';
import './MarketList.css';

interface MarketListProps {
  markets: Market[] | undefined;
  isPending: boolean;
  isFetching: boolean;
  error: ApiError | null;
  selectedMarketId: string | null;
  onSelectMarket: (marketId: string) => void;
  onRetry: () => void;
}

export function MarketList({
  markets,
  isPending,
  isFetching,
  error,
  selectedMarketId,
  onSelectMarket,
  onRetry,
}: MarketListProps) {
  if (isPending) {
    return <p role="status">Загружаем рынки...</p>;
  }

  if (markets === undefined) {
    return (
      <div className="market-list-state" role="alert">
        <p>{error?.message ?? 'Не удалось загрузить рынки.'}</p>
        <button className="secondary-button" type="button" onClick={onRetry} disabled={isFetching}>
          {isFetching ? 'Повторяем...' : 'Повторить'}
        </button>
      </div>
    );
  }

  return (
    <div className="market-list-content">
      {error !== null ? (
        <div className="market-list-warning" role="alert">
          <span>{error.message}</span>
          <button className="secondary-button" type="button" onClick={onRetry} disabled={isFetching}>
            Повторить
          </button>
        </div>
      ) : isFetching ? (
        <p className="market-list-refresh" role="status">Обновляем список...</p>
      ) : null}

      {markets.length === 0 ? (
        <p>Зарегистрированных рынков пока нет. Добавьте рынок по ссылке выше.</p>
      ) : (
        <ul className="market-list" aria-label="Зарегистрированные рынки">
          {markets.map((market) => (
            <MarketListItem
              key={market.marketId}
              market={market}
              isSelected={market.marketId === selectedMarketId}
              onSelect={onSelectMarket}
            />
          ))}
        </ul>
      )}
    </div>
  );
}
