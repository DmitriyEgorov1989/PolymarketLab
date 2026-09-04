import { describe, expect, it } from 'vitest';
import {
  getCollectorPhaseLabel,
  getCollectorStatusLabel,
  isExclusiveCollectorStatus,
  isPollableCollectorStatus,
  isStoppableCollectorStatus,
} from './collectorStatus';

describe('collector status', () => {
  const nonTerminalStatuses = [
    'Scheduled',
    'Starting',
    'Running',
    'Stopping',
    'Invalidating',
  ];
  const terminalAndUnknownStatuses = [
    'Stopped',
    'Failed',
    'Interrupted',
    'Unexpected',
    null,
    undefined,
  ];

  it.each(nonTerminalStatuses)('polls %s', (status) => {
    expect(isPollableCollectorStatus(status)).toBe(true);
  });

  it.each(terminalAndUnknownStatuses)(
    'does not poll %s',
    (status) => {
      expect(isPollableCollectorStatus(status)).toBe(false);
    },
  );

  it.each(nonTerminalStatuses)('reserves the exclusive slot for %s', (status) => {
    expect(isExclusiveCollectorStatus(status)).toBe(true);
  });

  it.each(terminalAndUnknownStatuses)(
    'does not reserve the exclusive slot for %s',
    (status) => {
      expect(isExclusiveCollectorStatus(status)).toBe(false);
    },
  );

  it.each(['Scheduled', 'Starting', 'Running', 'Stopping'])(
    'allows Stop for %s',
    (status) => {
      expect(isStoppableCollectorStatus(status)).toBe(true);
    },
  );

  it.each(['Invalidating', ...terminalAndUnknownStatuses])(
    'does not allow Stop for %s',
    (status) => {
      expect(isStoppableCollectorStatus(status)).toBe(false);
    },
  );

  it.each([
    'Scheduled',
    'Starting',
    'Running',
    'Stopping',
    'Invalidating',
    'Stopped',
    'Failed',
    'Interrupted',
  ])(
    'preserves the %s backend status',
    (status) => {
      expect(getCollectorStatusLabel(status)).toBe(status);
    },
  );

  it('protects unknown status values', () => {
    expect(getCollectorStatusLabel('Unexpected')).toBe('Unknown');
  });

  it.each([
    'WaitingForPreparation',
    'Connecting',
    'AwaitingInitialBooks',
    'AwaitingHeartbeat',
    'ReadyBeforeWindow',
    'CollectingWindow',
    'AwaitingResolution',
    'DrainingRaw',
    'AwaitingNormalization',
    'Cleaning',
  ])('preserves the %s backend phase', (phase) => {
    expect(getCollectorPhaseLabel(phase)).toBe(phase);
  });

  it('renders a terminal null phase as unavailable', () => {
    expect(getCollectorPhaseLabel(null)).toBe('-');
  });

  it('protects an unknown phase value', () => {
    expect(getCollectorPhaseLabel('Unexpected')).toBe('Unknown');
  });
});
