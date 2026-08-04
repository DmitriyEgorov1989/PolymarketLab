export interface ResponseError {
  errorCode: string | null;
  errorMessage: string | null;
  invalidField: string | null;
}

export interface Envelope<TResult> {
  result: TResult | null;
  listErrors: ResponseError[];
  createdOtc: string;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

export function isResponseError(value: unknown): value is ResponseError {
  if (!isObject(value)) {
    return false;
  }

  return (
    ('errorCode' in value &&
      (value.errorCode === null || typeof value.errorCode === 'string')) &&
    ('errorMessage' in value &&
      (value.errorMessage === null || typeof value.errorMessage === 'string')) &&
    ('invalidField' in value &&
      (value.invalidField === null || typeof value.invalidField === 'string'))
  );
}

export function isEnvelope<TResult>(value: unknown): value is Envelope<TResult> {
  if (!isObject(value)) {
    return false;
  }

  return (
    'result' in value &&
    'listErrors' in value &&
    Array.isArray(value.listErrors) &&
    value.listErrors.every(isResponseError) &&
    'createdOtc' in value &&
    typeof value.createdOtc === 'string'
  );
}

export function getResponseErrorMessage(
  errors: ResponseError[],
  fallbackMessage: string,
): string {
  for (const error of errors) {
    const message = error.errorMessage?.trim();
    if (message) {
      return message;
    }

    const code = error.errorCode?.trim();
    if (code) {
      return code;
    }
  }

  return fallbackMessage;
}
