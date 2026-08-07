import { describe, expect, it } from 'vitest';
import { formatLocalDate } from './formatLocalDate';

describe('formatLocalDate', () => {
  it('formats an ISO date in the local time zone', () => {
    const value = '2026-08-01T10:00:00Z';
    const expected = new Intl.DateTimeFormat(undefined, {
      dateStyle: 'medium',
      timeStyle: 'medium',
    }).format(new Date(value));

    expect(formatLocalDate(value)).toBe(expected);
  });

  it('renders null as a dash', () => {
    expect(formatLocalDate(null)).toBe('-');
  });

  it('renders an invalid date as a dash', () => {
    expect(formatLocalDate('not-an-iso-date')).toBe('-');
  });
});
