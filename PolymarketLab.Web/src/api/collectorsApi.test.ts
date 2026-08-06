import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  getCollectorById,
  getCollectorByMarketId,
  startCollector,
  stopCollector,
} from './collectorsApi';

describe('collectorsApi', () => {
  const fetchMock = vi.fn<typeof fetch>();

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('uses canonical read endpoints', async () => {
    fetchMock
      .mockResolvedValueOnce(envelopeResponse({ session: {} }))
      .mockResolvedValueOnce(envelopeResponse({ session: null }));

    await getCollectorById('session/id');
    await getCollectorByMarketId('market/id');

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/Collector/session%2Fid');
    expect(fetchMock.mock.calls[1]?.[0]).toBe('/api/Collector/by-market/market%2Fid');
  });

  it('starts a collector with a market id', async () => {
    fetchMock.mockResolvedValue(envelopeResponse({
      sessionId: 'session-id',
      marketId: 'market-id',
      status: 'Running',
    }));

    await startCollector({ marketId: 'market-id' });

    expect(fetchMock).toHaveBeenCalledWith('/api/Collector', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ marketId: 'market-id' }),
    }));
  });

  it('stops a collector through the route without a body', async () => {
    fetchMock.mockResolvedValue(envelopeResponse({ session: {} }));

    await stopCollector('session/id');

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/Collector/session%2Fid/stop',
      expect.objectContaining({ method: 'POST', body: undefined }),
    );
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
