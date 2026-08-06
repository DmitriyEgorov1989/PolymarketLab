import { useQuery } from '@tanstack/react-query';
import { ApiError } from '../../../api/apiError';
import {
  getCollectorById,
  type GetCollectorSessionByIdResponse,
} from '../../../api/collectorsApi';
import { collectorKeys } from '../model/collectorKeys';
import type { CollectorSession } from '../model/collectorSession';

export function useCollectorByIdQuery(sessionId: string | null) {
  return useQuery<GetCollectorSessionByIdResponse, ApiError, CollectorSession>({
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
  });
}
