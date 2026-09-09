import { ApiError } from '../../../api/apiError';
import { useCollectorsQuery } from '../hooks/useCollectorsQuery';
import { CollectorPanel } from './CollectorPanel';

export function CollectorJobsPanel() {
  const query = useCollectorsQuery();

  if (query.isPending) {
    return <p role="status">Загружаем collector jobs...</p>;
  }

  if (query.error !== null) {
    return (
      <div role="alert">
        <CollectorJobsError error={query.error} />
        <button type="button" onClick={() => void query.refetch()} disabled={query.isFetching}>
          {query.isFetching ? 'Повторяем...' : 'Повторить'}
        </button>
      </div>
    );
  }

  if (query.data.length === 0) {
    return <p>Нет collector jobs. Добавьте market, чтобы создать задание автоматически.</p>;
  }

  return (
    <div className="collector-jobs-list" aria-label="Collector jobs">
      {query.data.map((session) => (
        <CollectorPanel key={session.sessionId} session={session} />
      ))}
    </div>
  );
}

function CollectorJobsError({ error }: { error: ApiError }) {
  const codes = error.errors
    .map((item) => item.errorCode?.trim())
    .filter((code): code is string => Boolean(code));

  return (
    <p>
      {error.status === null ? 'HTTP status unavailable' : `HTTP ${error.status}`}
      {codes.length > 0 ? ` ${codes.join(', ')}` : ''}: {error.message}
    </p>
  );
}
