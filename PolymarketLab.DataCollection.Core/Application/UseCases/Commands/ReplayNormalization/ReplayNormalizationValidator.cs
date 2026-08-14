using FluentValidation;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.SharedKernel.Extensions.Validations;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.ReplayNormalization;

public sealed class ReplayNormalizationValidator : AbstractValidator<ReplayNormalizationCommand>
{
    public ReplayNormalizationValidator()
    {
        RuleFor(command => command.SourceProjectionVersion)
            .GreaterThan(0)
            .WithError(ReplayNormalizationErrors.SourceProjectionVersionInvalid);
        RuleFor(command => command.TargetProjectionVersion)
            .GreaterThan(command => command.SourceProjectionVersion)
            .WithError(ReplayNormalizationErrors.TargetProjectionVersionInvalid);
        RuleFor(command => command.SessionId)
            .Must(sessionId => !sessionId.HasValue || sessionId.Value != Guid.Empty)
            .WithError(ReplayNormalizationErrors.SessionIdInvalid);
        RuleFor(command => command.EventType)
            .Must(eventType => eventType is null
                || (!string.IsNullOrWhiteSpace(eventType) && eventType.Length <= 128))
            .WithError(ReplayNormalizationErrors.EventTypeInvalid);
    }
}
