// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { getCollectors } from '../api/collectorsApi';
import { createCollectorSession } from '../features/collectors/testing/createCollectorSession';
import { CollectorDashboardPage } from './CollectorDashboardPage';

vi.mock('../api/collectorsApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/collectorsApi')>()),
  getCollectors: vi.fn(),
}));

describe('CollectorDashboardPage', () => {
  it('renders multiple automatic jobs without a Start control', async () => {
    vi.mocked(getCollectors).mockResolvedValue({
      sessions: [createCollectorSession({ marketId: 'market-a' }), createCollectorSession({ marketId: 'market-b' })],
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
    render(<QueryClientProvider client={queryClient}><CollectorDashboardPage /></QueryClientProvider>);
    expect(await screen.findAllByText('Running')).toHaveLength(2);
    expect(screen.queryByRole('button', { name: /Start collector/i })).toBeNull();
  });
});
