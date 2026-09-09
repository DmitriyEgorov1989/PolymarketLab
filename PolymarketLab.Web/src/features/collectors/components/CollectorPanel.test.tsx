// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { stopCollector } from '../../../api/collectorsApi';
import { createCollectorSession } from '../testing/createCollectorSession';
import { CollectorPanel } from './CollectorPanel';

vi.mock('../../../api/collectorsApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/collectorsApi')>()),
  stopCollector: vi.fn(),
}));

describe('CollectorPanel', () => {
  it('shows the server session and sends cancellation for a running job', async () => {
    const session = createCollectorSession({ status: 'Running' });
    vi.mocked(stopCollector).mockResolvedValue({ session: { ...session, status: 'Invalidating', phase: 'Cleaning' } });
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderPanel(session);

    expect(await screen.findByText('Running')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Отменить задание' }));
    await waitFor(() => expect(stopCollector).toHaveBeenCalledWith(session.sessionId));
  });
});

function renderPanel(session: ReturnType<typeof createCollectorSession>) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}><CollectorPanel session={session} /></QueryClientProvider>);
}
