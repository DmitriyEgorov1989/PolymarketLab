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
  created: boolean;
}

export interface StopCollectorRequest {
  sessionId: string;
}

export interface StopCollectorResponse {
  sessionId: string;
  marketId: string;
  status: CollectorSessionStatus;
  stopped: boolean;
}

export function startCollector(
  body: StartCollectorRequest,
  signal?: AbortSignal,
): Promise<StartCollectorResponse> {
  return request<StartCollectorResponse>({
    method: 'POST',
    path: '/api/Collector/start',
    body,
    signal,
  });
}

export function stopCollector(
  body: StopCollectorRequest,
  signal?: AbortSignal,
): Promise<StopCollectorResponse> {
  return request<StopCollectorResponse>({
    method: 'POST',
    path: '/api/Collector/stop',
    body,
    signal,
  });
}
