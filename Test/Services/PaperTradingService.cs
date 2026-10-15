using Test.Models.Signal;
using Test.Services.Interfaces;

namespace Test.Services
{
    /// <summary>
    /// Thread-safe singleton managing sequential paper option-selling trades.
    ///
    /// Stop-loss (two layers):
    ///   1. NIFTY index SL: <see cref="PaperTrade.StopLossLevel"/> (sweep-candle High on NIFTY).
    ///   2. Option premium SL: <see cref="PaperTrade.SlPremium"/> — the option's HIGH at sweep-candle
    ///      time, drawn as a red line on the chart. If premium rises to/above this, close the trade.
    ///
    /// Trailing SL:
    ///   Every time the option premium falls (favourable move for short option), the premium SL
    ///   is moved down by <see cref="TrailingSLStep"/> to lock in profit. SL only ever tightens.
    ///
    /// Lot size: 65 (NIFTY current contract).
    /// </summary>
    public class PaperTradingService : IPaperTradingService
    {
        // ── Trailing SL step (option premium points) ──────────────────────────
        // Every time the option premium drops this many points below the current SL,
        // the SL is moved down by the same amount, locking in that much profit.
        private const double TrailingSLStep = 20.0;

        private readonly List<PaperTrade> _allTrades   = new();
        private PaperTrade? _activeTrade;
        private readonly HashSet<long> _processedSignalTimestamps = new();
        private readonly object _lock = new();

        private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

        // ── Entry ────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public void RecordEntry(
            TradingSignal signal,
            string        optionSymbol,
            double        entryPremium,
            double        slPremium = 0)
        {
            lock (_lock)
            {
                if (_processedSignalTimestamps.Contains(signal.TimestampSeconds)) return;
                if (_activeTrade is { Status: TradeStatus.Active }) return;

                _processedSignalTimestamps.Add(signal.TimestampSeconds);

                // NIFTY index SL: sweep-candle High (BUY) or confirmation-candle High (SELL)
                double niftySL = signal.Type == SignalType.Buy
                    ? signal.SweepCandleHigh
                    : signal.ConfirmationHigh;

                var trade = new PaperTrade
                {
                    TradeId                = $"T{_allTrades.Count + 1:D3}",
                    TradingDate            = signal.TradingDate,
                    OptionSymbol           = optionSymbol,
                    SignalType             = signal.Type.ToString(),
                    SignalTimestampSeconds = signal.TimestampSeconds,
                    EntryPremium           = entryPremium,
                    CurrentPremium         = entryPremium,
                    StopLossLevel          = niftySL,
                    SlPremium              = slPremium,
                    InitialSlPremium       = slPremium,
                    ConfirmationCandleTime = signal.ConfirmationTime,
                    Status                 = TradeStatus.Active,
                    EntryTime              = DateTimeOffset.UtcNow.ToOffset(IstOffset).ToString("HH:mm:ss"),
                    EntryTimestampFakeUtc  = signal.TimestampSeconds,
                    LotSize                = 65
                };

                _allTrades.Add(trade);
                _activeTrade = trade;
            }
        }

        // ── Mark-to-market + trailing SL ─────────────────────────────────────

        /// <inheritdoc/>
        public void UpdateCurrentPrice(string optionSymbol, double currentLtp)
        {
            lock (_lock)
            {
                if (_activeTrade is not { Status: TradeStatus.Active }) return;
                if (_activeTrade.OptionSymbol != optionSymbol || currentLtp <= 0) return;

                _activeTrade.CurrentPremium = currentLtp;

                // ── Trailing SL ────────────────────────────────────────────────
                // For short options (both PUT and CALL), profit = premium falling.
                // Move the SL down as premium falls, locking in profit progressively.
                // Rule: new SL = current + TrailingSLStep; only update if new SL < current SL.
                if (_activeTrade.SlPremium > 0)
                {
                    double trailedSL = currentLtp + TrailingSLStep;
                    if (trailedSL < _activeTrade.SlPremium)
                    {
                        _activeTrade.SlPremium = trailedSL;
                    }
                }
            }
        }

        // ── Stop-loss check ───────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool CheckAndApplyStopLoss(double niftyCurrentPrice)
        {
            lock (_lock)
            {
                if (_activeTrade is not { Status: TradeStatus.Active }) return false;

                // Layer 1: NIFTY index SL
                bool niftySLHit = _activeTrade.StopLossLevel > 0 && (
                    _activeTrade.SignalType == "Buy"
                        ? niftyCurrentPrice <= _activeTrade.StopLossLevel
                        : niftyCurrentPrice >= _activeTrade.StopLossLevel);

                // Layer 2: Option premium SL (the red line on the option chart).
                // For short options, SL hit when premium rises to/above SlPremium.
                bool premiumSLHit = _activeTrade.SlPremium > 0
                    && _activeTrade.CurrentPremium > 0
                    && _activeTrade.CurrentPremium >= _activeTrade.SlPremium;

                if (!niftySLHit && !premiumSLHit) return false;

                var nowIst = DateTimeOffset.UtcNow.ToOffset(IstOffset);
                string reason = niftySLHit
                    ? $"NIFTY SL: index={niftyCurrentPrice:F2} hit SL={_activeTrade.StopLossLevel:F2}"
                    : $"Premium SL: option={_activeTrade.CurrentPremium:F2} rose to SL={_activeTrade.SlPremium:F2}";

                _activeTrade.Status               = TradeStatus.StoppedOut;
                _activeTrade.ExitPremium          = _activeTrade.CurrentPremium;
                _activeTrade.ExitTime             = nowIst.ToString("HH:mm:ss");
                _activeTrade.ExitReason           = reason;
                _activeTrade.ExitTimestampFakeUtc = ToFakeUtcSeconds(nowIst);

                return true;
            }
        }

        // ── Dedup ─────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool IsSignalAlreadyProcessed(long signalTimestampSeconds)
        {
            lock (_lock) { return _processedSignalTimestamps.Contains(signalTimestampSeconds); }
        }

        // ── Queries ───────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public PaperTrade? GetActiveTrade()
        {
            lock (_lock) { return _activeTrade is { Status: TradeStatus.Active } ? _activeTrade : null; }
        }

        /// <inheritdoc/>
        public PaperTrade? GetLastTrade()
        {
            lock (_lock) { return _activeTrade; }
        }

        /// <inheritdoc/>
        public string? GetActiveOptionSymbol()
        {
            lock (_lock)
            {
                return _activeTrade is { Status: TradeStatus.Active } ? _activeTrade.OptionSymbol : null;
            }
        }

        /// <inheritdoc/>
        public List<DayWisePnL> GetDayWisePnL()
        {
            lock (_lock)
            {
                return _allTrades
                    .GroupBy(t => t.TradingDate)
                    .OrderByDescending(g => g.Key)
                    .Select(g => new DayWisePnL
                    {
                        Date   = g.Key,
                        Trades = g.ToList()
                    })
                    .ToList();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static long ToFakeUtcSeconds(DateTimeOffset ist)
        {
            var local = new DateTime(ist.Year, ist.Month, ist.Day,
                ist.Hour, ist.Minute, ist.Second, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, TimeSpan.Zero).ToUnixTimeSeconds();
        }
    }
}
