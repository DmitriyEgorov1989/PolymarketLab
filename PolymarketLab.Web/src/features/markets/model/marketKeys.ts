export const marketKeys = {
  all: ['markets'] as const,
  list: () => [...marketKeys.all, 'list'] as const,
  details: () => [...marketKeys.all, 'detail'] as const,
  detail: (marketId: string) => [...marketKeys.details(), marketId] as const,
};
