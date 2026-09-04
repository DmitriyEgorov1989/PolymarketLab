import { request } from './httpClient';

export type CollectorSessionStatus =
  | 'Scheduled'
  | 'Starting'
  | 'Running'
  | 'Stopping'
  | 'Invalidating'
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

export interface CollectorSessionTokenResponse {
  tokenId: string;
  outcome: string;
  outcomeIndex: number;
}

export interface CollectorSessionSnapshotResponse {
  externalEventId: string | null;
  eventSlug: string | null;
  externalMarketId: string | null;
  marketSlug: string | null;
  conditionId: string | null;
  eventStartsAt: string | null;
  eventEndsAt: string | null;
  projectionVersion: number | null;
  tokens: CollectorSessionTokenResponse[];
}

export interface CollectorTokenReadinessResponse {
  tokenId: string;
  initialBookEnqueuedAt: string | null;
}

export interface CollectorReadinessResponse {
  connectionEpoch: number;
  tokens: CollectorTokenReadinessResponse[];
}

export interface CollectorNormalizationResponse {
  rawCount: number;
  ledgerCount: number;
  processedCount: number;
  pendingCount: number;
  processingCount: number;
  unsupportedCount: number;
  invalidCount: number;
  failedCount: number;
  missingCount: number;
  resolutionRawItemProcessed: boolean;
}

export interface CollectorResolutionSourceResponse {
  source: string;
  status: string;
  observedAt: string;
  winningTokenId: string | null;
  winningOutcome: string | null;
  errorCode: string | null;
  errorMessage: string | null;
}

export interface CollectorResolutionResponse {
  signaledAt: string | null;
  confirmedAt: string | null;
  winningTokenId: string | null;
  winningOutcome: string | null;
  connectionEpoch: number | null;
  lastPollingCycleAt: string | null;
  sourceStates: CollectorResolutionSourceResponse[];
  confirmationSources: CollectorResolutionSourceResponse[];
}

export interface CollectorCleanupResponse {
  invalidatingAt: string | null;
  cleanedAt: string;
  projectionVersion: number | null;
  failureCode: string | null;
  failureMessage: string | null;
  deletedRawMessageCount: number;
  deletedNormalizationCount: number;
  deletedNormalizedEventCount: number;
}

export interface CollectorSessionResponse {
  sessionId: string;
  marketId: string;
  snapshot: CollectorSessionSnapshotResponse;
  status: CollectorSessionStatus;
  phase: string | null;
  effectiveDeadline: string | null;
  createdAt: string;
  startedAt: string | null;
  subscriptionReadyAt: string | null;
  stoppedAt: string | null;
  invalidatingAt: string | null;
  stopReason: string | null;
  failureCode: string | null;
  failureMessage: string | null;
  readiness: CollectorReadinessResponse;
  messagesReceived: number;
  messagesEnqueued: number;
  messagesPersisted: number;
  remainingRawMessageCount: number;
  lastMessageAt: string | null;
  reconnectCount: number;
  normalization: CollectorNormalizationResponse | null;
  resolution: CollectorResolutionResponse;
  cleanup: CollectorCleanupResponse | null;
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
