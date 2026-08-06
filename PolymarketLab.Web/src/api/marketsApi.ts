import { request } from './httpClient';

export interface RegisterMarketRequest {
  marketUri: string;
}

export interface RegisterMarketResponse {
  marketId: string;
  created: boolean;
}

export interface MarketTokenResponse {
  tokenId: string;
  outcome: string;
  outcomeIndex: number;
}

export interface MarketResponse {
  marketId: string;
  externalMarketId: string;
  slug: string;
  conditionId: string;
  question: string;
  startsAt: string | null;
  endsAt: string | null;
  tokens: MarketTokenResponse[];
}

export interface GetMarketsResponse {
  markets: MarketResponse[];
}

export interface GetMarketByIdResponse {
  market: MarketResponse;
}

export function getMarkets(signal?: AbortSignal): Promise<GetMarketsResponse> {
  return request<GetMarketsResponse>({
    method: 'GET',
    path: '/api/Market',
    signal,
  });
}

export function getMarketById(
  marketId: string,
  signal?: AbortSignal,
): Promise<GetMarketByIdResponse> {
  return request<GetMarketByIdResponse>({
    method: 'GET',
    path: `/api/Market/${encodeURIComponent(marketId)}`,
    signal,
  });
}

export function registerMarket(
  body: RegisterMarketRequest,
  signal?: AbortSignal,
): Promise<RegisterMarketResponse> {
  return request<RegisterMarketResponse>({
    method: 'POST',
    path: '/api/Market',
    body,
    signal,
  });
}
