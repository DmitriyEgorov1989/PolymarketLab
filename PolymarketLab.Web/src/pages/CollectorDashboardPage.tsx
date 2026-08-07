import { useEffect, useRef, useState } from 'react';
import { CollectorPanel } from '../features/collectors/components/CollectorPanel';
import { AddMarketForm } from '../features/markets/components/AddMarketForm';
import { MarketDetails } from '../features/markets/components/MarketDetails';
import { MarketList } from '../features/markets/components/MarketList';
import { useMarketQuery } from '../features/markets/hooks/useMarketQuery';
import { useMarketsQuery } from '../features/markets/hooks/useMarketsQuery';
import './CollectorDashboardPage.css';

export function CollectorDashboardPage() {
  const [selectedMarketId, setSelectedMarketId] = useState<string | null>(null);
  const didResolveInitialMarkets = useRef(false);
  const marketsQuery = useMarketsQuery();
  const marketQuery = useMarketQuery(selectedMarketId);

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

  function retryMarket() {
    void marketQuery.refetch();
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

        <article className="card market-details-card" aria-labelledby="market-details-title">
          <h2 id="market-details-title">Детали рынка</h2>
          <MarketDetails
            marketId={selectedMarketId}
            market={marketQuery.data}
            isPending={marketQuery.isPending}
            isFetching={marketQuery.isFetching}
            error={marketQuery.error}
            onRetry={retryMarket}
          />
        </article>

        <article className="card collector-card" aria-labelledby="collector-panel-title">
          <h2 id="collector-panel-title">Управление коллектором</h2>
          <CollectorPanel marketId={selectedMarketId} />
        </article>
      </section>
    </main>
  );
}
