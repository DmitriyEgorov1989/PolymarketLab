import type { Market } from '../model/market';

interface MarketListItemProps {
  market: Market;
  isSelected: boolean;
  onSelect: (marketId: string) => void;
}

export function MarketListItem({ market, isSelected, onSelect }: MarketListItemProps) {
  return (
    <li className="market-list-item">
      <button
        className="market-list-button"
        type="button"
        aria-pressed={isSelected}
        onClick={() => onSelect(market.marketId)}
      >
        <span className="market-list-question">{market.question}</span>
        <span className="market-list-meta">
          <span className="market-list-slug">{market.marketSlug}</span>
          {isSelected ? <span className="market-list-selected">Выбран</span> : null}
        </span>
      </button>
    </li>
  );
}
