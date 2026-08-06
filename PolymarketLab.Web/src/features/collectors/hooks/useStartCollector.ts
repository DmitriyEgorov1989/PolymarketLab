import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../../api/apiError';
import {
  startCollector,
  type StartCollectorResponse,
} from '../../../api/collectorsApi';
import { collectorKeys } from '../model/collectorKeys';
import type { StartCollectorRequest } from '../model/collectorSession';

export function useStartCollector() {
  const queryClient = useQueryClient();

  return useMutation<StartCollectorResponse, ApiError, StartCollectorRequest>({
    mutationFn: (request) => startCollector(request),
    onSuccess: (response) => Promise.all([
      queryClient.invalidateQueries({
        queryKey: collectorKeys.byMarket(response.marketId),
      }),
      queryClient.invalidateQueries({
        queryKey: collectorKeys.detail(response.sessionId),
      }),
    ]),
  });
}
