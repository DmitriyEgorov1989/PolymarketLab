interface CollectorFailureProps {
  failureCode: string | null;
  failureMessage: string | null;
}

export function CollectorFailure({ failureCode, failureMessage }: CollectorFailureProps) {
  return (
    <section className="collector-failure" aria-labelledby="collector-failure-title">
      <h3 id="collector-failure-title">Ошибка сессии</h3>
      <dl>
        <div>
          <dt>Код</dt>
          <dd><code>{failureCode ?? '-'}</code></dd>
        </div>
        <div>
          <dt>Сообщение</dt>
          <dd>{failureMessage ?? '-'}</dd>
        </div>
      </dl>
    </section>
  );
}
