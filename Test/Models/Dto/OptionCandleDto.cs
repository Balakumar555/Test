using System.Text.Json.Serialization;

namespace Test.Models.Dto
{
    /// <summary>
    /// One OHLCV candle for an option symbol.
    /// Time is ISO 8601 with IST offset (+05:30) — same format as CandleViewModel.
    /// </summary>
    public class OptionCandleDto
    {
        /// <summary>ISO 8601 in IST, e.g. "2026-10-15T09:15:00+05:30".</summary>
        [JsonPropertyName("time")]
        public string Time { get; set; } = string.Empty;

        [JsonPropertyName("open")]
        public double Open { get; set; }

        [JsonPropertyName("high")]
        public double High { get; set; }

        [JsonPropertyName("low")]
        public double Low { get; set; }

        [JsonPropertyName("close")]
        public double Close { get; set; }

        [JsonPropertyName("volume")]
        public double Volume { get; set; }
    }
}
