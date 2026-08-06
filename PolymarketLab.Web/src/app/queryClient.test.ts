import { describe, expect, it } from 'vitest';
import { ApiError } from '../api/apiError';
import { queryClient } from './queryClient';

describe('queryClient retry policy', () => {
  const retry = queryClient.getDefaultOptions().queries?.retry;

  it('does not retry client errors', () => {
    expectRetryFunction(retry);

    expect(retry(0, new ApiError('Not found.', 404))).toBe(false);
  });

  it('retries network and server errors once', () => {
    expectRetryFunction(retry);

    expect(retry(0, new ApiError('Network failed.', null))).toBe(true);
    expect(retry(0, new ApiError('Server failed.', 500))).toBe(true);
    expect(retry(1, new ApiError('Server failed.', 500))).toBe(false);
  });
});

function expectRetryFunction(
  retry: unknown,
): asserts retry is (failureCount: number, error: Error) => boolean {
  expect(retry).toBeTypeOf('function');
}
