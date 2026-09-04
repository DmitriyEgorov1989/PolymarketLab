import type {
  CollectorNormalizationResponse,
  CollectorReadinessResponse,
  CollectorResolutionResponse,
  CollectorSessionResponse,
  CollectorSessionSnapshotResponse,
} from '../../../api/collectorsApi';

type CollectorSessionOverrides = Omit<
  Partial<CollectorSessionResponse>,
  'snapshot' | 'readiness' | 'normalization' | 'resolution'
> & {
  snapshot?: Partial<CollectorSessionSnapshotResponse>;
  readiness?: Partial<CollectorReadinessResponse>;
  normalization?: Partial<CollectorNormalizationResponse> | null;
  resolution?: Partial<CollectorResolutionResponse>;
};

export function createCollectorSession(
  overrides: CollectorSessionOverrides = {},
): CollectorSessionResponse {
  const session: CollectorSessionResponse = {
    sessionId: 'session-id',
    marketId: 'market-id',
    snapshot: {
      externalEventId: 'event-id',
      eventSlug: 'event-slug',
      externalMarketId: 'external-market-id',
      marketSlug: 'market-slug',
      conditionId: 'condition-id',
      eventStartsAt: '2026-08-06T12:05:00Z',
      eventEndsAt: '2026-08-06T12:10:00Z',
      projectionVersion: 1,
      tokens: [
        { tokenId: 'token-yes', outcome: 'Yes', outcomeIndex: 0 },
        { tokenId: 'token-no', outcome: 'No', outcomeIndex: 1 },
      ],
    },
    status: 'Running',
    phase: 'CollectingWindow',
    effectiveDeadline: '2026-08-06T12:10:00Z',
    createdAt: '2026-08-06T12:00:00Z',
    startedAt: '2026-08-06T12:00:01Z',
    subscriptionReadyAt: '2026-08-06T12:00:02Z',
    stoppedAt: null,
    invalidatingAt: null,
    stopReason: null,
    failureCode: null,
    failureMessage: null,
    readiness: {
      connectionEpoch: 1,
      tokens: [
        { tokenId: 'token-yes', initialBookEnqueuedAt: '2026-08-06T12:00:01Z' },
        { tokenId: 'token-no', initialBookEnqueuedAt: '2026-08-06T12:00:02Z' },
      ],
    },
    messagesReceived: 120,
    messagesEnqueued: 120,
    messagesPersisted: 118,
    remainingRawMessageCount: 118,
    lastMessageAt: '2026-08-06T12:09:59Z',
    reconnectCount: 0,
    normalization: {
      rawCount: 118,
      ledgerCount: 118,
      processedCount: 118,
      pendingCount: 0,
      processingCount: 0,
      unsupportedCount: 0,
      invalidCount: 0,
      failedCount: 0,
      missingCount: 0,
      resolutionRawItemProcessed: false,
    },
    resolution: {
      signaledAt: null,
      confirmedAt: null,
      winningTokenId: null,
      winningOutcome: null,
      connectionEpoch: null,
      lastPollingCycleAt: null,
      sourceStates: [],
      confirmationSources: [],
    },
    cleanup: null,
  };

  return {
    ...session,
    ...overrides,
    snapshot: { ...session.snapshot, ...overrides.snapshot },
    readiness: { ...session.readiness, ...overrides.readiness },
    normalization: overrides.normalization === null
      ? null
      : { ...session.normalization!, ...overrides.normalization },
    resolution: { ...session.resolution, ...overrides.resolution },
  };
}
