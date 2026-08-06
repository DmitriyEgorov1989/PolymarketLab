import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../../api/apiError';
import {
  stopCollector,
  type StopCollectorResponse,
} from '../../../api/collectorsApi';
import { collectorKeys } from '../model/collectorKeys';

export function useStopCollector() {
  const queryClient = useQueryClient();

  return useMutation<StopCollectorResponse, ApiError, string>({
    mutationFn: (sessionId) => stopCollector(sessionId),
    onSuccess: (response) => Promise.all([
      queryClient.invalidateQueries({
        queryKey: collectorKeys.detail(response.session.sessionId),
      }),
      queryClient.invalidateQueries({
        queryKey: collectorKeys.byMarket(response.session.marketId),
      }),
    ]),
  });
}
