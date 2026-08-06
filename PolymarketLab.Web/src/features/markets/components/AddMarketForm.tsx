import { useId, useState, type FormEvent } from 'react';
import { ApiError } from '../../../api/apiError';
import { useRegisterMarketMutation } from '../hooks/useRegisterMarketMutation';

function getFormErrorMessage(error: ApiError | null): string | null {
  if (error === null) {
    return null;
  }

  const invalidMarketUriError = error.errors.find((item) => item.invalidField === 'marketUri');

  return invalidMarketUriError?.errorMessage ?? error.message;
}

export function AddMarketForm() {
  const inputId = useId();
  const errorId = useId();
  const successId = useId();
  const [marketUri, setMarketUri] = useState('');
  const mutation = useRegisterMarketMutation();
  const errorMessage = getFormErrorMessage(mutation.error ?? null);
  const statusMessage = mutation.isSuccess
    ? `Рынок зарегистрирован. Market ID: ${mutation.data.marketId}`
    : null;

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    mutation.mutate(
      { marketUri },
      {
        onSuccess: () => {
          setMarketUri('');
        },
      },
    );
  }

  return (
    <form className="add-market-form" onSubmit={handleSubmit} noValidate>
      <div className="form-copy">
        <p className="card-intro">
          Вставьте ссылку на событие Polymarket. Backend получит метаданные рынка и вернёт
          ` MarketId` для дальнейшего запуска collector session.
        </p>
      </div>

      <div className="form-controls">
        <label className="field-label" htmlFor={inputId}>
          Polymarket URL
        </label>

        <div className="field-row">
          <input
            id={inputId}
            className="url-input"
            name="marketUri"
            type="url"
            inputMode="url"
            autoComplete="off"
            placeholder="https://polymarket.com/event/..."
            value={marketUri}
            onChange={(event) => setMarketUri(event.target.value)}
            aria-invalid={errorMessage !== null}
            aria-describedby={errorMessage !== null ? errorId : undefined}
            disabled={mutation.isPending}
            required
          />

          <button className="primary-button" type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? 'Регистрируем...' : 'Добавить рынок'}
          </button>
        </div>

        {errorMessage !== null ? (
          <p id={errorId} className="form-message form-message-error" role="alert">
            {errorMessage}
          </p>
        ) : null}

        {statusMessage !== null ? (
          <p id={successId} className="form-message form-message-success" role="status">
            {statusMessage}
          </p>
        ) : null}
      </div>
    </form>
  );
}
