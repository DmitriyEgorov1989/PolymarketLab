export const MARKET_URI_REQUIRED_MESSAGE = 'Введите ссылку на событие Polymarket.';
export const MARKET_URI_INVALID_MESSAGE =
  'Введите HTTPS-ссылку вида https://polymarket.com/event/<slug>.';

export type MarketUriValidationResult =
  | { isValid: true; marketUri: string }
  | { isValid: false; message: string };

export function validateMarketUri(value: string): MarketUriValidationResult {
  const marketUri = value.trim();
  if (marketUri === '') {
    return { isValid: false, message: MARKET_URI_REQUIRED_MESSAGE };
  }

  let url: URL;
  try {
    url = new URL(marketUri);
  } catch {
    return { isValid: false, message: MARKET_URI_INVALID_MESSAGE };
  }

  if (url.protocol.toLowerCase() !== 'https:' || url.hostname.toLowerCase() !== 'polymarket.com') {
    return { isValid: false, message: MARKET_URI_INVALID_MESSAGE };
  }

  const segments = url.pathname.split('/');
  const eventIndex = segments.findIndex((segment) => segment === 'event');
  const slug = eventIndex < 0 ? undefined : segments[eventIndex + 1];
  if (eventIndex < 0 || slug === undefined || slug.trim() === '') {
    return { isValid: false, message: MARKET_URI_INVALID_MESSAGE };
  }

  return { isValid: true, marketUri };
}
