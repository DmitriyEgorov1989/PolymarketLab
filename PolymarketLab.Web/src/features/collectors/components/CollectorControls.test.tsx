// @vitest-environment jsdom

import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { createCollectorSession } from '../testing/createCollectorSession';
import { CollectorControls } from './CollectorControls';

describe('CollectorControls', () => {
  it('allows cancellation only for cancellable server states', () => {
    const onStop = vi.fn();
    render(<CollectorControls session={createCollectorSession({ status: 'Running' })} isStopPending={false} onStop={onStop} />);
    expect((screen.getByRole('button', { name: 'Отменить задание' }) as HTMLButtonElement).disabled).toBe(false);
  });

  it('disables cancellation for stopping sessions', () => {
    render(<CollectorControls session={createCollectorSession({ status: 'Stopping' })} isStopPending={false} onStop={vi.fn()} />);
    expect((screen.getByRole('button', { name: 'Отменить задание' }) as HTMLButtonElement).disabled).toBe(true);
  });
});
