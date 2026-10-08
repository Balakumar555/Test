using System.Text.Json.Serialization;

namespace Test.Models.Dto
{
    /// <summary>
    /// Mirrors the exact JSON shape returned by the Groww delayed charting API.
    /// Candles are returned as a jagged array: [unix_seconds, open, high, low, close, null_or_volume]
    /// Note: timestamp is in SECONDS, but API query parameters use MILLISECONDS.
    /// The 6th element (volume) may be null — hence nullable double.
    /// </summary>
    public class GrowwChartResponseDto
    {
        /// <summary>
        /// Each element is: [unixTimestampSeconds, open, high, low, close, volume?]
        /// Index 5 (volume) can be null in the Groww response.
        /// </summary>
        [JsonPropertyName("candles")]
        public double?[][] Candles { get; set; } = Array.Empty<double?[]>();

        [JsonPropertyName("changeValue")]
        public double? ChangeValue { get; set; }

        [JsonPropertyName("changePerc")]
        public double? ChangePerc { get; set; }

        [JsonPropertyName("closingPrice")]
        public double? ClosingPrice { get; set; }

        [JsonPropertyName("startTimeEpochInMillis")]
        public long? StartTimeEpochInMillis { get; set; }
    }
}
