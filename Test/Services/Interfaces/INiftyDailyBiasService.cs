using Test.Models.Signal;

namespace Test.Services.Interfaces
{
    /// <summary>
    /// Fetches the previous trading day's 1-day NIFTY 50 candle and computes:
    ///   - PDH / PDC / PDL
    ///   - Daily directional bias (Bullish / Bearish / Neutral)
    /// Result is cached per trading date so the Groww API is called only once per session.
    /// </summary>
    public interface INiftyDailyBiasService
    {
        /// <summary>
        /// Returns the TradingLevels for today's session.
        /// Uses IMemoryCache — subsequent calls within the same trading day return the cached result.
        /// </summary>
        Task<TradingLevels> GetTodayLevelsAsync();
    }
}
