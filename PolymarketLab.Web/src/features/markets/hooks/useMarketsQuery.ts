import { useQuery } from '@tanstack/react-query';
import { ApiError } from '../../../api/apiError';
import { getMarkets, type GetMarketsResponse } from '../../../api/marketsApi';
import type { Market } from '../model/market';
import { marketKeys } from '../model/marketKeys';

export function useMarketsQuery() {
  return useQuery<GetMarketsResponse, ApiError, Market[]>({
    queryKey: marketKeys.list(),
    queryFn: ({ signal }) => getMarkets(signal),
    select: (response) => response.markets,
  });
}
