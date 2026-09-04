export const ACTIVE_COLLECTOR_POLL_INTERVAL_MS = 2_000;

const POLLABLE_COLLECTOR_STATUSES = new Set([
  'Scheduled',
  'Starting',
  'Running',
  'Stopping',
  'Invalidating',
]);

const STOPPABLE_COLLECTOR_STATUSES = new Set([
  'Scheduled',
  'Starting',
  'Running',
  'Stopping',
]);

export function isPollableCollectorStatus(status: string | null | undefined): boolean {
  return status !== null
    && status !== undefined
    && POLLABLE_COLLECTOR_STATUSES.has(status);
}

export function isExclusiveCollectorStatus(status: string | null | undefined): boolean {
  return isPollableCollectorStatus(status);
}

export function isStoppableCollectorStatus(status: string | null | undefined): boolean {
  return status !== null
    && status !== undefined
    && STOPPABLE_COLLECTOR_STATUSES.has(status);
}

export function getCollectorStatusLabel(status: string): string {
  switch (status) {
    case 'Scheduled':
    case 'Starting':
    case 'Running':
    case 'Stopping':
    case 'Invalidating':
    case 'Stopped':
    case 'Failed':
    case 'Interrupted':
      return status;
    default:
      return 'Unknown';
  }
}

export function getCollectorPhaseLabel(phase: string | null): string {
  if (phase === null) {
    return '-';
  }

  switch (phase) {
    case 'WaitingForPreparation':
    case 'Connecting':
    case 'AwaitingInitialBooks':
    case 'AwaitingHeartbeat':
    case 'ReadyBeforeWindow':
    case 'CollectingWindow':
    case 'AwaitingResolution':
    case 'DrainingRaw':
    case 'AwaitingNormalization':
    case 'Cleaning':
      return phase;
    default:
      return 'Unknown';
  }
}
