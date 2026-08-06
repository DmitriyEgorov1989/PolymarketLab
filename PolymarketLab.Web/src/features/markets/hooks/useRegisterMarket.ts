import { useMutation } from '@tanstack/react-query';
import {
  registerMarket,
  type RegisterMarketRequest,
  type RegisterMarketResponse,
} from '../../../api/marketsApi';
import { ApiError } from '../../../api/apiError';

export function useRegisterMarket() {
  return useMutation<RegisterMarketResponse, ApiError, RegisterMarketRequest>({
    mutationFn: (request) => registerMarket(request),
  });
}
