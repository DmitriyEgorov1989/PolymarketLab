using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;

namespace PolymarketLab.DataCollection.Core.Application.Normalization;

/// <summary>Последовательно нормализует один захваченный пакет сообщений.</summary>
public sealed class NormalizationProcessor : INormalizationProcessor, IClaimedNormalizationBatchProcessor
{
    private const int MaximumLoggedEventTypeLength = 128;
    private static readonly NormalizationIssue ProcessingFailure = new(
        "normalization.processing.failed",
        "Normalization failed because of an unexpected technical error.");

    private readonly IRawMessageNormalizationClaimRepository claimRepository;
    private readonly IRawMessageDecoder decoder;
    private readonly INormalizationDispatcher dispatcher;
    private readonly INormalizedMessageWriter writer;
    private readonly int projectionVersion;
    private readonly int batchSize;
    private readonly TimeSpan claimTimeout;

    /// <summary>Создаёт ручной пакетный обработчик с явными параметрами захвата.</summary>
    public NormalizationProcessor(
        IRawMessageNormalizationClaimRepository claimRepository,
        IRawMessageDecoder decoder,
        INormalizationDispatcher dispatcher,
        INormalizedMessageWriter writer,
        int projectionVersion,
        int batchSize,
        TimeSpan claimTimeout)
    {
        this.claimRepository = claimRepository
            ?? throw new ArgumentNullException(nameof(claimRepository));
        this.decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectionVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        if (claimTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(claimTimeout));

        this.projectionVersion = projectionVersion;
        this.batchSize = batchSize;
        this.claimTimeout = claimTimeout;
    }

    /// <inheritdoc />
    public async Task<NormalizationBatchResult> ProcessBatchAsync(
        CancellationToken cancellationToken)
    {
        var claims = await claimRepository.ClaimBatchAsync(
            projectionVersion,
            batchSize,
            claimTimeout,
            cancellationToken);
        return await ProcessClaimsAsync(claims, cancellationToken);
    }

    public async Task<NormalizationBatchResult> ProcessClaimsAsync(
        IReadOnlyList<ClaimedRawMessage> claims,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claims);
        if (claims.Count == 0)
            return new NormalizationBatchResult(0, 0, 0, 0, 0, null, null);

        var processed = 0;
        var invalid = 0;
        var unsupported = 0;
        var failed = 0;
        var errors = new List<NormalizationMessageError>();

        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ProcessMessageAsync(claim, cancellationToken);
            if (result.Error is not null)
                errors.Add(result.Error);

