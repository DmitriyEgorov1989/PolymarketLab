import { describe, expect, it } from 'vitest';
import { getResponseErrorMessage, isEnvelope } from './envelope';

describe('isEnvelope', () => {
  it('accepts a successful envelope', () => {
    expect(isEnvelope({
      result: { value: 1 },
      listErrors: [],
      createdUtc: '2026-08-06T12:00:00Z',
    })).toBe(true);
  });

  it('accepts an error envelope', () => {
    expect(isEnvelope({
      result: null,
      listErrors: [
        {
          errorCode: 'request.validation',
          errorMessage: 'Value is required.',
          invalidField: 'marketId',
        },
      ],
      createdUtc: '2026-08-06T12:00:00Z',
    })).toBe(true);
  });

  it('rejects an invalid envelope shape', () => {
    expect(isEnvelope(null)).toBe(false);
    expect(isEnvelope({ result: {}, listErrors: [] })).toBe(false);
    expect(isEnvelope({
      result: {},
      listErrors: [{ errorCode: 'error' }],
      createdUtc: '2026-08-06T12:00:00Z',
    })).toBe(false);
  });
});

describe('getResponseErrorMessage', () => {
  it('uses message, then code, then fallback', () => {
    expect(getResponseErrorMessage([
      { errorCode: 'error.code', errorMessage: 'Backend message.', invalidField: null },
    ], 'Fallback.')).toBe('Backend message.');

    expect(getResponseErrorMessage([
      { errorCode: 'error.code', errorMessage: ' ', invalidField: null },
    ], 'Fallback.')).toBe('error.code');

    expect(getResponseErrorMessage([], 'Fallback.')).toBe('Fallback.');
  });
});
