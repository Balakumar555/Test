namespace Test.Models.Signal
{
    /// <summary>Type of trading signal emitted by the engine.</summary>
    public enum SignalType { Buy, Sell }

    /// <summary>
    /// A fully qualified BUY or SELL signal generated after all strategy conditions are met.
    /// </summary>
    public class TradingSignal
    {
        /// <summary>BUY or SELL.</summary>
        public SignalType Type { get; init; }

        /// <summary>Recommended entry price (confirmation candle High for BUY, Low for SELL).</summary>
        public double EntryPrice { get; init; }

        /// <summary>IST time when the signal was generated (HH:mm:ss).</summary>
        public string Time { get; init; } = string.Empty;

        /// <summary>Unix timestamp (seconds) of the signal candle — used to place the chart marker.</summary>
        public long TimestampSeconds { get; init; }

        /// <summary>Which level was swept: "PDH", "PDC", or "PDL".</summary>
        public string LiquidityLevel { get; init; } = string.Empty;

        /// <summary>The extreme price reached during the sweep (candle Low for bullish, High for bearish).</summary>
        public double SweepPrice { get; init; }

        /// <summary>IST time the sweep was detected.</summary>
        public string SweepTime { get; init; } = string.Empty;

        /// <summary>High of the bullish confirmation candle (for BUY).</summary>
        public double ConfirmationHigh { get; init; }

        /// <summary>Low of the bearish confirmation candle (for SELL).</summary>
        public double ConfirmationLow { get; init; }

        /// <summary>IST time the confirmation candle closed.</summary>
        public string ConfirmationTime { get; init; } = string.Empty;

        /// <summary>Human-readable reason string for the signal panel.</summary>
        public string Reason { get; init; } = string.Empty;
    }
}
