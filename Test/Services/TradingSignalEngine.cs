using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Test.Models;
using Test.Models.Signal;
using Test.Services.Interfaces;

namespace Test.Services
{
    /// <summary>
    /// Singleton signal engine implementing a 9-state state machine.
    ///
    /// Thread safety: all mutable state is guarded by _lock.
    ///
    /// State transitions:
    ///   WaitingForBias
    ///     → BullishWaitingForSweep       (bias = Bullish)
    ///     → BearishWaitingForSweep       (bias = Bearish)
    ///     → Neutral                      (bias = Neutral)
    ///
    ///   BullishWaitingForSweep
    ///     → BullishWaitingForConfirmation (sweep below level detected)
    ///
    ///   BullishWaitingForConfirmation
    ///     → BullishWaitingForBreakout    (green close candle formed)
    ///     → BullishWaitingForSweep       (setup expired)
    ///
    ///   BullishWaitingForBreakout
    ///     → BuySignal                    (next candle High > confirmation High)
    ///     → BullishWaitingForSweep       (setup expired)
    ///
    ///   BearishWaitingForSweep
    ///     → BearishWaitingForConfirmation (sweep above level detected)
    ///
    ///   BearishWaitingForConfirmation
    ///     → BearishWaitingForBreakdown   (red close candle formed)
    ///     → BearishWaitingForSweep       (setup expired)
    ///
    ///   BearishWaitingForBreakdown
    ///     → SellSignal                   (next candle Low &lt; confirmation Low)
    ///     → BearishWaitingForSweep       (setup expired)
    ///
    ///   BuySignal / SellSignal
    ///     → BullishWaitingForSweep / BearishWaitingForSweep (when MaxSignalsPerDay not reached)
    ///     → engine halts for the day when MaxSignalsPerDay reached
    /// </summary>
    public class TradingSignalEngine : ITradingSignalEngine
    {
        // ── Dependencies ──────────────────────────────────────────────────────
        private readonly TradingStrategyOptions _opts;
        private readonly ILogger<TradingSignalEngine> _logger;
        private readonly ISignalHistoryService _history;

        private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

        // ── Thread safety ─────────────────────────────────────────────────────
        private readonly object _lock = new();

        // ── Mutable state (all guarded by _lock) ──────────────────────────────
        private SignalEngineState _state = SignalEngineState.WaitingForBias;
        private DateOnly          _tradingDate;                         // IST date for which engine was initialised
        private long              _lastProcessedTimestamp = 0;          // Unix seconds of last seen candle
        private int               _signalsToday;

        // Sweep context
        private string  _sweepLevel    = string.Empty;   // "PDH","PDC","PDL"
        private double  _sweepLevelPx;                   // price of the swept level
        private double  _sweepExtreme;                   // lowest Low (bullish) / highest High (bearish)
        private string  _sweepTime     = string.Empty;
        private DateTimeOffset _sweepTimestamp;          // used for expiry checks

        // Confirmation context
        private double  _confirmHigh;
        private double  _confirmLow;
        private string  _confirmTime   = string.Empty;
        private long    _confirmTimestampSeconds;
        private DateTimeOffset _confirmTimestamp;

        // Accumulated markers for the session
        private readonly List<ChartMarker> _markers = new();

        // All signals generated today (for history archival)
        private readonly List<TradingSignal> _todaySignals = new();

        // Signal + UI strings
        private TradingSignal? _activeSignal;
        private string _statusMessage = "Initialising…";
        private string _nextCondition = string.Empty;
        private string _lastEvent     = string.Empty;
        private string _lastEventTime = string.Empty;

        // ── Public interface ──────────────────────────────────────────────────
        public SignalEngineState CurrentState  { get { lock (_lock) return _state; } }
        public TradingSignal?    ActiveSignal  { get { lock (_lock) return _activeSignal; } }
        public IReadOnlyList<ChartMarker> Markers { get { lock (_lock) return _markers.AsReadOnly(); } }
        public IReadOnlyList<TradingSignal> TodaySignals { get { lock (_lock) return _todaySignals.AsReadOnly(); } }
        public string StatusMessage  { get { lock (_lock) return _statusMessage; } }
        public string NextCondition  { get { lock (_lock) return _nextCondition; } }
        public string LastEvent      { get { lock (_lock) return _lastEvent; } }
        public string LastEventTime  { get { lock (_lock) return _lastEventTime; } }
        public int    SignalsToday   { get { lock (_lock) return _signalsToday; } }

