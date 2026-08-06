import {
  ApiError,
  createHttpApiError,
  createNetworkApiError,
  type ResponseBody,
} from './apiError';
import { getResponseErrorMessage, isEnvelope, type Envelope } from './envelope';

interface RequestOptions {
  method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  path: string;
  body?: unknown;
  signal?: AbortSignal;
}

export async function request<TResult>({
  method,
  path,
  body,
  signal,
}: RequestOptions): Promise<TResult> {
  assertApiPath(path);

  let response: Response;
  try {
    response = await fetch(path, {
      method,
      headers: {
        Accept: 'application/json',
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
    });
  } catch (error: unknown) {
    if (isAbortError(error)) {
      throw error;
    }

    throw createNetworkApiError(error);
  }

  let responseText: string;
  try {
    responseText = await response.text();
  } catch (error: unknown) {
    throw new ApiError('Unable to read the API response.', response.status, { cause: error });
  }

  const responseBody = parseResponseBody(
    responseText,
    response.headers.get('content-type'),
  );

  if (!response.ok) {
    throw createHttpApiError(response.status, responseBody);
  }

  return getSuccessfulResult<TResult>(response.status, responseBody);
}

function getSuccessfulResult<TResult>(status: number, responseBody: ResponseBody): TResult {
  if (responseBody.kind === 'empty') {
    throw new ApiError('Request succeeded with empty response body.', status);
  }

  if (responseBody.kind === 'invalid-json') {
    throw new ApiError('Request succeeded with invalid JSON response.', status, {
      body: responseBody.rawText,
    });
  }

  if (responseBody.kind === 'text' || !isEnvelope<TResult>(responseBody.value)) {
    throw new ApiError('Request succeeded with invalid response envelope.', status, {
      body: responseBody.kind === 'text' ? responseBody.rawText : responseBody.value,
    });
  }

  const envelope = responseBody.value;
  if (envelope.listErrors.length > 0) {
    throw new ApiError(
      getResponseErrorMessage(envelope.listErrors, `Request failed with status ${status}.`),
      status,
      { errors: envelope.listErrors, body: envelope },
    );
  }

  return getEnvelopeResult(envelope, status);
}

function parseResponseBody(text: string, contentType: string | null): ResponseBody {
  const trimmedText = text.trim();
  if (trimmedText === '') {
    return { kind: 'empty' };
  }

  try {
    return { kind: 'json', value: JSON.parse(trimmedText) as unknown };
  } catch {
    if (contentType?.toLowerCase().includes('json')) {
      return { kind: 'invalid-json', rawText: text };
    }

    return { kind: 'text', rawText: text };
  }
}

function getEnvelopeResult<TResult>(envelope: Envelope<TResult>, status: number): TResult {
  if (envelope.result === null) {
    throw new ApiError('Request succeeded without result payload.', status, { body: envelope });
  }

  return envelope.result;
}

function assertApiPath(path: string): void {
  if (!path.startsWith('/api/')) {
    throw new Error(`API path must start with '/api/': '${path}'.`);
  }
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}
