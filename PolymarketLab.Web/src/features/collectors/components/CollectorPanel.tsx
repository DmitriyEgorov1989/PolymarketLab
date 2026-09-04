import { formatLocalDate } from '../../../shared/formatters/formatLocalDate';
import { ApiError } from '../../../api/apiError';
import { useCollectorByIdQuery } from '../hooks/useCollectorByIdQuery';
import { useCollectorByMarketQuery } from '../hooks/useCollectorByMarketQuery';
import { useCollectorSlotsQuery } from '../hooks/useCollectorSlotsQuery';
import { useStartCollector } from '../hooks/useStartCollector';
import { useStopCollector } from '../hooks/useStopCollector';
import {
  isExclusiveCollectorStatus,
  isStoppableCollectorStatus,
} from '../model/collectorStatus';
import { CollectorControls } from './CollectorControls';
import { CollectorFailure } from './CollectorFailure';
import { CollectorMetrics } from './CollectorMetrics';
import { CollectorLifecycleDetails } from './CollectorLifecycleDetails';
import { CollectorStatusBadge } from './CollectorStatusBadge';
import './CollectorPanel.css';

interface CollectorPanelProps {
  marketId: string | null;
  registeredMarketIds?: string[];
}

export function CollectorPanel({
  marketId,
  registeredMarketIds = marketId === null ? [] : [marketId],
}: CollectorPanelProps) {
  const slotsQuery = useCollectorSlotsQuery(registeredMarketIds);
  const collectorByMarketQuery = useCollectorByMarketQuery(marketId);
  const startMutation = useStartCollector();
  const stopMutation = useStopCollector();
  const slotSession = slotsQuery.exclusiveSession;
  const marketSession = slotSession !== null && slotSession.marketId === marketId
    ? slotSession
    : collectorByMarketQuery.data;
  const isBlockedByOtherMarket = slotSession !== null && slotSession.marketId !== marketId;
  const startedSessionId = startMutation.data?.marketId === marketId
    ? startMutation.data.sessionId
    : null;
  const shouldTrackStartedSession = startedSessionId !== null
    && (
      marketSession === null
      || marketSession === undefined
      || marketSession.sessionId === startedSessionId
      || collectorByMarketQuery.dataUpdatedAt <= startMutation.submittedAt
    );
  const trackedSessionId = isExclusiveCollectorStatus(marketSession?.status)
    ? marketSession?.sessionId ?? null
    : shouldTrackStartedSession ? startedSessionId : null;
  const collectorByIdQuery = useCollectorByIdQuery(trackedSessionId);
  const matchingMarketSession = marketSession?.sessionId === trackedSessionId
    ? marketSession
    : undefined;
  const session = trackedSessionId === null
    ? marketSession
    : collectorByIdQuery.data ?? matchingMarketSession;
  const collectorError = trackedSessionId === null
    ? collectorByMarketQuery.error
    : collectorByIdQuery.error;
  const isCollectorFetching = trackedSessionId === null
    ? collectorByMarketQuery.isFetching
    : collectorByIdQuery.isFetching;
  const isCollectorPending = trackedSessionId === null
    ? collectorByMarketQuery.isPending
    : collectorByIdQuery.isPending && session === undefined;
  const startError = startMutation.error !== null
    && startMutation.variables?.marketId === marketId
    ? startMutation.error
    : null;
  const stopError = stopMutation.error !== null
    && stopMutation.variables === session?.sessionId
    ? stopMutation.error
    : null;
  const isStartPending = startMutation.isPending
    && startMutation.variables?.marketId === marketId;
  const isStopPending = stopMutation.isPending
    && stopMutation.variables === session?.sessionId;
  const isMutationPending = startMutation.isPending || stopMutation.isPending;

  function startCollector() {
    if (marketId !== null && slotsQuery.isResolved && !isBlockedByOtherMarket) {
      startMutation.mutate({ marketId });
    }
  }

  function stopCollector() {
    if (session !== null
      && session !== undefined
      && isStoppableCollectorStatus(session.status)
      && window.confirm(
        'Досрочный Stop аннулирует dataset, запустит cleanup и завершит session со статусом Failed. Продолжить?',
      )) {
      stopMutation.mutate(session.sessionId);
    }
  }

  function retryCollector() {
    if (trackedSessionId === null) {
      void collectorByMarketQuery.refetch();
    } else {
      void collectorByIdQuery.refetch();
    }
  }

  return (
    <div className="collector-panel">
      <CollectorControls
        marketId={marketId}
        session={session}
        isSessionResolved={marketId !== null && session !== undefined}
        isStartPending={isStartPending}
        isStopPending={isStopPending}
        isMutationPending={isMutationPending}
        isGlobalSlotResolved={slotsQuery.isResolved}
        isBlockedByOtherMarket={isBlockedByOtherMarket}
        onStart={startCollector}
        onStop={stopCollector}
      />

      {!slotsQuery.isResolved && slotsQuery.errors.length === 0 ? (
        <p className="collector-slot-status" role="status">Проверяем global collector slot...</p>
      ) : null}
      {slotsQuery.errors.length > 0 ? (
        <div className="collector-query-error" role="alert">
          <p>Global collector slot не подтверждён.</p>
          {slotsQuery.errors.map((error, index) => (
            <CollectorOperationError
              key={`${error.name}-${error.message}-${index}`}
              error={error}
              nested
            />
          ))}
          <button
            className="collector-retry-button"
            type="button"
            onClick={() => void slotsQuery.retry()}
            disabled={slotsQuery.isFetching}
          >
            Повторить проверку slot
          </button>
        </div>
      ) : isBlockedByOtherMarket ? (
        <p className="collector-slot-warning" role="status">
          Global collector slot занят рынком {slotSession.marketId}.
        </p>
      ) : null}

      {startError !== null ? <CollectorOperationError error={startError} /> : null}
      {stopError !== null ? <CollectorOperationError error={stopError} /> : null}

      {marketId === null ? (
        <p>Выберите рынок, чтобы управлять коллектором.</p>
      ) : isCollectorPending ? (
        <p role="status">Загружаем collector session...</p>
      ) : session === undefined ? (
        <div className="collector-query-error" role="alert">
          <p>{collectorError?.message ?? 'Не удалось загрузить collector session.'}</p>
          <button
            className="collector-retry-button"
            type="button"
            onClick={retryCollector}
            disabled={isCollectorFetching}
          >
            {isCollectorFetching ? 'Повторяем...' : 'Повторить'}
          </button>
        </div>
      ) : (
        <div className="collector-session-content">
          {collectorError !== null ? (
            <div className="collector-query-warning" role="alert">
              <span>{collectorError.message}</span>
              <button
                className="collector-retry-button"
                type="button"
                onClick={retryCollector}
                disabled={isCollectorFetching}
              >
                Повторить
              </button>
            </div>
          ) : isCollectorFetching ? (
            <p className="collector-refresh" role="status">Обновляем collector session...</p>
          ) : null}

          {session === null ? (
            <p className="collector-empty">Для выбранного рынка ещё нет collector sessions.</p>
          ) : (
            <>
              <div className="collector-session-heading">
                <h3>Collector session</h3>
                <CollectorStatusBadge status={session.status} />
              </div>
              <dl className="collector-session-grid">
                <div>
                  <dt>Создана</dt>
                  <dd>{formatLocalDate(session.createdAt)}</dd>
                </div>
                <div>
                  <dt>Запущена</dt>
                  <dd>{formatLocalDate(session.startedAt)}</dd>
                </div>
                <div>
                  <dt>Остановлена</dt>
                  <dd>{formatLocalDate(session.stoppedAt)}</dd>
                </div>
              </dl>
              <CollectorMetrics session={session} />
              <CollectorLifecycleDetails session={session} />
              {session.status === 'Failed'
                || session.failureCode !== null
                || session.failureMessage !== null ? (
                  <CollectorFailure
                    failureCode={session.failureCode}
                    failureMessage={session.failureMessage}
                  />
                ) : null}
            </>
          )}
        </div>
      )}
    </div>
  );
}

function CollectorOperationError({
  error,
  nested = false,
}: {
  error: Error;
  nested?: boolean;
}) {
  const apiError = error instanceof ApiError ? error : null;
  const codes = (apiError?.errors ?? [])
    .map((item) => item.errorCode?.trim())
    .filter((code): code is string => Boolean(code));

  return (
    <div className="collector-operation-error" role={nested ? undefined : 'alert'}>
      <strong>
        {apiError?.status === null || apiError === null
          ? 'HTTP status unavailable'
          : `HTTP ${apiError.status}`}
      </strong>
      {codes.length > 0 ? <code>{codes.join(', ')}</code> : null}
      <span>{error.message}</span>
    </div>
  );
}
