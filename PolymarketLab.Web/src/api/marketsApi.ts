import { request } from './httpClient';

export interface RegisterMarketRequest {
  marketUri: string;
}

export interface RegisterMarketResponse {
  marketId: string;
  created: boolean;
}

export function registerMarket(
  body: RegisterMarketRequest,
  signal?: AbortSignal,
): Promise<RegisterMarketResponse> {
  return request<RegisterMarketResponse>({
    method: 'POST',
    path: '/api/Market/register',
    body,
    signal,
  });
}
