import { describe, expect, it } from 'vitest';
import { formatCounter } from './formatCounter';

describe('formatCounter', () => {
  it('formats counters with the local number format', () => {
    expect(formatCounter(1234567)).toBe(new Intl.NumberFormat().format(1234567));
    expect(formatCounter(0)).toBe('0');
  });

  it.each([null, undefined, Number.NaN, Number.POSITIVE_INFINITY])(
    'renders %s as a dash',
    (value) => {
      expect(formatCounter(value)).toBe('-');
    },
  );
});
