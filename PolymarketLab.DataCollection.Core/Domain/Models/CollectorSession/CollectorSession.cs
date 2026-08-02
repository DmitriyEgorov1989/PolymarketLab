using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.Errors;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.SharedKernel.DomainModels;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession
{
    public sealed class CollectorSession : Aggregate<CollectorSessionId>
    {
        private CollectorSession()
        {
        }

        private CollectorSession(
            CollectorSessionId id,
            MarketId marketId,
            DateTimeOffset createdAt) : base(id)
        {
            MarketId = marketId;
            CreatedAt = createdAt;
            Status = CollectorSessionStatus.Starting;
        }

        /// <summary>Идентификатор рынка, для которого запущен сбор данных.</summary>
        public MarketId MarketId { get; private set; } = null!;

        /// <summary>Текущее состояние сессии сбора данных.</summary>
        public CollectorSessionStatus Status { get; private set; }

        /// <summary>Дата и время создания сессии.</summary>
        public DateTimeOffset CreatedAt { get; private set; }

        /// <summary>Дата и время фактического начала сбора данных.</summary>
        public DateTimeOffset? StartedAt { get; private set; }

        /// <summary>Дата и время завершения сессии.</summary>
        public DateTimeOffset? StoppedAt { get; private set; }

        /// <summary>Причина остановки или неуспешного завершения сессии.</summary>
        public CollectorStopReason? StopReason { get; private set; }

        /// <summary>Машиночитаемый код ошибки при неуспешном завершении.</summary>
        public string? FailureCode { get; private set; }

        /// <summary>Описание ошибки при неуспешном завершении.</summary>
        public string? FailureMessage { get; private set; }

        public static Result<CollectorSession, Error> Create(
            CollectorSessionId id,
            MarketId marketId,
            DateTimeOffset createdAt)
        {
            if (createdAt == default)
                return CollectorSessionErrors.InvalidCreatedAt;

            return new CollectorSession(id, marketId, createdAt);
        }

        public UnitResult<Error> MarkRunning(DateTimeOffset startedAt)
        {
            if (Status != CollectorSessionStatus.Starting)
            {
                return UnitResult.Failure(
                    CollectorSessionErrors.InvalidTransition(
                        Status,
                        CollectorSessionStatus.Running));
            }

            if (startedAt < CreatedAt)
                return UnitResult.Failure(CollectorSessionErrors.InvalidStartedAt);

            Status = CollectorSessionStatus.Running;
            StartedAt = startedAt;

            return UnitResult.Success<Error>();
        }

        public UnitResult<Error> Stop(
            DateTimeOffset stoppedAt,
            CollectorStopReason reason)
        {
            if (Status is not CollectorSessionStatus.Starting
                and not CollectorSessionStatus.Running
                and not CollectorSessionStatus.Stopping)
            {
                return UnitResult.Failure(CollectorSessionErrors.NotActive);
            }

            var lowerBound = StartedAt ?? CreatedAt;

            if (stoppedAt < lowerBound)
                return UnitResult.Failure(CollectorSessionErrors.InvalidStoppedAt);

            Status = CollectorSessionStatus.Stopped;
            StoppedAt = stoppedAt;
            StopReason = reason;
            FailureCode = null;
            FailureMessage = null;

            return UnitResult.Success<Error>();
        }

        public UnitResult<Error> MarkStopping()
        {
            if (Status is not CollectorSessionStatus.Starting
                and not CollectorSessionStatus.Running)
            {
                return UnitResult.Failure(
                    CollectorSessionErrors.InvalidTransition(
                        Status,
                        CollectorSessionStatus.Stopping));
            }

            Status = CollectorSessionStatus.Stopping;

            return UnitResult.Success<Error>();
        }

        public UnitResult<Error> Interrupt(
            DateTimeOffset interruptedAt,
            CollectorStopReason reason)
        {
            if (Status is not CollectorSessionStatus.Starting
                and not CollectorSessionStatus.Running
                and not CollectorSessionStatus.Stopping)
            {
                return UnitResult.Failure(CollectorSessionErrors.NotActive);
            }

            var lowerBound = StartedAt ?? CreatedAt;
            if (interruptedAt < lowerBound)
                return UnitResult.Failure(CollectorSessionErrors.InvalidStoppedAt);

            Status = CollectorSessionStatus.Interrupted;
            StoppedAt = interruptedAt;
            StopReason = reason;
            FailureCode = null;
            FailureMessage = null;

            return UnitResult.Success<Error>();
        }

        public UnitResult<Error> Fail(
            DateTimeOffset failedAt,
            CollectorStopReason reason,
            string failureCode,
            string failureMessage)
        {
            if (Status is CollectorSessionStatus.Stopped
                or CollectorSessionStatus.Failed
                or CollectorSessionStatus.Interrupted)
            {
                return UnitResult.Failure(CollectorSessionErrors.NotActive);
            }

            if (string.IsNullOrWhiteSpace(failureCode))
            {
                return UnitResult.Failure(
                    GeneralErrors.ValueIsRequired(nameof(failureCode)));
            }

            if (string.IsNullOrWhiteSpace(failureMessage))
            {
                return UnitResult.Failure(
                    GeneralErrors.ValueIsRequired(nameof(failureMessage)));
            }

            var lowerBound = StartedAt ?? CreatedAt;

            if (failedAt < lowerBound)
                return UnitResult.Failure(CollectorSessionErrors.InvalidStoppedAt);

            Status = CollectorSessionStatus.Failed;
            StoppedAt = failedAt;
            StopReason = reason;
            FailureCode = failureCode;
            FailureMessage = failureMessage;

            return UnitResult.Success<Error>();
        }
    }
}
