using Microsoft.Extensions.Options;
using Test.Models.Signal;
using Test.Services;
using Test.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// ── MVC ─────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

// ── In-memory cache (daily bias + levels cached per trading date) ───────────
builder.Services.AddMemoryCache();

// ── Named HttpClient for Groww API ──────────────────────────────────────────
builder.Services.AddHttpClient("GrowwClient", client =>
{
    client.DefaultRequestHeaders.Add(
        "User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(20);
});

// ── Strategy options (bound from appsettings.json → "TradingStrategy") ──────
builder.Services.Configure<TradingStrategyOptions>(
    builder.Configuration.GetSection(TradingStrategyOptions.SectionName));

// ── Live chart services ───────────────────────────────────────────────────────
builder.Services.AddScoped<IMarketDataProvider,    GrowwMarketDataProvider>();
builder.Services.AddScoped<INiftyMarketService,    NiftyMarketService>();

// ── Signal engine services ────────────────────────────────────────────────────
// Scoped: per-request (bias fetched once per poll, cached internally by IMemoryCache)
builder.Services.AddScoped<INiftyDailyBiasService, NiftyDailyBiasService>();

// Singleton: persists state across all HTTP requests for the lifetime of the app
builder.Services.AddSingleton<ITradingSignalEngine, TradingSignalEngine>();

// Singleton: accumulates daily signal history (up to 30 trading days in memory)
builder.Services.AddSingleton<ISignalHistoryService, SignalHistoryService>();

// ── Build & Configure pipeline ───────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
