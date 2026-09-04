// @vitest-environment jsdom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { CollectorSession } from '../model/collectorSession';
import { createCollectorSession } from '../testing/createCollectorSession';
import { CollectorControls } from './CollectorControls';

describe('CollectorControls', () => {
  it('disables controls without a selected market', () => {
    renderControls({ marketId: null, isSessionResolved: false });

    expect(startButton().disabled).toBe(true);
    expect(stopButton().disabled).toBe(true);
  });

  it('starts when the selected market has no active session', () => {
    const onStart = vi.fn();
    renderControls({ session: null, onStart });

    expect(startButton().disabled).toBe(false);
    expect(stopButton().disabled).toBe(true);
    fireEvent.click(startButton());
    expect(onStart).toHaveBeenCalledOnce();
  });

  it.each(['Scheduled', 'Starting', 'Running', 'Stopping'] as const)(
    'allows Stop and blocks Start for %s',
    (status) => {
      const onStop = vi.fn();
      renderControls({ session: createCollectorSession({ status }), onStop });

      expect(startButton().disabled).toBe(true);
      expect(stopButton().disabled).toBe(false);
      fireEvent.click(stopButton());
      expect(onStop).toHaveBeenCalledOnce();
    },
  );

  it('blocks Stop and Start while Invalidating', () => {
    renderControls({ session: createCollectorSession({ status: 'Invalidating' }) });

    expect(startButton().disabled).toBe(true);
    expect(stopButton().disabled).toBe(true);
  });

  it('blocks Stop for an unknown status', () => {
    renderControls({
      session: createCollectorSession({ status: 'FutureStatus' as CollectorSession['status'] }),
    });

    expect(stopButton().disabled).toBe(true);
  });

  it.each(['Stopped', 'Failed', 'Interrupted'] as const)(
    'allows a new Start and blocks Stop for %s',
    (status) => {
      renderControls({ session: createCollectorSession({ status }) });

      expect(startButton().disabled).toBe(false);
      expect(stopButton().disabled).toBe(true);
    },
  );

  it('blocks both controls while a mutation is pending', () => {
    renderControls({
      session: createCollectorSession({ status: 'Running' }),
      isStopPending: true,
    });

    expect(startButton().disabled).toBe(true);
    expect(stopButton().disabled).toBe(true);
    expect(stopButton().textContent).toBe('Останавливаем...');
  });
});

interface RenderControlsOptions {
  marketId?: string | null;
  session?: CollectorSession | null;
  isSessionResolved?: boolean;
  isStartPending?: boolean;
  isStopPending?: boolean;
  isMutationPending?: boolean;
  onStart?: () => void;
  onStop?: () => void;
}

function renderControls(options: RenderControlsOptions = {}) {
  return render(
    <CollectorControls
      marketId={options.marketId === undefined ? 'market-id' : options.marketId}
      session={options.session}
      isSessionResolved={options.isSessionResolved ?? true}
      isStartPending={options.isStartPending ?? false}
      isStopPending={options.isStopPending ?? false}
      isMutationPending={options.isMutationPending
        ?? ((options.isStartPending ?? false) || (options.isStopPending ?? false))}
      isGlobalSlotResolved={true}
      isBlockedByOtherMarket={false}
      onStart={options.onStart ?? vi.fn()}
      onStop={options.onStop ?? vi.fn()}
    />,
  );
}

function startButton(): HTMLButtonElement {
  return screen.getByRole('button', { name: /Start collector|Запускаем/ }) as HTMLButtonElement;
}

function stopButton(): HTMLButtonElement {
  return screen.getByRole('button', { name: /Stop collector|Останавливаем/ }) as HTMLButtonElement;
}
