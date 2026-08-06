import { useId, useState, type FormEvent } from 'react';
import { ApiError } from '../../../api/apiError';
import { useRegisterMarketMutation } from '../hooks/useRegisterMarketMutation';
import { validateMarketUri } from '../model/marketUrl';

function getBackendMarketUriError(error: ApiError | null): string | null {
  if (error === null) {
    return null;
  }

  const marketUriError = error.errors.find((item) => {
    const invalidField = item.invalidField?.trim().toLowerCase();
    const errorCode = item.errorCode?.trim().toLowerCase();

    return invalidField === 'marketuri'
      || invalidField?.endsWith('.marketuri') === true
      || errorCode?.startsWith('polymarket.url.') === true;
  });

  if (marketUriError === undefined) {
    return null;
  }

  return marketUriError.errorMessage?.trim()
    || marketUriError.errorCode?.trim()
    || error.message;
}

interface AddMarketFormProps {
  onMarketRegistered: (marketId: string) => void;
}

export function AddMarketForm({ onMarketRegistered }: AddMarketFormProps) {
  const inputId = useId();
  const errorId = useId();
  const successId = useId();
  const [marketUri, setMarketUri] = useState('');
  const [clientError, setClientError] = useState<string | null>(null);
  const mutation = useRegisterMarketMutation();
  const backendFieldError = getBackendMarketUriError(mutation.error);
  const fieldError = clientError ?? backendFieldError;
  const submissionError = mutation.error !== null && backendFieldError === null
    ? mutation.error.message
    : null;
  const errorMessage = fieldError ?? submissionError;
  const statusMessage = mutation.isSuccess
    ? mutation.data.created
      ? `Рынок добавлен. Market ID: ${mutation.data.marketId}`
      : `Рынок уже был зарегистрирован. Выбран существующий рынок. Market ID: ${mutation.data.marketId}`
    : null;

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const validationResult = validateMarketUri(marketUri);
    if (!validationResult.isValid) {
      mutation.reset();
      setClientError(validationResult.message);
      return;
    }

    setClientError(null);

    mutation.mutate(
      { marketUri: validationResult.marketUri },
      {
        onSuccess: (result) => {
          onMarketRegistered(result.marketId);
          setMarketUri('');
        },
      },
    );
  }

  return (
    <form
      className="add-market-form"
      onSubmit={handleSubmit}
      aria-busy={mutation.isPending}
      noValidate
    >
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
            onChange={(event) => {
              setMarketUri(event.target.value);
              setClientError(null);
              mutation.reset();
            }}
            aria-invalid={fieldError !== null}
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
