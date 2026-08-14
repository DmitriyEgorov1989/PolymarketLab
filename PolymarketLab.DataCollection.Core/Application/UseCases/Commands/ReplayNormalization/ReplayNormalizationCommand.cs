using CSharpFunctionalExtensions;
using MediatR;
using static PolymarketLab.SharedKernel.Errors.Error;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.ReplayNormalization;

public sealed record ReplayNormalizationCommand(
    int SourceProjectionVersion,
    int TargetProjectionVersion,
    Guid? SessionId,
    string? EventType)
    : IRequest<Result<ReplayNormalizationResponse, ErrorList>>;
