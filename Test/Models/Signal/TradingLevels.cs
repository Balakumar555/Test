namespace Test.Models.Signal
{
    /// <summary>
    /// Previous-day price levels and the daily directional bias.
    /// Populated once per trading session by NiftyDailyBiasService.
    /// </summary>
    public class TradingLevels
    {
        /// <summary>Previous Day High — key resistance / sweep level.</summary>
        public double PDH { get; init; }

        /// <summary>Previous Day Close — mid-range reference level.</summary>
        public double PDC { get; init; }

        /// <summary>Previous Day Low — key support / sweep level.</summary>
        public double PDL { get; init; }

        /// <summary>Upper wick of the previous day's 1-day candle (points).</summary>
        public double PrevDayUpperWick { get; init; }

        /// <summary>Lower wick of the previous day's 1-day candle (points).</summary>
        public double PrevDayLowerWick { get; init; }

        /// <summary>Body of the previous day's 1-day candle (points).</summary>
        public double PrevDayBody { get; init; }

        /// <summary>Directional bias for today derived from the previous-day wick structure.</summary>
        public DailyBias Bias { get; init; }

        /// <summary>The trading date for which this bias is valid (IST).</summary>
        public DateOnly BiasDate { get; init; }

        /// <summary>True when all levels are populated.</summary>
        public bool IsValid => PDH > 0 && PDL > 0 && PDC > 0;
    }
}
