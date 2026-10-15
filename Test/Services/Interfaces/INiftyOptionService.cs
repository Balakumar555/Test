using Test.Models.Options;

namespace Test.Services.Interfaces
{
    /// <summary>
    /// Fetches live NIFTY option prices from the Groww FnO batch endpoint
    /// and returns a structured option chain for display.
    /// </summary>
    public interface INiftyOptionService
    {
        /// <summary>
        /// Fetches live CE/PE prices for strikes around the given ATM price.
        /// Symbols are generated automatically from config (expiry, strikeInterval, numStrikes).
        /// </summary>
        /// <param name="atmPrice">Current NIFTY price used to compute ATM strike.</param>
        Task<OptionChainViewModel> GetOptionChainAsync(double atmPrice);
    }
}
