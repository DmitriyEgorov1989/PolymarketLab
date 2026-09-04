import { useEffect, useState } from 'react';
import { formatCounter } from '../../../shared/formatters/formatCounter';
import { formatLocalDate } from '../../../shared/formatters/formatLocalDate';
import { formatCollectorCountdown } from '../model/collectorCountdown';
import type { CollectorSession } from '../model/collectorSession';
import { getCollectorPhaseLabel } from '../model/collectorStatus';

interface CollectorLifecycleDetailsProps {
  session: CollectorSession;
}

export function CollectorLifecycleDetails({ session }: CollectorLifecycleDetailsProps) {
  const [nowMs, setNowMs] = useState(Date.now());

  useEffect(() => {
    if (session.effectiveDeadline === null) {
      return undefined;
    }

    const intervalId = window.setInterval(() => setNowMs(Date.now()), 1_000);
    return () => window.clearInterval(intervalId);
  }, [session.effectiveDeadline]);

  const readinessByToken = new Map(
    session.readiness.tokens.map((token) => [token.tokenId, token.initialBookEnqueuedAt]),
  );

  return (
    <section className="collector-lifecycle" aria-labelledby="collector-lifecycle-title">
      <h3 id="collector-lifecycle-title">Lifecycle evidence</h3>

      <LifecycleSection title="Phase and deadline">
        <EvidenceGrid items={[
          ['Phase', getCollectorPhaseLabel(session.phase)],
          ['Effective deadline', formatLocalDate(session.effectiveDeadline)],
          ['Countdown', formatCollectorCountdown(session.effectiveDeadline, nowMs)],
          ['Invalidating at', formatLocalDate(session.invalidatingAt)],
          ['Stop reason', value(session.stopReason)],
        ]} />
      </LifecycleSection>

      <LifecycleSection title="Session snapshot">
        <EvidenceGrid items={[
          ['External event ID', value(session.snapshot.externalEventId)],
          ['Event slug', value(session.snapshot.eventSlug)],
          ['External market ID', value(session.snapshot.externalMarketId)],
          ['Market slug', value(session.snapshot.marketSlug)],
          ['Condition ID', value(session.snapshot.conditionId)],
          ['Window starts', formatLocalDate(session.snapshot.eventStartsAt)],
          ['Window ends', formatLocalDate(session.snapshot.eventEndsAt)],
          ['Projection version', counter(session.snapshot.projectionVersion)],
        ]} />
        {session.snapshot.tokens.length === 0 ? (
          <p className="collector-evidence-empty">No snapshot tokens.</p>
        ) : (
          <ul className="collector-evidence-list">
            {session.snapshot.tokens.map((token) => (
              <li key={token.tokenId}>
                <strong>{token.tokenId} / {token.outcome} / {formatCounter(token.outcomeIndex)}</strong>
                <span>Initial book: {formatLocalDate(readinessByToken.get(token.tokenId) ?? null)}</span>
              </li>
            ))}
          </ul>
        )}
        <p className="collector-evidence-note">
          Current connection epoch: {formatCounter(session.readiness.connectionEpoch)}
        </p>
      </LifecycleSection>

      <LifecycleSection title="Continuity">
        <p className="collector-evidence-note">
          Historical counters are cumulative; remaining rows describe data currently retained.
        </p>
        <EvidenceGrid items={[
          ['Historical received', formatCounter(session.messagesReceived)],
          ['Historical enqueued', formatCounter(session.messagesEnqueued)],
          ['Historical persisted', formatCounter(session.messagesPersisted)],
          ['Remaining raw rows', formatCounter(session.remainingRawMessageCount)],
          ['Reconnect count', formatCounter(session.reconnectCount)],
          ['Last message', formatLocalDate(session.lastMessageAt)],
          ['Subscription ready', formatLocalDate(session.subscriptionReadyAt)],
        ]} />
      </LifecycleSection>

      <LifecycleSection title="Resolution">
        <EvidenceGrid items={[
          ['Signaled at', formatLocalDate(session.resolution.signaledAt)],
          ['Confirmed at', formatLocalDate(session.resolution.confirmedAt)],
          ['Winning token ID', value(session.resolution.winningTokenId)],
          ['Winning outcome', value(session.resolution.winningOutcome)],
          ['Resolution epoch', counter(session.resolution.connectionEpoch)],
          ['Last polling cycle', formatLocalDate(session.resolution.lastPollingCycleAt)],
        ]} />
        <ResolutionSources title="Latest resolution sources" sources={session.resolution.sourceStates} />
        <ResolutionSources title="Confirmation sources" sources={session.resolution.confirmationSources} />
      </LifecycleSection>

      <LifecycleSection title="Normalization">
        {session.normalization === null ? (
          <p className="collector-evidence-empty">
            Normalization evidence is unavailable: <strong>-</strong>
          </p>
        ) : (
          <>
            <EvidenceGrid items={[
              ['Raw count', formatCounter(session.normalization.rawCount)],
              ['Ledger count', formatCounter(session.normalization.ledgerCount)],
              ['Processed count', formatCounter(session.normalization.processedCount)],
              ['Pending count', formatCounter(session.normalization.pendingCount)],
              ['Processing count', formatCounter(session.normalization.processingCount)],
              ['Unsupported count', formatCounter(session.normalization.unsupportedCount)],
              ['Invalid count', formatCounter(session.normalization.invalidCount)],
              ['Failed count', formatCounter(session.normalization.failedCount)],
              ['Missing count', formatCounter(session.normalization.missingCount)],
            ]} />
            <p className="collector-evidence-note">
              Resolution raw item processed: {session.normalization.resolutionRawItemProcessed ? 'Yes' : 'No'}
            </p>
          </>
        )}
      </LifecycleSection>

      <LifecycleSection title="Cleanup">
        {session.cleanup === null ? (
          <p className="collector-evidence-empty">
            Cleanup audit is unavailable: <strong>-</strong>
          </p>
        ) : (
          <EvidenceGrid items={[
            ['Invalidating at', formatLocalDate(session.cleanup.invalidatingAt)],
            ['Cleaned at', formatLocalDate(session.cleanup.cleanedAt)],
            ['Projection version', counter(session.cleanup.projectionVersion)],
            ['Failure code', value(session.cleanup.failureCode)],
            ['Failure message', value(session.cleanup.failureMessage)],
            ['Deleted raw messages', formatCounter(session.cleanup.deletedRawMessageCount)],
            ['Deleted normalization rows', formatCounter(session.cleanup.deletedNormalizationCount)],
            ['Deleted normalized events', formatCounter(session.cleanup.deletedNormalizedEventCount)],
          ]} />
        )}
      </LifecycleSection>
    </section>
  );
}

function LifecycleSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="collector-evidence-section">
      <h4>{title}</h4>
      {children}
    </section>
  );
}

function EvidenceGrid({ items }: { items: Array<[string, string]> }) {
  return (
    <dl className="collector-evidence-grid">
      {items.map(([label, displayValue]) => (
        <div key={label}>
          <dt>{label}</dt>
          <dd>{displayValue}</dd>
        </div>
      ))}
    </dl>
  );
}

function ResolutionSources({
  title,
  sources,
}: {
  title: string;
  sources: CollectorSession['resolution']['sourceStates'];
}) {
  return (
    <section className="collector-resolution-sources">
      <h5>{title}</h5>
      {sources.length === 0 ? (
        <p className="collector-evidence-empty">No source evidence.</p>
      ) : (
        <ul className="collector-evidence-list">
          {sources.map((source, index) => (
            <li key={`${source.source}-${source.observedAt}-${index}`}>
              <strong>{source.source}: {source.status}</strong>
              <span>Observed: {formatLocalDate(source.observedAt)}</span>
              <span>Winner: {value(source.winningTokenId)} / {value(source.winningOutcome)}</span>
              <span>Error: {value(source.errorCode)} / {value(source.errorMessage)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function value(input: string | null): string {
  return input ?? '-';
}

function counter(input: number | null): string {
  return input === null ? '-' : formatCounter(input);
}
