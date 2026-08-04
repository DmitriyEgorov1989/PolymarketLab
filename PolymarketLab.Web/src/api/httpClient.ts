import {
  getResponseErrorMessage,
  isEnvelope,
  type Envelope,
  type ResponseError,
} from './envelope';

const apiBaseUrl = getApiBaseUrl();

export class ApiError extends Error {
  readonly status: number;
  readonly errors: ResponseError[];
  readonly body?: unknown;

  constructor(
    message: string,
    status: number,
    errors: ResponseError[] = [],
    body?: unknown,
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.errors = errors;
    this.body = body;
  }
}

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
  const response = await fetch(buildUrl(path), {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });

  const responseText = await response.text();
  const parsedBody = parseJsonSafely(responseText);

  if (!response.ok) {
    throw buildApiError(response.status, parsedBody, responseText);
  }

  if (!isEnvelope<TResult>(parsedBody)) {
    const message = responseText.trim()
      ? 'Request succeeded with invalid JSON envelope.'
      : 'Request succeeded with empty response body.';

    throw new ApiError(message, response.status, [], parsedBody);
  }

  if (parsedBody.listErrors.length > 0) {
    throw new ApiError(
      getResponseErrorMessage(
        parsedBody.listErrors,
        `Request failed with status ${response.status}.`,
      ),
      response.status,
      parsedBody.listErrors,
      parsedBody,
    );
  }

  return getEnvelopeResult(parsedBody, response.status);
}

function getApiBaseUrl(): string {
  const value = import.meta.env.VITE_API_BASE_URL;

  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error('VITE_API_BASE_URL is not configured.');
  }

  return value.trim().replace(/\/$/, '');
}

function buildUrl(path: string): string {
  if (path.startsWith('http://') || path.startsWith('https://')) {
    return path;
  }

  return `${apiBaseUrl}${path.startsWith('/') ? path : `/${path}`}`;
}

function parseJsonSafely(text: string): unknown {
  const trimmedText = text.trim();

  if (trimmedText === '') {
    return null;
  }

  try {
    return JSON.parse(trimmedText) as unknown;
  } catch {
    return null;
  }
}

function buildApiError(status: number, body: unknown, responseText: string): ApiError {
  if (isEnvelope(body)) {
    return new ApiError(
      getResponseErrorMessage(body.listErrors, `Request failed with status ${status}.`),
      status,
      body.listErrors,
      body,
    );
  }

  if (responseText.trim() === '') {
    return new ApiError('Request failed with empty response body.', status, [], body);
  }

  if (body === null) {
    return new ApiError('Request failed with invalid JSON response.', status, [], responseText);
  }

  return new ApiError(`Request failed with status ${status}.`, status, [], body);
}

function getEnvelopeResult<TResult>(
  envelope: Envelope<TResult>,
  status: number,
): TResult {
  if (envelope.result === null) {
    throw new ApiError('Request succeeded without result payload.', status, [], envelope);
  }

  return envelope.result;
}
