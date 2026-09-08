import type { CollectorSession } from '../model/collectorSession';
import {
  isPollableCollectorStatus,
  isStoppableCollectorStatus,
} from '../model/collectorStatus';

interface CollectorControlsProps {
  marketId: string | null;
  session: CollectorSession | null | undefined;
  isSessionResolved: boolean;
  isStartPending: boolean;
  isStopPending: boolean;
  isMutationPending: boolean;
  /** Оставлено необязательным для совместимости существующих callers; slot глобальным не является. */
  isGlobalSlotResolved?: boolean;
  isBlockedByOtherMarket?: boolean;
  onStart: () => void;
  onStop: () => void;
}

export function CollectorControls({
  marketId,
  session,
  isSessionResolved,
  isStartPending,
  isStopPending,
  isMutationPending,
  onStart,
  onStop,
}: CollectorControlsProps) {
  const isActive = isPollableCollectorStatus(session?.status);
  const isStoppable = isStoppableCollectorStatus(session?.status);
  const canStart = marketId !== null
    && isSessionResolved
    && !isActive
    && !isMutationPending;
  const canStop = session !== null && session !== undefined && isStoppable && !isMutationPending;

  return (
    <div className="collector-controls" aria-busy={isMutationPending}>
      <button
        className="collector-button collector-start-button"
        type="button"
        onClick={onStart}
        disabled={!canStart}
      >
        {isStartPending ? 'Запускаем...' : 'Start collector'}
      </button>
      <button
        className="collector-button collector-stop-button"
        type="button"
        onClick={onStop}
        disabled={!canStop}
      >
        {isStopPending ? 'Останавливаем...' : 'Stop collector'}
      </button>
    </div>
  );
}
