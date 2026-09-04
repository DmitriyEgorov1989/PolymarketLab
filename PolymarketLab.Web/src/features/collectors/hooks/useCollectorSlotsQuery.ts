import { useQueries } from '@tanstack/react-query';
import {
  getCollectorByMarketId,
  type GetCollectorSessionByMarketResponse,
} from '../../../api/collectorsApi';
import { collectorKeys } from '../model/collectorKeys';
import type { CollectorSession } from '../model/collectorSession';
import {
  ACTIVE_COLLECTOR_POLL_INTERVAL_MS,
  isExclusiveCollectorStatus,
} from '../model/collectorStatus';

export function useCollectorSlotsQuery(marketIds: string[]) {
  const queries = useQueries({
    queries: marketIds.map((marketId) => ({
      queryKey: collectorKeys.byMarket(marketId),
      queryFn: ({ signal }: { signal: AbortSignal }) => getCollectorByMarketId(marketId, signal),
      refetchInterval: (query: { state: { data?: GetCollectorSessionByMarketResponse } }) => (
        isExclusiveCollectorStatus(query.state.data?.session?.status)
          ? ACTIVE_COLLECTOR_POLL_INTERVAL_MS
          : false
      ),
    })),
  });
  const errors = queries
    .map((query) => query.error)
    .filter((error): error is Error => error !== null);
  const isPending = queries.some((query) => query.isPending);
  const exclusiveSession = queries
    .map((query) => query.data?.session)
    .find((session): session is CollectorSession => (
      session !== null
      && session !== undefined
      && isExclusiveCollectorStatus(session.status)
    )) ?? null;

  return {
    exclusiveSession,
    errors,
    isPending,
    isResolved: !isPending && errors.length === 0,
    isFetching: queries.some((query) => query.isFetching),
    retry: () => Promise.all(
      queries.filter((query) => query.error !== null).map((query) => query.refetch()),
    ),
  };
}
