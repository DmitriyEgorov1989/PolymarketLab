import type { MarketToken } from '../model/market';
import './MarketTokens.css';

interface MarketTokensProps {
  tokens: MarketToken[];
}

export function MarketTokens({ tokens }: MarketTokensProps) {
  return (
    <section className="market-tokens" aria-labelledby="market-tokens-title">
      <div className="market-tokens-heading">
        <h3 id="market-tokens-title">Токены</h3>
        <span>{tokens.length}</span>
      </div>

      {tokens.length === 0 ? (
        <p className="market-tokens-empty">У этого рынка нет токенов.</p>
      ) : (
        <ul className="market-token-list">
          {tokens.map((token) => (
            <li className="market-token" key={token.tokenId}>
              <div className="market-token-summary">
                <strong>{token.outcome}</strong>
                <span>Outcome index: {token.outcomeIndex}</span>
              </div>
              <div className="market-token-id-row">
                <span>Token ID</span>
                <code className="market-token-id">{token.tokenId}</code>
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
