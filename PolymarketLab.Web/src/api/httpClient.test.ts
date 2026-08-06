import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from './apiError';
import { request } from './httpClient';

describe('request', () => {
  const fetchMock = vi.fn<typeof fetch>();

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns the result from a successful envelope', async () => {
    fetchMock.mockResolvedValue(jsonResponse({
      result: { marketId: 'market-id' },
      listErrors: [],
      createdUtc: '2026-08-06T12:00:00Z',
    }));

    const result = await request<{ marketId: string }>({
      method: 'GET',
      path: '/api/Market/market-id',
    });

    expect(result).toEqual({ marketId: 'market-id' });
    expect(fetchMock).toHaveBeenCalledWith('/api/Market/market-id', expect.objectContaining({
      method: 'GET',
    }));
  });

  it('extracts Problem Details detail and validation errors', async () => {
    fetchMock.mockResolvedValue(jsonResponse({
      title: 'Validation failed',
      detail: 'The request is invalid.',
      status: 400,
      errors: {
        marketId: ['Market id is required.'],
      },
    }, 400, 'application/problem+json'));

    const error = await captureApiError(request({
      method: 'POST',
      path: '/api/Collector',
      body: { marketId: '' },
    }));

    expect(error.message).toBe('The request is invalid.');
    expect(error.title).toBe('Validation failed');
    expect(error.detail).toBe('The request is invalid.');
    expect(error.errors).toEqual([
      {
        errorCode: 'request.validation',
        errorMessage: 'Market id is required.',
        invalidField: 'marketId',
      },
    ]);
    expect(error.status).toBe(400);
  });

  it('extracts an error Envelope and preserves status', async () => {
    fetchMock.mockResolvedValue(jsonResponse({
      result: null,
      listErrors: [
        {
          errorCode: 'market.query.not_found',
          errorMessage: 'Market was not found.',
          invalidField: 'marketId',
        },
      ],
      createdUtc: '2026-08-06T12:00:00Z',
    }, 404));

    const error = await captureApiError(request({ method: 'GET', path: '/api/Market/id' }));

    expect(error.message).toBe('Market was not found.');
    expect(error.status).toBe(404);
    expect(error.errors[0]?.invalidField).toBe('marketId');
  });

  it('uses Problem Details validation message before title', async () => {
    fetchMock.mockResolvedValue(jsonResponse({
      title: 'Validation failed',
      errors: { marketId: ['Market id is required.'] },
    }, 400));

    const error = await captureApiError(request({ method: 'GET', path: '/api/Market' }));

    expect(error.message).toBe('Market id is required.');
  });

  it('uses Problem Details title when detail and validation errors are absent', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ title: 'Resource not found' }, 404));

    const error = await captureApiError(request({ method: 'GET', path: '/api/Market/id' }));

    expect(error.message).toBe('Resource not found');
    expect(error.status).toBe(404);
  });

  it('uses a safe plain text error', async () => {
    fetchMock.mockResolvedValue(new Response('Service unavailable.', {
      status: 503,
      headers: { 'Content-Type': 'text/plain' },
    }));

    const error = await captureApiError(request({ method: 'GET', path: '/api/Market' }));

    expect(error.message).toBe('Service unavailable.');
    expect(error.status).toBe(503);
  });

  it('handles an empty error response and preserves status', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 404 }));

    const error = await captureApiError(request({ method: 'GET', path: '/api/Market/id' }));

    expect(error.message).toBe('Request failed with empty response body.');
    expect(error.status).toBe(404);
  });

  it('rejects an empty successful response', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 200 }));

    const error = await captureApiError(request({ method: 'GET', path: '/api/Market' }));

    expect(error.message).toBe('Request succeeded with empty response body.');
    expect(error.status).toBe(200);
  });

  it('rejects invalid JSON without exposing the raw payload', async () => {
    fetchMock.mockResolvedValue(new Response('{invalid', {
      status: 500,
      headers: { 'Content-Type': 'application/json' },
    }));

    const error = await captureApiError(request({ method: 'GET', path: '/api/Market' }));

    expect(error.message).toBe('Request failed with invalid JSON response.');
    expect(error.message).not.toContain('{invalid');
    expect(error.status).toBe(500);
  });

  it('does not expose an HTML error page', async () => {
    fetchMock.mockResolvedValue(new Response('<html><body>Proxy failed</body></html>', {
      status: 502,
      headers: { 'Content-Type': 'text/html' },
    }));

    const error = await captureApiError(request({ method: 'GET', path: '/api/Market' }));

    expect(error.message).toBe('Request failed with status 502.');
    expect(error.message).not.toContain('<html>');
  });

  it('does not stringify an unknown JSON object', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ nested: { error: true } }, 500));

    const error = await captureApiError(request({ method: 'GET', path: '/api/Market' }));

    expect(error.message).toBe('Request failed with status 500.');
    expect(error.message).not.toBe('[object Object]');
  });

  it('normalizes a network failure without an HTTP status', async () => {
    fetchMock.mockRejectedValue(new TypeError('fetch failed'));

    const error = await captureApiError(request({ method: 'GET', path: '/api/Market' }));

    expect(error.message).toBe('Unable to reach the API.');
    expect(error.status).toBeNull();
  });

  it('preserves AbortError', async () => {
    const abortError = new DOMException('Aborted', 'AbortError');
    fetchMock.mockRejectedValue(abortError);

    await expect(request({ method: 'GET', path: '/api/Market' })).rejects.toBe(abortError);
  });
});

function jsonResponse(
  body: unknown,
  status = 200,
  contentType = 'application/json',
): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': contentType },
  });
}

async function captureApiError(promise: Promise<unknown>): Promise<ApiError> {
  try {
    await promise;
  } catch (error: unknown) {
    expect(error).toBeInstanceOf(ApiError);
    return error as ApiError;
  }

  throw new Error('Expected request to reject.');
}
