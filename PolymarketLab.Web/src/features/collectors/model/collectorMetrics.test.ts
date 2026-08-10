import { describe, expect, it } from 'vitest';
import { calculateUnpersisted } from './collectorMetrics';

describe('calculateUnpersisted', () => {
  it('returns the number of received messages not yet persisted', () => {
    expect(calculateUnpersisted(120, 118)).toBe(2);
  });

  it('returns zero when counters are equal', () => {
    expect(calculateUnpersisted(120, 120)).toBe(0);
  });

  it('exposes inconsistent counters as a negative value', () => {
    expect(calculateUnpersisted(118, 120)).toBe(-2);
  });
});
