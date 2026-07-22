using CSharpFunctionalExtensions;
using PolymarketLab.Markets.Core.Application.Errors;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Application.Extensions
{
    public static class PolymarketUrlExtensions
    {
        private const string PolymarketHost = "polymarket.com";
        private const string EventSegment = "event";

        public static Result<MarketSlug, Error> ParsePolymarketSlug(this string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return PolymarketUrlErrors.Empty;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return PolymarketUrlErrors.Invalid;

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return PolymarketUrlErrors.HttpsRequired;

            if (!string.Equals(uri.Host, PolymarketHost, StringComparison.OrdinalIgnoreCase))
                return PolymarketUrlErrors.InvalidHost;

            // AbsolutePath excludes the query string and preserves the URL path structure.
            var segments = uri.AbsolutePath.Split('/');
            var eventIndex = Array.FindIndex(
                segments,
                segment => string.Equals(segment, EventSegment, StringComparison.Ordinal));

            if (eventIndex < 0)
                return PolymarketUrlErrors.EventSegmentMissing;

            if (eventIndex + 1 >= segments.Length || string.IsNullOrWhiteSpace(segments[eventIndex + 1]))
                return PolymarketUrlErrors.SlugMissing;

            return MarketSlug.Create(segments[eventIndex + 1]);
        }
    }
}
