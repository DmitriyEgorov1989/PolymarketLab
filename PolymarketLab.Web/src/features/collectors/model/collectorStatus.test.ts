import { describe, expect, it } from 'vitest';
import { getCollectorStatusLabel, isActiveCollectorStatus } from './collectorStatus';

describe('collector status', () => {
  it.each(['Starting', 'Running', 'Stopping'])('treats %s as active', (status) => {
    expect(isActiveCollectorStatus(status)).toBe(true);
  });

  it.each(['Stopped', 'Failed', 'Interrupted', 'Unexpected', null, undefined])(
    'treats %s as inactive',
    (status) => {
      expect(isActiveCollectorStatus(status)).toBe(false);
    },
  );

  it.each(['Starting', 'Running', 'Stopping', 'Stopped', 'Failed', 'Interrupted'])(
    'preserves the %s backend status',
    (status) => {
      expect(getCollectorStatusLabel(status)).toBe(status);
    },
  );

  it('protects unknown status values', () => {
    expect(getCollectorStatusLabel('Unexpected')).toBe('Unknown');
  });
});