            switch (result.Status)
            {
                case NormalizationStatus.Processed:
                    processed++;
                    break;
                case NormalizationStatus.Invalid:
                    invalid++;
                    break;
                case NormalizationStatus.Unsupported:
                    unsupported++;
                    break;
                case NormalizationStatus.Failed:
                    failed++;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected terminal status '{result.Status}'.");
            }
        }

        return new NormalizationBatchResult(
            claims.Count,
            processed,
            invalid,
            unsupported,
            failed,
            claims.Min(claim => claim.Message.RawMessageId),
            claims.Max(claim => claim.Message.RawMessageId),
            errors);
    }

    private async Task<MessageProcessingResult> ProcessMessageAsync(
        ClaimedRawMessage claim,
        CancellationToken cancellationToken)
    {
        try
        {
            var build = CreateCompletion(claim, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var writeStatus = await writer.WriteAsync(claim, build.Completion, cancellationToken);
            if (writeStatus == NormalizationWriteStatus.Written)
                return new MessageProcessingResult(build.Completion.Status, build.Error);

            var errorCode = writeStatus == NormalizationWriteStatus.ClaimLost
                ? "normalization.write.claim_lost"
                : "normalization.write.already_completed";
            return new MessageProcessingResult(
                NormalizationStatus.Failed,
                CreateError(claim, null, null, null, NormalizationStatus.Failed, errorCode));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await TryCompleteFailedAsync(claim, cancellationToken);
            return new MessageProcessingResult(
                NormalizationStatus.Failed,
                CreateError(
                    claim,
                    null,
                    null,
                    null,
                    NormalizationStatus.Failed,
                    ProcessingFailure.Code));
        }
    }

    private CompletionBuild CreateCompletion(
        ClaimedRawMessage claim,
        CancellationToken cancellationToken)
    {
        var decoded = decoder.Decode(claim.Message);
        if (!decoded.IsDecoded)
        {
            return new CompletionBuild(
                NormalizationCompletion.Invalid(decoded.Issue!),
                CreateError(
                    claim,
                    null,
                    null,
                    null,
                    NormalizationStatus.Invalid,
                    decoded.Issue!.Code));
        }

        var events = new List<NormalizedEvent>(decoded.Items.Count);
        (NormalizationIssue Issue, NormalizationMessageError Error)? firstInvalid = null;
        (NormalizationIssue Issue, NormalizationMessageError Error)? firstUnsupported = null;

        foreach (var item in decoded.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.IsDecoded)
            {
                firstInvalid ??= (
                    item.Issue!,
                    CreateError(
                        claim,
                        item.RawItemIndex,
                        null,
                        null,
                        NormalizationStatus.Invalid,
                        item.Issue!.Code));
                continue;
            }

            var message = claim.Message;
            var rawEvent = new LogicalRawEvent(
                message.RawMessageId,
                item.RawItemIndex,
                claim.ProjectionVersion,
                message.SessionId,
                message.ReceivedAt,
                item.Json!.Value);
            var result = dispatcher.Dispatch(rawEvent);
            var eventType = ReadEventType(item.Json.Value);
            switch (result.Outcome)
            {
                case NormalizationOutcome.Processed:
                    events.Add(result.Event!);
                    break;
                case NormalizationOutcome.Invalid:
                    firstInvalid ??= (
                        result.Issue!,
                        CreateError(
                            claim,
                            item.RawItemIndex,
                            eventType,
                            result.NormalizerVersion,
                            NormalizationStatus.Invalid,
                            result.Issue!.Code));
                    break;
                case NormalizationOutcome.Unsupported:
                    firstUnsupported ??= (
                        result.Issue!,
                        CreateError(
                            claim,
                            item.RawItemIndex,
                            eventType,
                            result.NormalizerVersion,
                            NormalizationStatus.Unsupported,
                            result.Issue!.Code));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected normalization outcome '{result.Outcome}'.");
            }
        }

        if (firstInvalid is not null)
        {
            return new CompletionBuild(
                NormalizationCompletion.Invalid(firstInvalid.Value.Issue),
                firstInvalid.Value.Error);
        }
        if (firstUnsupported is not null)
        {
            return new CompletionBuild(
                NormalizationCompletion.Unsupported(firstUnsupported.Value.Issue),
                firstUnsupported.Value.Error);
        }

        return new CompletionBuild(NormalizationCompletion.Processed(events), null);
    }

    private async Task TryCompleteFailedAsync(
        ClaimedRawMessage claim,
        CancellationToken cancellationToken)
    {
        try
        {
            await writer.WriteAsync(
                claim,
                NormalizationCompletion.Failed(ProcessingFailure),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Захват останется восстанавливаемым по timeout, а пакет продолжит обработку.
        }

    }

    private static NormalizationMessageError CreateError(
        ClaimedRawMessage claim,
        int? rawItemIndex,
        string? eventType,
        int? normalizerVersion,
        NormalizationStatus status,
        string errorCode) =>
        new(
            claim.Message.RawMessageId,
            claim.Message.SessionId,
            rawItemIndex,
            eventType,
            claim.ProjectionVersion,
            normalizerVersion,
            status,
            errorCode);

    private static string? ReadEventType(System.Text.Json.JsonElement json)
    {
        if (!json.TryGetProperty("event_type", out var property)
            || property.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            return null;
        }

        var eventType = property.GetString();
        return eventType is { Length: > MaximumLoggedEventTypeLength }
            ? eventType[..MaximumLoggedEventTypeLength]
            : eventType;
    }

    private sealed record CompletionBuild(
        NormalizationCompletion Completion,
        NormalizationMessageError? Error);

    private sealed record MessageProcessingResult(
        NormalizationStatus Status,
        NormalizationMessageError? Error);
}
