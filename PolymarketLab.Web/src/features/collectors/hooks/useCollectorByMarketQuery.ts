import { useQuery } from '@tanstack/react-query';
import { ApiError } from '../../../api/apiError';
import {
  getCollectorByMarketId,
  type GetCollectorSessionByMarketResponse,
} from '../../../api/collectorsApi';
import { collectorKeys } from '../model/collectorKeys';
import type { CollectorSession } from '../model/collectorSession';

export function useCollectorByMarketQuery(marketId: string | null) {
  return useQuery<GetCollectorSessionByMarketResponse, ApiError, CollectorSession | null>({
    queryKey: marketId === null
      ? [...collectorKeys.byMarkets(), null]
      : collectorKeys.byMarket(marketId),
    queryFn: ({ signal }) => {
      if (marketId === null) {
        throw new Error('marketId is required to fetch a collector session.');
      }

      return getCollectorByMarketId(marketId, signal);
    },
    select: (response) => response.session,
    enabled: marketId !== null,
  });
}
