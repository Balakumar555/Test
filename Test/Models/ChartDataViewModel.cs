using System.Text.Json.Serialization;

namespace Test.Models
{
    /// <summary>
    /// The complete JSON payload delivered to the browser by GET /Nifty/GetChartData.
    /// </summary>
    public class ChartDataViewModel
    {
        /// <summary>Close price of the most recent candle of the current trading day.</summary>
        [JsonPropertyName("currentPrice")]
        public double CurrentPrice { get; set; }

        /// <summary>Maximum High across all 5-min candles of the previous trading day.</summary>
        [JsonPropertyName("previousDayHigh")]
        public double PreviousDayHigh { get; set; }

        /// <summary>Minimum Low across all 5-min candles of the previous trading day.</summary>
        [JsonPropertyName("previousDayLow")]
        public double PreviousDayLow { get; set; }

        /// <summary>Close of the final candle of the previous trading day.</summary>
        [JsonPropertyName("previousDayClose")]
        public double PreviousDayClose { get; set; }

        /// <summary>Maximum High across all 5-min candles of the current trading day.</summary>
        [JsonPropertyName("currentDayHigh")]
        public double CurrentDayHigh { get; set; }

        /// <summary>Minimum Low across all 5-min candles of the current trading day.</summary>
        [JsonPropertyName("currentDayLow")]
        public double CurrentDayLow { get; set; }

        /// <summary>IST date string of the current trading day, e.g. "08 Oct 2026".</summary>
        [JsonPropertyName("currentTradingDate")]
        public string CurrentTradingDate { get; set; } = string.Empty;

        /// <summary>IST date string of the previous trading day.</summary>
        [JsonPropertyName("previousTradingDate")]
        public string PreviousTradingDate { get; set; } = string.Empty;

        /// <summary>Current-day 5-minute candles ordered by time ascending.</summary>
        [JsonPropertyName("candles")]
        public List<CandleViewModel> Candles { get; set; } = new();

        /// <summary>True when data was successfully fetched; false on error.</summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; } = true;

        /// <summary>Human-readable error message; null when Success is true.</summary>
        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }
    }
}
