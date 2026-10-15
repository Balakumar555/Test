using Test.Models.Dto;
using Test.Models.Options;

namespace Test.Services.Interfaces
{
    /// <summary>
    /// Fetches live NIFTY option data from Groww FnO APIs.
    /// </summary>
    public interface INiftyOptionService
    {
        /// <summary>
        /// Fetches live CE/PE prices for strikes around the given ATM price (option chain table).
        /// </summary>
        Task<OptionChainViewModel> GetOptionChainAsync(double atmPrice);

        /// <summary>
        /// Fetches 1-minute OHLCV candles for a specific option symbol from the Groww FnO chart API.
        /// </summary>
        Task<List<OptionCandleDto>> GetOptionChartAsync(string symbol, long startMs, long endMs);

        /// <summary>
        /// Fetches the current LTP for a single option symbol via the batch price endpoint.
        /// Returns 0 on error or if the symbol is not found.
        /// </summary>
        Task<double> GetSingleOptionPriceAsync(string symbol);
    }
}
