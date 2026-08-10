// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { PropsWithChildren } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/apiError';
import { registerMarket, type RegisterMarketResponse } from '../../../api/marketsApi';
import { MARKET_URI_REQUIRED_MESSAGE } from '../model/marketUrl';
import { AddMarketForm } from './AddMarketForm';

vi.mock('../../../api/marketsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/marketsApi')>();

  return {
    ...actual,
    registerMarket: vi.fn(),
  };
});

const registerMarketMock = vi.mocked(registerMarket);

describe('AddMarketForm', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('rejects an empty value without calling backend', () => {
    renderForm();

    fireEvent.click(screen.getByRole('button', { name: 'Добавить рынок' }));

    const input = screen.getByLabelText('Polymarket URL');
    expect(screen.getByRole('alert').textContent).toBe(MARKET_URI_REQUIRED_MESSAGE);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(registerMarketMock).not.toHaveBeenCalled();
  });

  it('rejects an invalid Polymarket URL and keeps the input', () => {
    renderForm();
    const input = screen.getByLabelText('Polymarket URL') as HTMLInputElement;

    fireEvent.change(input, { target: { value: 'https://example.com/event/example' } });
    fireEvent.submit(input.closest('form')!);

    expect(screen.getByRole('alert').textContent).toContain('https://polymarket.com/event/<slug>');
    expect(input.value).toBe('https://example.com/event/example');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(registerMarketMock).not.toHaveBeenCalled();
  });

  it('trims the URL, selects a newly created market and clears the input', async () => {
    registerMarketMock.mockResolvedValue({ marketId: 'registered-id', created: true });
    renderForm();
    const input = screen.getByLabelText('Polymarket URL') as HTMLInputElement;

    fireEvent.change(input, {
      target: { value: '  https://polymarket.com/event/example  ' },
    });
    fireEvent.submit(input.closest('form')!);

    await waitFor(() => expect(input.value).toBe(''));
    expect(registerMarketMock).toHaveBeenCalledWith({
      marketUri: 'https://polymarket.com/event/example',
    });
    expect(input.value).toBe('');
    expect(screen.getByRole('status').textContent).toBe(
      'Рынок добавлен. Market ID: registered-id',
    );
  });

  it('reports an already registered market without selecting it directly', async () => {
    registerMarketMock.mockResolvedValue({ marketId: 'existing-id', created: false });
    renderForm();
    submitValue('https://polymarket.com/event/existing');

    await screen.findByRole('status');
    expect(screen.getByRole('status').textContent).toBe(
      'Рынок уже был зарегистрирован. Market ID: existing-id',
    );
  });

  it('keeps the input and shows the exact backend URL error', async () => {
    registerMarketMock.mockRejectedValue(new ApiError('URL is invalid.', 400, {
      errors: [
        {
          errorCode: 'polymarket.url.invalid',
          errorMessage: 'URL is invalid.',
          invalidField: null,
        },
      ],
    }));
    renderForm();
    const input = screen.getByLabelText('Polymarket URL') as HTMLInputElement;
    const rawValue = 'https://polymarket.com/event/example';

    fireEvent.change(input, { target: { value: rawValue } });
    fireEvent.submit(input.closest('form')!);

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toBe('URL is invalid.');
    expect(input.value).toBe(rawValue);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe(alert.id);
  });

  it('shows an operation error without marking the URL invalid', async () => {
    registerMarketMock.mockRejectedValue(new ApiError('Gamma API is unavailable.', 500));
    renderForm();
    const input = screen.getByLabelText('Polymarket URL') as HTMLInputElement;

    submitValue('https://polymarket.com/event/example');

    expect((await screen.findByRole('alert')).textContent).toBe('Gamma API is unavailable.');
    expect(input.getAttribute('aria-invalid')).toBe('false');
    expect(input.value).toBe('https://polymarket.com/event/example');
  });

  it('disables the form while pending and clears only after success', async () => {
    const registration = deferred<RegisterMarketResponse>();
    registerMarketMock.mockReturnValue(registration.promise);
    renderForm();
    const input = screen.getByLabelText('Polymarket URL') as HTMLInputElement;
    const submit = screen.getByRole('button', { name: 'Добавить рынок' }) as HTMLButtonElement;

    fireEvent.change(input, { target: { value: 'https://polymarket.com/event/example' } });
    fireEvent.click(submit);

    await waitFor(() => expect(input.disabled).toBe(true));
    expect(submit.disabled).toBe(true);
    expect(input.value).toBe('https://polymarket.com/event/example');
    expect(input.closest('form')?.getAttribute('aria-busy')).toBe('true');

    await act(async () => {
      registration.resolve({ marketId: 'registered-id', created: true });
      await registration.promise;
    });

    await waitFor(() => expect(input.value).toBe(''));
    expect(input.disabled).toBe(false);
  });
});

function submitValue(value: string): void {
  const input = screen.getByLabelText('Polymarket URL');
  fireEvent.change(input, { target: { value } });
  fireEvent.submit(input.closest('form')!);
}

function renderForm() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(<AddMarketForm />, {
    wrapper: function Wrapper({ children }: PropsWithChildren) {
      return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    },
  });
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((promiseResolve) => {
    resolve = promiseResolve;
  });

  return { promise, resolve };
}
