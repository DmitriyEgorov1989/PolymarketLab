import { useQuery } from '@tanstack/react-query';
import { ApiError } from '../../../api/apiError';
import { getCollectors, type GetCollectorSessionsResponse } from '../../../api/collectorsApi';
import type { CollectorSession } from '../model/collectorSession';
import { collectorKeys } from '../model/collectorKeys';
import {
  ACTIVE_COLLECTOR_POLL_INTERVAL_MS,
  isPollableCollectorStatus,
} from '../model/collectorStatus';

export function useCollectorsQuery() {
  return useQuery<GetCollectorSessionsResponse, ApiError, CollectorSession[]>({
    queryKey: collectorKeys.current(),
    queryFn: ({ signal }) => getCollectors(signal),
    select: (response) => response.sessions,
    refetchInterval: (query) => {
      const sessions = query.state.data?.sessions ?? [];
      return sessions.some((session) => isPollableCollectorStatus(session.status))
        ? ACTIVE_COLLECTOR_POLL_INTERVAL_MS
        : false;
    },
  });
}
