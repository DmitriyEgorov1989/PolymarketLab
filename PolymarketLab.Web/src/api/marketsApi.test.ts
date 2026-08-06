import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getMarketById, getMarkets, registerMarket } from './marketsApi';

describe('marketsApi', () => {
  const fetchMock = vi.fn<typeof fetch>();

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('uses canonical read endpoints', async () => {
    fetchMock
      .mockResolvedValueOnce(envelopeResponse({ markets: [] }))
      .mockResolvedValueOnce(envelopeResponse({ market: { marketId: 'id' } }));

    await getMarkets();
    await getMarketById('market/id');

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/Market');
    expect(fetchMock.mock.calls[1]?.[0]).toBe('/api/Market/market%2Fid');
  });

  it('registers a market through POST /api/Market', async () => {
    fetchMock.mockResolvedValue(envelopeResponse({ marketId: 'id', created: true }));

    await registerMarket({ marketUri: 'https://polymarket.com/event/example' });

    expect(fetchMock).toHaveBeenCalledWith('/api/Market', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ marketUri: 'https://polymarket.com/event/example' }),
    }));
  });
});

function envelopeResponse(result: unknown): Response {
  return new Response(JSON.stringify({
    result,
    listErrors: [],
    createdUtc: '2026-08-06T12:00:00Z',
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}
