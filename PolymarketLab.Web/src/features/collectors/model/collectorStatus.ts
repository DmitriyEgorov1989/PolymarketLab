export const ACTIVE_COLLECTOR_POLL_INTERVAL_MS = 2_000;

export function isActiveCollectorStatus(status: string | null | undefined): boolean {
  return status === 'Starting' || status === 'Running' || status === 'Stopping';
}

export function getCollectorStatusLabel(status: string): string {
  switch (status) {
    case 'Starting':
    case 'Running':
    case 'Stopping':
    case 'Stopped':
    case 'Failed':
    case 'Interrupted':
      return status;
    default:
      return 'Unknown';
  }
}
