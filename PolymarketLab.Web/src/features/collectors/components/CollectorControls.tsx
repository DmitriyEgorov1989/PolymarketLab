import type { CollectorSession } from '../model/collectorSession';
import { isStoppableCollectorStatus } from '../model/collectorStatus';

interface CollectorControlsProps {
  session: CollectorSession;
  isStopPending: boolean;
  onStop: () => void;
}

export function CollectorControls({
  session,
  isStopPending,
  onStop,
}: CollectorControlsProps) {
  const canStop = isStoppableCollectorStatus(session.status) && !isStopPending;

  return (
    <div className="collector-controls" aria-busy={isStopPending}>
      <button
        className="collector-button collector-stop-button"
        type="button"
        onClick={onStop}
        disabled={!canStop}
      >
        {isStopPending ? 'Отменяем...' : 'Отменить задание'}
      </button>
    </div>
  );
}
