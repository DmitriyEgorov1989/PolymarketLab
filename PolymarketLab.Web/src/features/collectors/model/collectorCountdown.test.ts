import { describe, expect, it } from 'vitest';
import { formatCollectorCountdown } from './collectorCountdown';

describe('formatCollectorCountdown', () => {
  const nowMs = Date.parse('2026-09-04T12:00:00Z');

  it.each([
    [null, '-'],
    ['not-a-date', '-'],
  ])('formats %s as %s', (deadline, expected) => {
    expect(formatCollectorCountdown(deadline, nowMs)).toBe(expected);
  });

  it.each([
    ['2026-09-04T12:01:05Z', '01:05'],
    ['2026-09-04T13:01:01Z', '01:01:01'],
    ['2026-09-04T12:00:00Z', '00:00'],
    ['2026-09-04T11:59:59Z', '00:00'],
  ])('formats deadline %s as %s', (deadline, expected) => {
    expect(formatCollectorCountdown(deadline, nowMs)).toBe(expected);
  });
});
