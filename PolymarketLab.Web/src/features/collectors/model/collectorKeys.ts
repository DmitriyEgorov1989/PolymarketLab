export const collectorKeys = {
  all: ['collectors'] as const,
  current: () => [...collectorKeys.all, 'current'] as const,
  details: () => [...collectorKeys.all, 'detail'] as const,
  detail: (sessionId: string) => [...collectorKeys.details(), sessionId] as const,
  byMarkets: () => [...collectorKeys.all, 'by-market'] as const,
  byMarket: (marketId: string) => [...collectorKeys.byMarkets(), marketId] as const,
};
