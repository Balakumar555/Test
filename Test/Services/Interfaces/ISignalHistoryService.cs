using Test.Models.Signal;

namespace Test.Services.Interfaces
{
    /// <summary>
    /// Stores and retrieves historical trading signals day-by-day.
    /// Registered as a Singleton so history accumulates for the lifetime of the app.
    /// </summary>
    public interface ISignalHistoryService
    {
        /// <summary>
        /// Archive the signals for a completed trading day.
        /// If an entry for that date already exists it will be replaced.
        /// </summary>
        /// <param name="date">IST date string (yyyy-MM-dd).</param>
        /// <param name="signals">All signals fired on that date.</param>
        void Archive(string date, IReadOnlyList<TradingSignal> signals);

        /// <summary>
        /// Returns all archived days, ordered most-recent first.
        /// </summary>
        IReadOnlyList<DailySignalRecord> GetHistory();
    }
}
