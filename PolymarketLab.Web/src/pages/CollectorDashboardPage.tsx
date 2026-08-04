import { AddMarketForm } from '../features/markets/components/AddMarketForm';
import './CollectorDashboardPage.css';

export function CollectorDashboardPage() {
  return (
    <main className="dashboard-shell">
      <section className="hero-panel" aria-labelledby="dashboard-title">
        <p className="eyebrow">PolymarketLab</p>
        <h1 id="dashboard-title">Collector dashboard</h1>
        <p className="hero-copy">
          Frontend shell готов к подключению фактических backend endpoints для рынков и collector sessions.
        </p>
      </section>

      <section className="workspace-grid" aria-label="Collector workspace">
        <article className="card add-market-card">
          <h2>Добавить рынок</h2>
          <AddMarketForm />
        </article>

        <article className="card market-list-card">
          <h2>Рынки</h2>
          <p>Здесь появится список зарегистрированных рынков.</p>
        </article>

        <article className="card market-details-card">
          <h2>Детали и collector</h2>
          <p>Здесь будут данные выбранного рынка, token ids, статус сессии и counters.</p>
        </article>
      </section>
    </main>
  );
}
