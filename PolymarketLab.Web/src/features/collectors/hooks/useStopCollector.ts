import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../../api/apiError';
import {
  stopCollector,
  type GetCollectorSessionByIdResponse,
  type GetCollectorSessionByMarketResponse,
  type StopCollectorResponse,
} from '../../../api/collectorsApi';
import { collectorKeys } from '../model/collectorKeys';

export function useStopCollector() {
  const queryClient = useQueryClient();

  return useMutation<StopCollectorResponse, ApiError, string>({
    mutationFn: (sessionId) => stopCollector(sessionId),
    onSuccess: async (response) => {
      await Promise.all([
        queryClient.cancelQueries({
          queryKey: collectorKeys.detail(response.session.sessionId),
          exact: true,
        }),
        queryClient.cancelQueries({
          queryKey: collectorKeys.byMarket(response.session.marketId),
          exact: true,
        }),
      ]);
      queryClient.setQueryData<GetCollectorSessionByIdResponse>(
        collectorKeys.detail(response.session.sessionId),
        { session: response.session },
      );
      queryClient.setQueryData<GetCollectorSessionByMarketResponse>(
        collectorKeys.byMarket(response.session.marketId),
        { session: response.session },
      );
    },
  });
}
