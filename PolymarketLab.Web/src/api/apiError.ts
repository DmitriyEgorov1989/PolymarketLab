import { getResponseErrorMessage, isEnvelope, type ResponseError } from './envelope';

export type ResponseBody =
  | { kind: 'empty' }
  | { kind: 'json'; value: unknown }
  | { kind: 'invalid-json'; rawText: string }
  | { kind: 'text'; rawText: string };

export class ApiError extends Error {
  readonly status: number | null;
  readonly errors: ResponseError[];
  readonly title?: string;
  readonly detail?: string;
  readonly body?: unknown;

  constructor(
    message: string,
    status: number | null,
    options: {
      errors?: ResponseError[];
      title?: string;
      detail?: string;
      body?: unknown;
      cause?: unknown;
    } = {},
  ) {
    super(message, { cause: options.cause });
    this.name = 'ApiError';
    this.status = status;
    this.errors = options.errors ?? [];
    this.title = options.title;
    this.detail = options.detail;
    this.body = options.body;
  }
}

export function createHttpApiError(status: number, responseBody: ResponseBody): ApiError {
  const fallbackMessage = `Request failed with status ${status}.`;

  if (responseBody.kind === 'empty') {
    return new ApiError('Request failed with empty response body.', status);
  }

  if (responseBody.kind === 'invalid-json') {
    return new ApiError('Request failed with invalid JSON response.', status, {
      body: responseBody.rawText,
    });
  }

  if (responseBody.kind === 'text') {
    return new ApiError(getSafeTextMessage(responseBody.rawText) ?? fallbackMessage, status, {
      body: responseBody.rawText,
    });
  }

  const body = responseBody.value;
  if (isEnvelope(body)) {
    return new ApiError(
      getResponseErrorMessage(body.listErrors, fallbackMessage),
      status,
      { errors: body.listErrors, body },
    );
  }

  const problemDetails = parseProblemDetails(body);
  if (problemDetails !== null) {
    const firstValidationMessage = problemDetails.errors
      .map((error) => error.errorMessage?.trim())
      .find((message): message is string => Boolean(message));
    const message = problemDetails.detail
      ?? firstValidationMessage
      ?? problemDetails.title
      ?? fallbackMessage;

    return new ApiError(message, status, {
      errors: problemDetails.errors,
      title: problemDetails.title,
      detail: problemDetails.detail,
      body,
    });
  }

  if (typeof body === 'string') {
    return new ApiError(getSafeTextMessage(body) ?? fallbackMessage, status, { body });
  }

  return new ApiError(fallbackMessage, status, { body });
}

export function createNetworkApiError(cause: unknown): ApiError {
  return new ApiError('Unable to reach the API.', null, { cause });
}

function parseProblemDetails(value: unknown): {
  title?: string;
  detail?: string;
  errors: ResponseError[];
} | null {
  if (!isObject(value)) {
    return null;
  }

  const title = getNonEmptyString(value.title);
  const detail = getNonEmptyString(value.detail);
  const errors = parseValidationErrors(value.errors);

  if (title === undefined && detail === undefined && errors.length === 0) {
    return null;
  }

  return { title, detail, errors };
}

function parseValidationErrors(value: unknown): ResponseError[] {
  if (!isObject(value)) {
    return [];
  }

  return Object.entries(value).flatMap(([field, messages]) => {
    if (!Array.isArray(messages)) {
      return [];
    }

    return messages
      .filter((message): message is string => typeof message === 'string')
      .map((message) => ({
        errorCode: 'request.validation',
        errorMessage: message,
        invalidField: field,
      }));
  });
}

function getSafeTextMessage(value: string): string | null {
  const text = value.trim();
  if (text === '' || /<\s*(?:!doctype|html|body)\b/i.test(text)) {
    return null;
  }

  return text.length <= 500 ? text : `${text.slice(0, 497)}...`;
}

function getNonEmptyString(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() !== '' ? value.trim() : undefined;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
