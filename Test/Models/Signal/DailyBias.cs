namespace Test.Models.Signal
{
    /// <summary>Daily directional bias determined from the previous trading day's 1-day candle.</summary>
    public enum DailyBias
    {
        /// <summary>Lower wick dominates — expect upward price movement today.</summary>
        Bullish,

        /// <summary>Upper wick dominates — expect downward price movement today.</summary>
        Bearish,

        /// <summary>Equal wicks with a doji body — no directional edge; no signals generated.</summary>
        Neutral
    }
}
