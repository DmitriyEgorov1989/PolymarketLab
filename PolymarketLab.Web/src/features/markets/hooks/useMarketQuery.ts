import { useQuery } from '@tanstack/react-query';
import { ApiError } from '../../../api/apiError';
import { getMarketById, type GetMarketByIdResponse } from '../../../api/marketsApi';
import type { Market } from '../model/market';
import { marketKeys } from '../model/marketKeys';

export function useMarketQuery(marketId: string | null) {
  return useQuery<GetMarketByIdResponse, ApiError, Market>({
    queryKey: marketId === null
      ? [...marketKeys.details(), null]
      : marketKeys.detail(marketId),
    queryFn: ({ signal }) => {
      if (marketId === null) {
        throw new Error('marketId is required to fetch a market.');
      }

      return getMarketById(marketId, signal);
    },
    select: (response) => response.market,
    enabled: marketId !== null,
  });
}
