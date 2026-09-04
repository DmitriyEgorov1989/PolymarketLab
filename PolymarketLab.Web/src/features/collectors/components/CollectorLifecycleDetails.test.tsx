// @vitest-environment jsdom

import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createCollectorSession } from '../testing/createCollectorSession';
import { CollectorLifecycleDetails } from './CollectorLifecycleDetails';

describe('CollectorLifecycleDetails', () => {
  afterEach(() => vi.useRealTimers());

  it('renders lifecycle timing, readiness, resolution, normalization and cleanup evidence', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-08-06T12:09:30Z'));
    const session = createCollectorSession({
      status: 'Stopping',
      phase: 'AwaitingNormalization',
      effectiveDeadline: '2026-08-06T12:10:00Z',
      normalization: {
        rawCount: 1_250, ledgerCount: 1_249, processedCount: 1_240,
        pendingCount: 5, processingCount: 4, unsupportedCount: 1,
        invalidCount: 2, failedCount: 3, missingCount: 1,
        resolutionRawItemProcessed: true,
      },
      resolution: {
        signaledAt: '2026-08-06T12:05:01Z',
        confirmedAt: '2026-08-06T12:05:03Z',
        winningTokenId: 'token-yes', winningOutcome: 'Yes', connectionEpoch: 2,
        lastPollingCycleAt: '2026-08-06T12:05:02Z',
        sourceStates: [{
          source: 'Gamma', status: 'Failed', observedAt: '2026-08-06T12:05:02Z',
          winningTokenId: null, winningOutcome: null,
          errorCode: 'gamma.timeout', errorMessage: 'Timed out.',
        }],
        confirmationSources: [{
          source: 'WebSocket', status: 'Terminal', observedAt: '2026-08-06T12:05:01Z',
          winningTokenId: 'token-yes', winningOutcome: 'Yes',
          errorCode: null, errorMessage: null,
        }],
      },
      cleanup: {
        invalidatingAt: '2026-08-06T12:10:01Z', cleanedAt: '2026-08-06T12:10:02Z',
        projectionVersion: 1, failureCode: 'collector.stop.requested',
        failureMessage: 'Stopped by user.', deletedRawMessageCount: 1_250,
        deletedNormalizationCount: 1_249, deletedNormalizedEventCount: 12,
      },
    });

    render(<CollectorLifecycleDetails session={session} />);

    expect(screen.getByText('AwaitingNormalization')).toBeTruthy();
    expect(screen.getByText('00:30')).toBeTruthy();
    act(() => vi.advanceTimersByTime(1_000));
    expect(screen.getByText('00:29')).toBeTruthy();
    expect(screen.getAllByText('Projection version')).toHaveLength(2);
    expect(screen.getByText('token-yes / Yes / 0')).toBeTruthy();
    expect(screen.getByText('Current connection epoch: 1')).toBeTruthy();
    expect(screen.getByText('Historical received')).toBeTruthy();
    expect(screen.getByText('Remaining raw rows')).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Latest resolution sources' })).toBeTruthy();
    expect(screen.getByText(/gamma\.timeout/)).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Confirmation sources' })).toBeTruthy();
    expect(screen.getByText(/Resolution raw item processed: Yes/)).toBeTruthy();
    expect(screen.getByText('Deleted raw messages')).toBeTruthy();
  });

  it('renders null and unknown legacy lifecycle values safely', () => {
    const session = createCollectorSession({
      status: 'FutureStatus' as never,
      phase: 'FuturePhase', effectiveDeadline: null,
      snapshot: {
        externalEventId: null, eventSlug: null, externalMarketId: null, marketSlug: null,
        conditionId: null, eventStartsAt: null, eventEndsAt: null,
        projectionVersion: null, tokens: [],
      },
      normalization: null,
    });

    render(<CollectorLifecycleDetails session={session} />);

    expect(screen.getByText('Unknown')).toBeTruthy();
    expect(screen.getAllByText('-').length).toBeGreaterThan(5);
    expect(screen.getByText('No snapshot tokens.')).toBeTruthy();
    expect(screen.getByText(/Normalization evidence is unavailable/).textContent).toContain('-');
    expect(screen.getByText(/Cleanup audit is unavailable/).textContent).toContain('-');
  });
});
