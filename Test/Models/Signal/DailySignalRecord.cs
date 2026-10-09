using System.Text.Json.Serialization;

namespace Test.Models.Signal
{
    /// <summary>
    /// Holds all signals generated on a single trading day.
    /// Archived by SignalHistoryService when the engine resets for a new day.
    /// </summary>
    public class DailySignalRecord
    {
        /// <summary>IST calendar date (yyyy-MM-dd).</summary>
        [JsonPropertyName("date")]
        public string Date { get; init; } = string.Empty;

        /// <summary>All BUY / SELL signals fired on this date.</summary>
        [JsonPropertyName("signals")]
        public List<TradingSignal> Signals { get; init; } = new();

        /// <summary>Total number of signals.</summary>
        [JsonPropertyName("totalSignals")]
        public int TotalSignals => Signals.Count;
    }
}
