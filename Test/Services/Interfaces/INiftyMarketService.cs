using Test.Models;

namespace Test.Services.Interfaces
{
    /// <summary>
    /// Business logic interface for NIFTY 50 market data.
    /// Orchestrates data fetching, IST conversion, trading-day identification,
    /// and Previous Day High / Low / Close calculation.
    /// </summary>
    public interface INiftyMarketService
    {
        /// <summary>
        /// Returns a fully processed <see cref="ChartDataViewModel"/> ready for the browser.
        /// Includes current-day candles and previous-day key levels.
        /// On failure the returned object will have Success=false and an ErrorMessage.
        /// </summary>
        Task<ChartDataViewModel> GetChartDataAsync();
    }
}
