using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Application.Errors;

internal static class PolymarketUrlErrors
{
    public static Error Empty => new(
        "polymarket.url.empty",
        "URL is empty.",
        ErrorType.ValueIsRequired);

    public static Error Invalid => new(
        "polymarket.url.invalid",
        "URL is invalid.",
        ErrorType.ValueIsInvalid);

    public static Error HttpsRequired => new(
        "polymarket.url.https.required",
        "Only HTTPS URLs are supported.",
        ErrorType.ValueIsInvalid);

    public static Error InvalidHost => new(
        "polymarket.url.host.invalid",
        "URL must belong to polymarket.com.",
        ErrorType.ValueIsInvalid);

    public static Error EventSegmentMissing => new(
        "polymarket.url.event.missing",
        "URL does not contain an event segment.",
        ErrorType.ValueIsInvalid);

    public static Error SlugMissing => new(
        "polymarket.url.slug.missing",
        "Market slug is missing.",
        ErrorType.ValueIsRequired);
}
