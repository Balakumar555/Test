using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Test.Models.Options;
using Test.Services.Interfaces;

namespace Test.Services
{
    /// <summary>
    /// Calls the Groww FnO live prices batch endpoint for NIFTY options.
    ///
    /// Endpoint : POST https://groww.in/v1/api/stocks_fo_data/v1/tr_live_prices/exchange/NSE/segment/FNO/latest_prices_batch
    /// Payload  : ["NIFTY26O1922850CE", "NIFTY26O1922850PE", ...]
    ///
    /// Symbols are generated dynamically: NIFTY + Expiry + Strike + CE/PE
    /// e.g. Expiry="26O19", Strike=22850 → "NIFTY26O1922850CE"
    /// </summary>
    public class NiftyOptionService : INiftyOptionService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly OptionChainOptions _opts;
        private readonly ILogger<NiftyOptionService> _logger;

        private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

        private const string FnoUrl =
            "https://groww.in/v1/api/stocks_fo_data/v1/tr_live_prices/exchange/NSE/segment/FNO/latest_prices_batch";

        public NiftyOptionService(
            IHttpClientFactory httpFactory,
            IOptions<OptionChainOptions> opts,
            ILogger<NiftyOptionService> logger)
        {
            _httpFactory = httpFactory;
            _opts        = opts.Value;
            _logger      = logger;
        }

        /// <inheritdoc/>
        public async Task<OptionChainViewModel> GetOptionChainAsync(double atmPrice)
        {
            try
            {
                // Round ATM to nearest strike interval
                int interval = _opts.StrikeInterval > 0 ? _opts.StrikeInterval : 50;
                double atm = Math.Round(atmPrice / interval) * interval;

                // Generate all strikes: ATM ± numStrikes
                int n = _opts.NumStrikes > 0 ? _opts.NumStrikes : 6;
                var strikes = Enumerable.Range(-n, 2 * n + 1)
                    .Select(i => atm + i * interval)
                    .OrderBy(s => s)
                    .ToList();

                // Build symbol list
                var expiry  = _opts.Expiry;
                var symbols = strikes
                    .SelectMany(s => new[]
                    {
                        $"NIFTY{expiry}{(int)s}CE",
                        $"NIFTY{expiry}{(int)s}PE"
                    })
                    .ToArray();

                _logger.LogInformation(
                    "Fetching option chain: expiry={Expiry} atm={Atm} strikes={N}",
                    expiry, atm, strikes.Count);

                // POST to Groww FnO API
                var client  = _httpFactory.CreateClient("GrowwClient");
                var json    = JsonSerializer.Serialize(symbols);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(FnoUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("FnO API returned {Status}", response.StatusCode);
                    return Error($"Groww FnO API returned {(int)response.StatusCode}");
                }

                var rawJson = await response.Content.ReadAsStringAsync();
                var priceMap = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (priceMap == null)
                    return Error("Could not parse FnO response.");

                // Build rows
                var rows = strikes.Select(strike =>
                {
                    var ceSym = $"NIFTY{expiry}{(int)strike}CE";
                    var peSym = $"NIFTY{expiry}{(int)strike}PE";

                    var ce = priceMap.GetValueOrDefault(ceSym);
                    var pe = priceMap.GetValueOrDefault(peSym);

                    return new OptionChainRow
                    {
                        Strike       = strike,
                        IsAtm        = Math.Abs(strike - atm) < 0.01,
                        CeSymbol     = ceSym,
                        CeLtp        = GetDouble(ce, "ltp"),
                        CeChange     = GetDouble(ce, "dayChange"),
                        CeChangePct  = GetDouble(ce, "dayChangePerc"),
                        CeOi         = GetDouble(ce, "openInterest"),
                        CeOiChange   = GetDouble(ce, "oiDayChange"),
                        CeOiChangePct= GetDouble(ce, "oiDayChangePerc"),
                        CeVolume     = GetLong(ce,   "volume"),
                        CeBuyQty     = GetLong(ce,   "totalBuyQty"),
                        CeSellQty    = GetLong(ce,   "totalSellQty"),

                        PeSymbol     = peSym,
                        PeLtp        = GetDouble(pe, "ltp"),
                        PeChange     = GetDouble(pe, "dayChange"),
                        PeChangePct  = GetDouble(pe, "dayChangePerc"),
                        PeOi         = GetDouble(pe, "openInterest"),
                        PeOiChange   = GetDouble(pe, "oiDayChange"),
                        PeOiChangePct= GetDouble(pe, "oiDayChangePerc"),
                        PeVolume     = GetLong(pe,   "volume"),
                        PeBuyQty     = GetLong(pe,   "totalBuyQty"),
                        PeSellQty    = GetLong(pe,   "totalSellQty"),
                    };
                }).ToList();

                var nowIst = DateTimeOffset.UtcNow.ToOffset(IstOffset);

                return new OptionChainViewModel
                {
                    Success   = true,
                    Expiry    = expiry,
                    AtmStrike = atm,
                    Rows      = rows,
                    FetchedAt = nowIst.ToString("HH:mm:ss")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching option chain.");
                return Error("Unexpected error fetching option chain.");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static double GetDouble(JsonElement el, string prop)
        {
            if (el.ValueKind == JsonValueKind.Undefined) return 0;
            return el.TryGetProperty(prop, out var v) && v.TryGetDouble(out var d) ? d : 0;
        }

        private static long GetLong(JsonElement el, string prop)
        {
            if (el.ValueKind == JsonValueKind.Undefined) return 0;
            return el.TryGetProperty(prop, out var v) && v.TryGetInt64(out var l) ? l : 0;
        }

        private static OptionChainViewModel Error(string msg) => new()
        {
            Success      = false,
            ErrorMessage = msg
        };
    }
}
