using System.Text.Json.Serialization;

namespace Test.Models.Signal
{
    /// <summary>Lifecycle status of a single paper trade.</summary>
    public enum TradeStatus
    {
        /// <summary>Position is open — monitoring for stop-loss or end-of-day exit.</summary>
        Active,

        /// <summary>Closed because the NIFTY 50 index crossed the confirmation-candle stop-loss level.</summary>
        StoppedOut,

        /// <summary>Closed at end of session (market close or manual).</summary>
        ClosedEOD
    }

    /// <summary>
    /// A single paper (simulated) option-selling trade.
    ///
    /// Strategy:
    ///   BUY signal  (bullish) → short PUT  2 strikes OTM (ATM−100 PE)
    ///   SELL signal (bearish) → short CALL 2 strikes OTM (ATM+100 CE)
    ///
    /// Stop-loss reference: NIFTY 50 index price (NOT the option premium).
    ///   BUY  → SL = confirmation candle Low  (index breaks below that → exit)
    ///   SELL → SL = confirmation candle High (index breaks above that → exit)
    /// </summary>
    public class PaperTrade
    {
        /// <summary>Sequential ID: T001, T002, …</summary>
        [JsonPropertyName("tradeId")]
        public string TradeId { get; set; } = string.Empty;

        [JsonPropertyName("tradingDate")]
        public string TradingDate { get; set; } = string.Empty;

        [JsonPropertyName("optionSymbol")]
        public string OptionSymbol { get; set; } = string.Empty;

        /// <summary>"Buy" = bullish signal; we sold a PUT. "Sell" = bearish; we sold a CALL.</summary>
        [JsonPropertyName("signalType")]
        public string SignalType { get; set; } = string.Empty;

        [JsonPropertyName("optionType")]
        public string OptionType => OptionSymbol.EndsWith("PE") ? "PE" : "CE";

        /// <summary>
        /// Deduplication key — <see cref="TradingSignal.TimestampSeconds"/> of the originating signal.
        /// Prevents the same signal from opening two trades on repeated polls.
        /// </summary>
        [JsonPropertyName("signalTimestampSeconds")]
        public long SignalTimestampSeconds { get; set; }

        [JsonPropertyName("entryPremium")]
        public double EntryPremium { get; set; }

        [JsonPropertyName("currentPremium")]
        public double CurrentPremium { get; set; }

        [JsonPropertyName("exitPremium")]
        public double ExitPremium { get; set; }

        // ── Stop-loss ─────────────────────────────────────────────────────────

        /// <summary>
        /// NIFTY 50 index price that triggers stop-loss exit (primary SL reference).
        /// BUY  → SweepCandleHigh (first red candle High on NIFTY)
        /// SELL → ConfirmationHigh (red confirmation candle High on NIFTY)
        /// </summary>
        [JsonPropertyName("stopLossLevel")]
        public double StopLossLevel { get; set; }

        /// <summary>
        /// Option premium SL level drawn as a red horizontal line on the right chart.
        /// = HIGH of the option candle at sweep-candle time.
        /// If premium rises to/above this, the position is closed.
        /// Updated by trailing-SL logic: moves DOWN as premium falls (locking in profit).
        /// </summary>
        [JsonPropertyName("slPremium")]
        public double SlPremium { get; set; }

        /// <summary>Initial SL premium (before trailing; preserved for reference).</summary>
        [JsonPropertyName("initialSlPremium")]
        public double InitialSlPremium { get; set; }

        /// <summary>IST time of the confirmation candle (for reference display).</summary>
        [JsonPropertyName("confirmationCandleTime")]
        public string ConfirmationCandleTime { get; set; } = string.Empty;

        // ── Trade lifecycle ───────────────────────────────────────────────────

        [JsonPropertyName("status")]
        public TradeStatus Status { get; set; } = TradeStatus.Active;

        /// <summary>Computed: true when Status ≠ Active.</summary>
        [JsonPropertyName("isClosed")]
        public bool IsClosed => Status != TradeStatus.Active;

        [JsonPropertyName("entryTime")]
        public string EntryTime { get; set; } = string.Empty;

        [JsonPropertyName("exitTime")]
        public string ExitTime { get; set; } = string.Empty;

        /// <summary>E.g. "StopLoss: NIFTY=22680.00 ≤ SL=22720.50" or "MarketClose".</summary>
        [JsonPropertyName("exitReason")]
        public string ExitReason { get; set; } = string.Empty;

        // ── Chart timestamp helpers (fake-UTC seconds, same convention as markers) ──

        /// <summary>Signal breakout-candle timestamp used for entry marker on option chart.</summary>
        [JsonPropertyName("entryTimestampFakeUtc")]
        public long EntryTimestampFakeUtc { get; set; }

        /// <summary>Timestamp when SL was triggered; 0 if not yet exited.</summary>
        [JsonPropertyName("exitTimestampFakeUtc")]
        public long ExitTimestampFakeUtc { get; set; }

        // ── P&L ───────────────────────────────────────────────────────────────

        /// <summary>NIFTY lot size = 65.</summary>
        [JsonPropertyName("lotSize")]
        public int LotSize { get; set; } = 65;

        /// <summary>
        /// Short-option P&amp;L: profit when premium falls after entry.
        /// (EntryPremium − CurrentOrExitPremium) × LotSize
        /// </summary>
        [JsonPropertyName("pnl")]
        public double PnL => IsClosed
            ? (EntryPremium - ExitPremium)  * LotSize
            : (EntryPremium - CurrentPremium) * LotSize;
    }

    /// <summary>Day-aggregated P&amp;L for a single trading date.</summary>
    public class DayWisePnL
    {
        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("trades")]
        public List<PaperTrade> Trades { get; set; } = new();

        [JsonPropertyName("totalPnl")]
        public double TotalPnL => Trades.Sum(t => t.PnL);

        [JsonPropertyName("winningTrades")]
        public int WinningTrades => Trades.Count(t => t.PnL > 0);

        [JsonPropertyName("losingTrades")]
        public int LosingTrades => Trades.Count(t => t.PnL < 0);
    }
}
