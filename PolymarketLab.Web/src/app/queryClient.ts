import { QueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/apiError';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => error instanceof ApiError
        && failureCount < 1
        && (error.status === null || error.status >= 500),
    },
  },
});
