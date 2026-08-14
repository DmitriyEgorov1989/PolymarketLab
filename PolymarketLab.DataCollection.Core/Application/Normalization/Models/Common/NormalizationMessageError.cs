using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Безопасный контекст ошибки одного исходного сообщения.</summary>
public sealed record NormalizationMessageError(
    long RawMessageId,
    CollectorSessionId SessionId,
    int? RawItemIndex,
    string? EventType,
    int ProjectionVersion,
    int? NormalizerVersion,
    NormalizationStatus Status,
    string ErrorCode);
