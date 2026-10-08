namespace Test.Models.Signal
{
    /// <summary>
    /// Strategy configuration values loaded from appsettings.json → "TradingStrategy".
    /// Change values in appsettings.json; no code recompile needed.
    /// </summary>
    public class TradingStrategyOptions
    {
        public const string SectionName = "TradingStrategy";

        /// <summary>
        /// How many NIFTY points the candle's wick must penetrate beyond the level to count as a sweep.
        /// Default 5 pts.  Set to 0 to accept any penetration.
        /// </summary>
        public double SweepTolerancePoints { get; set; } = 5;

        /// <summary>
        /// If true, the candle's CLOSE must recover back inside (above/below) the level after sweeping.
        /// Recommended true — confirms rejection, not just a brief touch.
        /// </summary>
        public bool RequireCloseBackInsideLevel { get; set; } = true;

        /// <summary>
        /// Minutes after a sweep is detected before the setup is considered stale and discarded.
        /// Engine returns to WaitingForSweep if no confirmation candle closes within this window.
        /// Default 10 min.
        /// </summary>
        public int SetupExpiryMinutes { get; set; } = 10;

        /// <summary>
        /// Maximum BUY/SELL signals emitted per trading day.
        /// After this count is reached the engine stops scanning until the next trading day.
        /// Default 3.
        /// </summary>
        public int MaxSignalsPerDay { get; set; } = 3;

        /// <summary>NSE regular-session open time in IST (HH:mm). Default "09:15".</summary>
        public string MarketStartTime { get; set; } = "09:15";

        /// <summary>NSE regular-session close time in IST (HH:mm). Default "15:10".</summary>
        public string MarketEndTime { get; set; } = "15:10";
    }
}
