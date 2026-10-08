using Test.Models.Dto;

namespace Test.Services.Interfaces
{
    /// <summary>
    /// Core abstraction for fetching raw market data from an external provider.
    /// Implement this interface to switch between Groww delayed, Groww live,
    /// or any other data source without touching the business logic or UI layers.
    /// </summary>
    public interface IMarketDataProvider
    {
        /// <summary>
        /// Fetches raw NIFTY 50 5-minute candle data from the underlying provider.
        /// </summary>
        /// <param name="startMs">Range start as Unix epoch in MILLISECONDS.</param>
        /// <param name="endMs">Range end as Unix epoch in MILLISECONDS.</param>
        /// <returns>
        /// Parsed DTO on success; null if the provider is unavailable or returns no data.
        /// </returns>
        Task<GrowwChartResponseDto?> GetRawDataAsync(long startMs, long endMs);
    }
}
