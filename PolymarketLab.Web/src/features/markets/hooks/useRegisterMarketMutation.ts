import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../../api/apiError';
import {
  registerMarket,
  type RegisterMarketResponse,
} from '../../../api/marketsApi';
import type { RegisterMarketRequest } from '../model/market';
import { marketKeys } from '../model/marketKeys';

export function useRegisterMarketMutation() {
  const queryClient = useQueryClient();

  return useMutation<RegisterMarketResponse, ApiError, RegisterMarketRequest>({
    mutationFn: (request) => registerMarket(request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: marketKeys.list() });
    },
  });
}
