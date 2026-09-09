import { formatLocalDate } from '../../../shared/formatters/formatLocalDate';
import { ApiError } from '../../../api/apiError';
import { useStopCollector } from '../hooks/useStopCollector';
import {
  isStoppableCollectorStatus,
} from '../model/collectorStatus';
import { CollectorControls } from './CollectorControls';
import { CollectorFailure } from './CollectorFailure';
import { CollectorMetrics } from './CollectorMetrics';
import { CollectorLifecycleDetails } from './CollectorLifecycleDetails';
import { CollectorStatusBadge } from './CollectorStatusBadge';
import './CollectorPanel.css';

interface CollectorPanelProps {
  session: import('../model/collectorSession').CollectorSession;
}

export function CollectorPanel({ session }: CollectorPanelProps) {
  const stopMutation = useStopCollector();
  const stopError = stopMutation.error !== null
    && stopMutation.variables === session.sessionId
    ? stopMutation.error
    : null;
  const isStopPending = stopMutation.isPending
    && stopMutation.variables === session.sessionId;

  function stopCollector() {
    if (isStoppableCollectorStatus(session.status)
      && window.confirm(
        'Досрочный Stop аннулирует dataset, запустит cleanup и завершит session со статусом Failed. Продолжить?',
      )) {
      stopMutation.mutate(session.sessionId);
    }
  }

  return (
    <div className="collector-panel">
      <CollectorControls
        session={session}
        isStopPending={isStopPending}
        onStop={stopCollector}
      />

      {stopError !== null ? <CollectorOperationError error={stopError} /> : null}
        <div className="collector-session-content">
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
        </div>
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
