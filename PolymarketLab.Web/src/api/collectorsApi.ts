import { request } from './httpClient';

export type CollectorSessionStatus =
  | 'Starting'
  | 'Running'
  | 'Stopping'
  | 'Stopped'
  | 'Failed'
  | 'Interrupted';

export interface StartCollectorRequest {
  marketId: string;
}

export interface StartCollectorResponse {
  sessionId: string;
  marketId: string;
  status: CollectorSessionStatus;
}

export interface CollectorSessionResponse {
  sessionId: string;
  marketId: string;
  status: CollectorSessionStatus;
  createdAt: string;
  startedAt: string | null;
  stoppedAt: string | null;
  failureCode: string | null;
  failureMessage: string | null;
}

export interface StopCollectorResponse {
  session: CollectorSessionResponse;
}

export interface GetCollectorSessionByIdResponse {
  session: CollectorSessionResponse;
}

export interface GetCollectorSessionByMarketResponse {
  session: CollectorSessionResponse | null;
}

export function getCollectorById(
  sessionId: string,
  signal?: AbortSignal,
): Promise<GetCollectorSessionByIdResponse> {
  return request<GetCollectorSessionByIdResponse>({
    method: 'GET',
    path: `/api/Collector/${encodeURIComponent(sessionId)}`,
    signal,
  });
}

export function getCollectorByMarketId(
  marketId: string,
  signal?: AbortSignal,
): Promise<GetCollectorSessionByMarketResponse> {
  return request<GetCollectorSessionByMarketResponse>({
    method: 'GET',
    path: `/api/Collector/by-market/${encodeURIComponent(marketId)}`,
    signal,
  });
}

export function startCollector(
  body: StartCollectorRequest,
  signal?: AbortSignal,
): Promise<StartCollectorResponse> {
  return request<StartCollectorResponse>({
    method: 'POST',
    path: '/api/Collector',
    body,
    signal,
  });
}

export function stopCollector(
  sessionId: string,
  signal?: AbortSignal,
): Promise<StopCollectorResponse> {
  return request<StopCollectorResponse>({
    method: 'POST',
    path: `/api/Collector/${encodeURIComponent(sessionId)}/stop`,
    signal,
  });
}
