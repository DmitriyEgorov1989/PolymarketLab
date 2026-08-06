import { useEffect, useRef, useState } from 'react';
import { AddMarketForm } from '../features/markets/components/AddMarketForm';
import { MarketList } from '../features/markets/components/MarketList';
import { useMarketsQuery } from '../features/markets/hooks/useMarketsQuery';
import './CollectorDashboardPage.css';

export function CollectorDashboardPage() {
  const [selectedMarketId, setSelectedMarketId] = useState<string | null>(null);
  const didResolveInitialMarkets = useRef(false);
  const marketsQuery = useMarketsQuery();

  useEffect(() => {
    if (!marketsQuery.isSuccess || didResolveInitialMarkets.current) {
      return;
    }

    didResolveInitialMarkets.current = true;
    const firstMarketId = marketsQuery.data[0]?.marketId ?? null;
    setSelectedMarketId((current) => current ?? firstMarketId);
  }, [marketsQuery.data, marketsQuery.isSuccess]);

  function retryMarkets() {
    void marketsQuery.refetch();
  }

  return (
    <main className="dashboard-shell">
      <section className="hero-panel" aria-labelledby="dashboard-title">
        <p className="eyebrow">PolymarketLab</p>
        <h1 id="dashboard-title">Collector dashboard</h1>
        <p className="hero-copy">
          Выберите рынок, чтобы открыть его детали и управлять collector session.
        </p>
      </section>

      <section className="workspace-grid" aria-label="Collector workspace">
        <article className="card add-market-card">
          <h2>Добавить рынок</h2>
          <AddMarketForm onMarketRegistered={setSelectedMarketId} />
        </article>

        <article className="card market-list-card" aria-labelledby="market-list-title">
          <h2 id="market-list-title">Рынки</h2>
          <MarketList
            markets={marketsQuery.data}
            isPending={marketsQuery.isPending}
            isFetching={marketsQuery.isFetching}
            error={marketsQuery.error}
            selectedMarketId={selectedMarketId}
            onSelectMarket={setSelectedMarketId}
            onRetry={retryMarkets}
          />
        </article>

        <article className="card market-details-card">
          <h2>Детали и collector</h2>
          <p>
            {selectedMarketId === null
              ? 'Выберите рынок из списка.'
              : `Выбран рынок: ${selectedMarketId}`}
          </p>
        </article>
      </section>
    </main>
  );
}
