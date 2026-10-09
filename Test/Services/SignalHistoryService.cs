using Test.Models.Signal;
using Test.Services.Interfaces;

namespace Test.Services
{
    /// <summary>
    /// Thread-safe in-memory store for daily signal history.
    /// Keeps the last 30 trading days.  Registered as a Singleton.
    /// </summary>
    public class SignalHistoryService : ISignalHistoryService
    {
        private readonly List<DailySignalRecord> _history = new();
        private readonly object _lock = new();
        private const int MaxDaysKept = 30;

        /// <inheritdoc/>
        public void Archive(string date, IReadOnlyList<TradingSignal> signals)
        {
            if (string.IsNullOrWhiteSpace(date) || signals.Count == 0) return;

            lock (_lock)
            {
                // Replace any existing entry for this date
                _history.RemoveAll(r => r.Date == date);
                _history.Add(new DailySignalRecord
                {
                    Date    = date,
                    Signals = signals.ToList()
                });

                // Keep only the most-recent MaxDaysKept days
                if (_history.Count > MaxDaysKept)
                    _history.RemoveAt(0);
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<DailySignalRecord> GetHistory()
        {
            lock (_lock)
                return _history.OrderByDescending(r => r.Date).ToList();
        }
    }
}
