using System.Text.Json.Serialization;

namespace Test.Models
{
    /// <summary>
    /// A single 5-minute OHLC candle returned to the browser.
    /// Time is ISO 8601 with IST offset (+05:30).
    /// </summary>
    public class CandleViewModel
    {
        /// <summary>ISO 8601 timestamp in IST, e.g. "2026-10-08T09:15:00+05:30"</summary>
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
    }
}
