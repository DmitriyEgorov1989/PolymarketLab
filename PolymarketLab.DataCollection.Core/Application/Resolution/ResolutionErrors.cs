using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Resolution;

/// <summary>Ошибки проверки и согласования terminal resolution.</summary>
public static class ResolutionErrors
{
    /// <summary>Terminal observation противоречит snapshot или другому источнику.</summary>
    public static Error Conflict => new(
        "collector.resolution.conflict",
        "Resolution source reported terminal data incompatible with the collector session snapshot or another source.",
        ErrorType.Conflict);

    /// <summary>Полный consensus не был подтверждён до общего срока.</summary>
    public static Error ConfirmationTimeout => new(
        "collector.resolution.confirmation_timeout",
        "Resolution consensus was not confirmed before the deadline.",
        ErrorType.Failure);
}
