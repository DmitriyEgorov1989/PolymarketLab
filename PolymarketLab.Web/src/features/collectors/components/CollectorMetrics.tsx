import { formatCounter } from '../../../shared/formatters/formatCounter';
import { formatLocalDate } from '../../../shared/formatters/formatLocalDate';
import type { CollectorSession } from '../model/collectorSession';
import { calculateUnpersisted } from '../model/collectorMetrics';

interface CollectorMetricsProps {
  session: CollectorSession;
}

export function CollectorMetrics({ session }: CollectorMetricsProps) {
  const unpersisted = calculateUnpersisted(
    session.messagesReceived,
    session.messagesPersisted,
  );
  const isCompletedAndPersisted = session.status === 'Stopped'
    && session.messagesReceived === session.messagesPersisted;
  const hasInconsistentCounters = unpersisted < 0;

  return (
    <section className="collector-metrics" aria-labelledby="collector-metrics-title">
      <h3 id="collector-metrics-title">Наблюдаемость</h3>
      <dl className="collector-metrics-grid">
        <div>
          <dt>Messages received</dt>
          <dd>{formatCounter(session.messagesReceived)}</dd>
        </div>
        <div>
          <dt>Messages persisted</dt>
          <dd>{formatCounter(session.messagesPersisted)}</dd>
        </div>
        <div className={unpersisted !== 0 ? 'collector-metric-unpersisted' : undefined}>
          <dt>Unpersisted</dt>
          <dd>{formatCounter(unpersisted)}</dd>
        </div>
        <div>
          <dt>Reconnect count</dt>
          <dd>{formatCounter(session.reconnectCount)}</dd>
        </div>
        <div>
          <dt>Последнее сообщение</dt>
          <dd>{formatLocalDate(session.lastMessageAt)}</dd>
        </div>
      </dl>

      {hasInconsistentCounters ? (
        <p className="collector-persistence-warning" role="alert">
          Ошибка counters: сохранено больше сообщений, чем получено.
        </p>
      ) : unpersisted > 0 ? (
        <p className="collector-persistence-warning" role="status">
          Ожидают сохранения: {formatCounter(unpersisted)}.
        </p>
      ) : isCompletedAndPersisted ? (
        <p className="collector-persistence-success" role="status">
          Проверка завершения пройдена: все полученные сообщения сохранены.
        </p>
      ) : null}
    </section>
  );
}
