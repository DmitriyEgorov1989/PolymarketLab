import { describe, expect, it } from 'vitest';
import {
  MARKET_URI_INVALID_MESSAGE,
  MARKET_URI_REQUIRED_MESSAGE,
  validateMarketUri,
} from './marketUrl';

describe('validateMarketUri', () => {
  it.each([
    '',
    '   ',
  ])('rejects an empty value: %j', (value) => {
    expect(validateMarketUri(value)).toEqual({
      isValid: false,
      message: MARKET_URI_REQUIRED_MESSAGE,
    });
  });

  it.each([
    'not-a-url',
    'http://polymarket.com/event/example',
    'https://example.com/event/example',
    'https://www.polymarket.com/event/example',
    'https://polymarket.com/markets/example',
    'https://polymarket.com/Event/example',
    'https://polymarket.com/event/',
    'https://polymarket.com/event//other',
  ])('rejects a URL unsupported by backend: %s', (value) => {
    expect(validateMarketUri(value)).toEqual({
      isValid: false,
      message: MARKET_URI_INVALID_MESSAGE,
    });
  });

  it.each([
    'https://polymarket.com/event/example',
    'https://polymarket.com/ru/event/example',
    'https://polymarket.com/event/example?source=test#details',
    'https://polymarket.com/event/example/',
    'https://polymarket.com/prefix/event/example/extra',
  ])('accepts a URL supported by backend: %s', (marketUri) => {
    expect(validateMarketUri(marketUri)).toEqual({ isValid: true, marketUri });
  });

  it('returns the trimmed original URL', () => {
    expect(validateMarketUri('  https://polymarket.com/event/example?source=test  ')).toEqual({
      isValid: true,
      marketUri: 'https://polymarket.com/event/example?source=test',
    });
  });
});
