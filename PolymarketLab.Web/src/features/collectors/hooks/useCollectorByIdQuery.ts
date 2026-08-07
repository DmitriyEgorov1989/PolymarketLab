import { useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../../api/apiError';
import {
  getCollectorById,
  type GetCollectorSessionByMarketResponse,
  type GetCollectorSessionByIdResponse,
} from '../../../api/collectorsApi';
import { collectorKeys } from '../model/collectorKeys';
import type { CollectorSession } from '../model/collectorSession';
import {
  ACTIVE_COLLECTOR_POLL_INTERVAL_MS,
  isActiveCollectorStatus,
} from '../model/collectorStatus';

export function useCollectorByIdQuery(sessionId: string | null) {
  const queryClient = useQueryClient();
  const query = useQuery<GetCollectorSessionByIdResponse, ApiError, CollectorSession>({
    queryKey: sessionId === null
      ? [...collectorKeys.details(), null]
      : collectorKeys.detail(sessionId),
    queryFn: ({ signal }) => {
      if (sessionId === null) {
        throw new Error('sessionId is required to fetch a collector session.');
      }

      return getCollectorById(sessionId, signal);
    },
    select: (response) => response.session,
    enabled: sessionId !== null,
    refetchInterval: (collectorQuery) => {
      const status = collectorQuery.state.data?.session.status;

      return status === undefined || isActiveCollectorStatus(status)
        ? ACTIVE_COLLECTOR_POLL_INTERVAL_MS
        : false;
    },
  });

  useEffect(() => {
    if (query.data === undefined) {
      return;
    }

    queryClient.setQueryData<GetCollectorSessionByMarketResponse>(
      collectorKeys.byMarket(query.data.marketId),
      (current) => {
        const currentSession = current?.session;

        if (currentSession !== null
          && currentSession !== undefined
          && currentSession.sessionId !== query.data.sessionId) {
          if (isActiveCollectorStatus(currentSession.status)) {
            return current;
          }

          const currentCreatedAt = Date.parse(currentSession.createdAt);
          const detailCreatedAt = Date.parse(query.data.createdAt);

          if (!Number.isNaN(currentCreatedAt)
            && !Number.isNaN(detailCreatedAt)
            && currentCreatedAt >= detailCreatedAt) {
            return current;
          }
        }

        return { session: query.data };
      },
    );
  }, [query.data, queryClient]);

  return query;
}
