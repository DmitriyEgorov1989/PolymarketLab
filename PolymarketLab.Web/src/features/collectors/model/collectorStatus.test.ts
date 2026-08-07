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

  it('preserves known statuses and protects unknown values', () => {
    expect(getCollectorStatusLabel('Running')).toBe('Running');
    expect(getCollectorStatusLabel('Unexpected')).toBe('Unknown');
  });
});