        public TradingSignalEngine(
            IOptions<TradingStrategyOptions> opts,
            ISignalHistoryService history,
            ILogger<TradingSignalEngine> logger)
        {
            _opts    = opts.Value;
            _history = history;
            _logger  = logger;
        }

        /// <inheritdoc/>
        public void ProcessCandles(IReadOnlyList<CandleViewModel> candles, TradingLevels levels)
        {
            if (candles.Count == 0) return;

            var nowIst  = DateTimeOffset.UtcNow.ToOffset(IstOffset);
            var todayIst = DateOnly.FromDateTime(nowIst.DateTime);

            lock (_lock)
            {
                // ── New trading day: reset all state ─────────────────────────
                if (_tradingDate != todayIst)
                {
                    ResetForNewDay(todayIst);
                }

                // ── Initialise bias on first call ────────────────────────────
                if (_state == SignalEngineState.WaitingForBias)
                {
                    ApplyBias(levels.Bias);
                }

                // ── No signals if neutral / max reached ──────────────────────
                if (_state == SignalEngineState.Neutral) return;
                if (_signalsToday >= _opts.MaxSignalsPerDay)
                {
                    _statusMessage = $"Max {_opts.MaxSignalsPerDay} signals reached for today.";
                    return;
                }

                // ── Parse market hours ────────────────────────────────────────
                TimeOnly.TryParse(_opts.MarketStartTime, out var marketStart);
                TimeOnly.TryParse(_opts.MarketEndTime,   out var marketEnd);

                // ── Filter candles: closed, within market hours, not yet processed ──
                // Only process CLOSED candles: skip the last (currently forming) candle
                // if markets are open.
                var closedCandles = candles
                    .Where(c =>
                    {
                        if (!TryParseIst(c.Time, out var ist)) return false;
                        // ── Critical: only accept candles that belong to TODAY (IST) ──
                        // Prevents yesterday's candles from being processed when the
                        // Groww API hasn't yet returned today's data (e.g. first few
                        // minutes after market open).
                        if (DateOnly.FromDateTime(ist.DateTime) != todayIst) return false;
                        var t = TimeOnly.FromTimeSpan(ist.TimeOfDay);
                        return t >= marketStart && t <= marketEnd;
                    })
                    .OrderBy(c => c.Time)
                    .ToList();

                // Skip the last candle if we're mid-minute (markets open)
                // A candle is "closed" if its timestamp is at least 60 s in the past.
                var cutoff = nowIst.ToUnixTimeSeconds() - 60;
                var newCandles = closedCandles
                    .Where(c =>
                    {
                        if (!TryParseIst(c.Time, out var ist)) return false;
                        long unixSec = ist.ToUnixTimeSeconds();
                        return unixSec > _lastProcessedTimestamp && unixSec <= cutoff;
                    })
                    .ToList();

                if (newCandles.Count == 0) return;

                _logger.LogDebug("Processing {N} new 1-min candles. State={State}",
                    newCandles.Count, _state);

                foreach (var candle in newCandles)
                {
                    if (!TryParseIst(candle.Time, out var candleIst)) continue;

                    // Advance watermark
                    _lastProcessedTimestamp = candleIst.ToUnixTimeSeconds();

                    Tick(candle, candleIst, levels, nowIst);

                    // Stop if we hit max signals
                    if (_signalsToday >= _opts.MaxSignalsPerDay) break;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // State machine tick — called once per new closed 1-min candle
        // ─────────────────────────────────────────────────────────────────────
        private void Tick(
            CandleViewModel candle,
            DateTimeOffset  candleIst,
            TradingLevels   levels,
            DateTimeOffset  nowIst)
        {
            switch (_state)
            {
                case SignalEngineState.BullishWaitingForSweep:
                    TryBullishSweep(candle, candleIst, levels);
                    break;

                case SignalEngineState.BullishWaitingForConfirmation:
                    if (IsExpired(candleIst))
                    {
                        ExpireSetup(isBullish: true);
                        break;
                    }
                    TryBullishConfirmation(candle, candleIst);
                    break;

                case SignalEngineState.BullishWaitingForBreakout:
                    if (IsExpired(candleIst))
                    {
                        ExpireSetup(isBullish: true);
                        break;
                    }
                    TryBullishBreakout(candle, candleIst, levels);
                    break;

                case SignalEngineState.BearishWaitingForSweep:
                    TryBearishSweep(candle, candleIst, levels);
                    break;

                case SignalEngineState.BearishWaitingForConfirmation:
                    if (IsExpired(candleIst))
                    {
                        ExpireSetup(isBullish: false);
                        break;
                    }
                    TryBearishConfirmation(candle, candleIst);
                    break;

                case SignalEngineState.BearishWaitingForBreakdown:
                    if (IsExpired(candleIst))
                    {
                        ExpireSetup(isBullish: false);
                        break;
                    }
                    TryBearishBreakdown(candle, candleIst, levels);
                    break;

                // After signal: allow further setups until max reached
                case SignalEngineState.BuySignal:
                case SignalEngineState.SellSignal:
                    // Already recorded; reset to scan for next setup
                    ApplyBias(levels.Bias);
                    Tick(candle, candleIst, levels, nowIst); // re-process this candle in new state
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Bullish path
        // ─────────────────────────────────────────────────────────────────────
        private void TryBullishSweep(CandleViewModel candle, DateTimeOffset candleIst, TradingLevels levels)
        {
            double tol = _opts.SweepTolerancePoints;

            // Check Previous Day levels
            var sweepLevel = CheckSweepDown(candle, levels.PDH, tol, "PDH")
                          ?? CheckSweepDown(candle, levels.PDC, tol, "PDC")
                          ?? CheckSweepDown(candle, levels.PDL, tol, "PDL");

            // Check Opening Range levels (only once OR period has locked)
            if (sweepLevel == null && levels.OpeningRangeReady)
            {
                sweepLevel = CheckSweepDown(candle, levels.ORL, tol, "ORL")
                          ?? CheckSweepDown(candle, levels.ORH, tol, "ORH");
            }

            if (sweepLevel == null) return;

            _sweepLevel     = sweepLevel.Value.name;
            _sweepLevelPx   = sweepLevel.Value.levelPrice;
            _sweepExtreme   = candle.Low;
            _sweepTime      = candleIst.ToString("HH:mm:ss");
            _sweepTimestamp = candleIst;

            var timeSecFake = ToDisplaySeconds(candle.Time);
            if (timeSecFake > 0)
                _markers.Add(new ChartMarker
                {
                    Time     = timeSecFake,
                    Position = "belowBar",
                    Color    = "#f0b429",
                    Shape    = "circle",
                    Text     = $"Sweep {_sweepLevel}"
                });

            _state         = SignalEngineState.BullishWaitingForConfirmation;
            _lastEvent     = $"{_sweepLevel} liquidity swept (Low={candle.Low:F2})";
            _lastEventTime = _sweepTime;
            _statusMessage = $"Bullish sweep on {_sweepLevel} detected";
            _nextCondition = "Waiting for GREEN 1-min confirmation candle";

            _logger.LogInformation("Bullish sweep detected on {Level} at {Time}.", _sweepLevel, _sweepTime);
        }

        private void TryBullishConfirmation(CandleViewModel candle, DateTimeOffset candleIst)
        {
            // Need a closed GREEN candle (Close > Open)
            if (candle.Close <= candle.Open) return;

            _confirmHigh             = candle.High;
            _confirmLow              = candle.Low;
            _confirmTime             = candleIst.ToString("HH:mm:ss");
            _confirmTimestamp        = candleIst;
            _confirmTimestampSeconds = candleIst.ToUnixTimeSeconds();

            var timeSecFake = ToDisplaySeconds(candle.Time);
            if (timeSecFake > 0)
                _markers.Add(new ChartMarker
                {
                    Time     = timeSecFake,
                    Position = "belowBar",
                    Color    = "#26a69a",
                    Shape    = "circle",
                    Text     = "Confirmation"
                });

            _state         = SignalEngineState.BullishWaitingForBreakout;
            _lastEvent     = $"Green confirmation candle (High={_confirmHigh:F2})";
            _lastEventTime = _confirmTime;
            _statusMessage = "Confirmation candle formed";
            _nextCondition = $"Next candle must cross High = {_confirmHigh:F2}";

            _logger.LogInformation("Bullish confirmation candle at {Time}. High={High}", _confirmTime, _confirmHigh);
        }

        private void TryBullishBreakout(CandleViewModel candle, DateTimeOffset candleIst, TradingLevels levels)
        {
            // The NEXT candle's High must exceed confirmation candle High
            if (candle.High <= _confirmHigh) return;

            double entryPx = _confirmHigh;
            var timeStr    = candleIst.ToString("HH:mm:ss");
            var timeSecFake = ToDisplaySeconds(candle.Time);

            if (timeSecFake > 0)
                _markers.Add(new ChartMarker
                {
                    Time     = timeSecFake,
                    Position = "belowBar",
                    Color    = "#26a69a",
                    Shape    = "arrowUp",
                    Text     = "BUY"
                });

            _activeSignal = new TradingSignal
            {
                Type               = SignalType.Buy,
                EntryPrice         = entryPx,
                Time               = timeStr,
                TimestampSeconds   = timeSecFake,
                LiquidityLevel     = _sweepLevel,
                SweepPrice         = _sweepExtreme,
                SweepTime          = _sweepTime,
                ConfirmationHigh   = _confirmHigh,
                ConfirmationLow    = _confirmLow,
                ConfirmationTime   = _confirmTime,
                Reason             = $"Bullish liquidity sweep on {_sweepLevel} + green candle high breakout",
                TradingDate        = _tradingDate.ToString("yyyy-MM-dd")
            };

            _todaySignals.Add(_activeSignal);
            _signalsToday++;
            _state         = SignalEngineState.BuySignal;
            _lastEvent     = $"BUY triggered at {entryPx:F2}";
            _lastEventTime = timeStr;
            _statusMessage = $"BUY SIGNAL — Entry {entryPx:F2}";
            _nextCondition = _signalsToday < _opts.MaxSignalsPerDay
                ? "Scanning for next setup…"
                : "Max signals reached for today.";

            _logger.LogInformation("BUY signal generated. Entry={Entry} Level={Level} Signals={S}",
                entryPx, _sweepLevel, _signalsToday);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Bearish path
        // ─────────────────────────────────────────────────────────────────────
        private void TryBearishSweep(CandleViewModel candle, DateTimeOffset candleIst, TradingLevels levels)
        {
            double tol = _opts.SweepTolerancePoints;

            // Check Previous Day levels
            var sweepLevel = CheckSweepUp(candle, levels.PDH, tol, "PDH")
                          ?? CheckSweepUp(candle, levels.PDC, tol, "PDC")
                          ?? CheckSweepUp(candle, levels.PDL, tol, "PDL");

            // Check Opening Range levels (only once OR period has locked)
            if (sweepLevel == null && levels.OpeningRangeReady)
            {
                sweepLevel = CheckSweepUp(candle, levels.ORH, tol, "ORH")
                          ?? CheckSweepUp(candle, levels.ORL, tol, "ORL");
            }

            if (sweepLevel == null) return;

            _sweepLevel     = sweepLevel.Value.name;
            _sweepLevelPx   = sweepLevel.Value.levelPrice;
            _sweepExtreme   = candle.High;
            _sweepTime      = candleIst.ToString("HH:mm:ss");
            _sweepTimestamp = candleIst;

            var timeSecFake = ToDisplaySeconds(candle.Time);
            if (timeSecFake > 0)
                _markers.Add(new ChartMarker
                {
                    Time     = timeSecFake,
                    Position = "aboveBar",
                    Color    = "#f0b429",
                    Shape    = "circle",
                    Text     = $"Sweep {_sweepLevel}"
                });

            _state         = SignalEngineState.BearishWaitingForConfirmation;
            _lastEvent     = $"{_sweepLevel} liquidity swept (High={candle.High:F2})";
            _lastEventTime = _sweepTime;
            _statusMessage = $"Bearish sweep on {_sweepLevel} detected";
            _nextCondition = "Waiting for RED 1-min confirmation candle";

            _logger.LogInformation("Bearish sweep detected on {Level} at {Time}.", _sweepLevel, _sweepTime);
        }

        private void TryBearishConfirmation(CandleViewModel candle, DateTimeOffset candleIst)
        {
            if (candle.Close >= candle.Open) return;  // need RED candle

            _confirmHigh             = candle.High;
            _confirmLow              = candle.Low;
            _confirmTime             = candleIst.ToString("HH:mm:ss");
            _confirmTimestamp        = candleIst;
            _confirmTimestampSeconds = candleIst.ToUnixTimeSeconds();

            var timeSecFake = ToDisplaySeconds(candle.Time);
            if (timeSecFake > 0)
                _markers.Add(new ChartMarker
                {
                    Time     = timeSecFake,
                    Position = "aboveBar",
                    Color    = "#ef5350",
                    Shape    = "circle",
                    Text     = "Confirmation"
                });

            _state         = SignalEngineState.BearishWaitingForBreakdown;
            _lastEvent     = $"Red confirmation candle (Low={_confirmLow:F2})";
            _lastEventTime = _confirmTime;
            _statusMessage = "Confirmation candle formed";
            _nextCondition = $"Next candle must cross Low = {_confirmLow:F2}";

            _logger.LogInformation("Bearish confirmation candle at {Time}. Low={Low}", _confirmTime, _confirmLow);
        }

        private void TryBearishBreakdown(CandleViewModel candle, DateTimeOffset candleIst, TradingLevels levels)
        {
            if (candle.Low >= _confirmLow) return;

            double entryPx = _confirmLow;
            var timeStr    = candleIst.ToString("HH:mm:ss");
            var timeSecFake = ToDisplaySeconds(candle.Time);

            if (timeSecFake > 0)
                _markers.Add(new ChartMarker
                {
                    Time     = timeSecFake,
                    Position = "aboveBar",
                    Color    = "#ef5350",
                    Shape    = "arrowDown",
                    Text     = "SELL"
                });

            _activeSignal = new TradingSignal
            {
                Type               = SignalType.Sell,
                EntryPrice         = entryPx,
                Time               = timeStr,
                TimestampSeconds   = timeSecFake,
                LiquidityLevel     = _sweepLevel,
                SweepPrice         = _sweepExtreme,
                SweepTime          = _sweepTime,
                ConfirmationHigh   = _confirmHigh,
                ConfirmationLow    = _confirmLow,
                ConfirmationTime   = _confirmTime,
                Reason             = $"Bearish liquidity sweep on {_sweepLevel} + red candle low breakdown",
                TradingDate        = _tradingDate.ToString("yyyy-MM-dd")
            };

            _todaySignals.Add(_activeSignal);
            _signalsToday++;
            _state         = SignalEngineState.SellSignal;
            _lastEvent     = $"SELL triggered at {entryPx:F2}";
            _lastEventTime = timeStr;
            _statusMessage = $"SELL SIGNAL — Entry {entryPx:F2}";
            _nextCondition = _signalsToday < _opts.MaxSignalsPerDay
                ? "Scanning for next setup…"
                : "Max signals reached for today.";

            _logger.LogInformation("SELL signal generated. Entry={Entry} Level={Level} Signals={S}",
                entryPx, _sweepLevel, _signalsToday);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Sweep helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns non-null if the candle swept DOWN below the level and recovered above it.
        /// Bullish sweep: Low dips below (level - tolerance) AND Close >= (level - tolerance).
        /// </summary>
        private (string name, double levelPrice)? CheckSweepDown(
            CandleViewModel candle, double level, double tol, string name)
        {
            if (level <= 0) return null;

            bool swept    = candle.Low < level - tol;
            bool recovered = !_opts.RequireCloseBackInsideLevel
                             || candle.Close >= (level - tol);

            return (swept && recovered) ? (name, level) : null;
        }

        /// <summary>
        /// Returns non-null if the candle swept UP above the level and recovered below it.
        /// Bearish sweep: High exceeds (level + tolerance) AND Close <= (level + tolerance).
        /// </summary>
        private (string name, double levelPrice)? CheckSweepUp(
            CandleViewModel candle, double level, double tol, string name)
        {
            if (level <= 0) return null;

            bool swept    = candle.High > level + tol;
            bool recovered = !_opts.RequireCloseBackInsideLevel
                             || candle.Close <= (level + tol);

            return (swept && recovered) ? (name, level) : null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Expiry
        // ─────────────────────────────────────────────────────────────────────
        private bool IsExpired(DateTimeOffset candleIst)
        {
            var reference = _state == SignalEngineState.BullishWaitingForConfirmation ||
                            _state == SignalEngineState.BearishWaitingForConfirmation
                ? _sweepTimestamp
                : _confirmTimestamp;

            return (candleIst - reference).TotalMinutes > _opts.SetupExpiryMinutes;
        }

        private void ExpireSetup(bool isBullish)
        {
            _logger.LogInformation("Setup expired. Returning to {State}.",
                isBullish ? "BullishWaitingForSweep" : "BearishWaitingForSweep");

            _state         = isBullish
                ? SignalEngineState.BullishWaitingForSweep
                : SignalEngineState.BearishWaitingForSweep;
            _statusMessage = "Setup expired — scanning for new sweep";
            _nextCondition = isBullish
                ? "Waiting for price to sweep below PDH / PDC / PDL"
                : "Waiting for price to sweep above PDH / PDC / PDL";
            _lastEvent     = "Setup expired";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────
        private void ResetForNewDay(DateOnly today)
        {
            // ── Archive yesterday's signals before clearing ───────────────────
            if (_tradingDate != default && _todaySignals.Count > 0)
            {
                _history.Archive(_tradingDate.ToString("yyyy-MM-dd"), _todaySignals.AsReadOnly());
                _logger.LogInformation(
                    "Archived {Count} signal(s) for {Date}.", _todaySignals.Count, _tradingDate);
            }

            _tradingDate            = today;
            _state                  = SignalEngineState.WaitingForBias;
            _lastProcessedTimestamp = 0;
            _signalsToday           = 0;
            _markers.Clear();
            _todaySignals.Clear();
            _activeSignal   = null;
            _sweepLevel     = string.Empty;
            _statusMessage  = "New trading day — determining bias…";
            _nextCondition  = string.Empty;
            _lastEvent      = string.Empty;
            _lastEventTime  = string.Empty;

            _logger.LogInformation("TradingSignalEngine reset for {Date}.", today);
        }

        private void ApplyBias(DailyBias bias)
        {
            switch (bias)
            {
                case DailyBias.Bullish:
                    _state         = SignalEngineState.BullishWaitingForSweep;
                    _statusMessage = "Bullish bias — scanning for liquidity sweep";
                    _nextCondition = "Waiting for price to sweep below PDH / PDC / PDL";
                    break;

                case DailyBias.Bearish:
                    _state         = SignalEngineState.BearishWaitingForSweep;
                    _statusMessage = "Bearish bias — scanning for liquidity sweep";
                    _nextCondition = "Waiting for price to sweep above PDH / PDC / PDL";
                    break;

                default:
                    _state         = SignalEngineState.Neutral;
                    _statusMessage = "Neutral bias — no signals today";
                    _nextCondition = string.Empty;
                    break;
            }
        }

        private static bool TryParseIst(string isoStr, out DateTimeOffset result)
        {
            return DateTimeOffset.TryParse(isoStr, out result);
        }

        /// <summary>
        /// Convert an ISO 8601 IST string to the same fake-UTC Unix seconds used by the chart JS.
        /// Strips the offset and treats local time as UTC.
        /// </summary>
        private static long ToDisplaySeconds(string isoStr)
        {
            try
            {
                // "2026-10-08T09:15:00+05:30" → strip to "2026-10-08T09:15:00"
                var local = isoStr.Length >= 19 ? isoStr[..19] : isoStr;
                var dt    = DateTime.Parse(local, null, System.Globalization.DateTimeStyles.None);
                return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
            }
            catch
            {
                return 0;
            }
        }
    }
}
