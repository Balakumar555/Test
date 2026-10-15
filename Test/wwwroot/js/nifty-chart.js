/**
 * nifty-chart.js  –  NIFTY 50 live signal dashboard
 * TradingView Lightweight Charts v4 for the candlestick chart.
 * Polls /Nifty/GetSignalData every 30 s.
 */

'use strict';

// ─── Constants ────────────────────────────────────────────────────────────────
const SIGNAL_URL  = '/Nifty/GetSignalData';
const REFRESH_MS  = 30_000;

const COLOR_UP    = '#26a69a';
const COLOR_DOWN  = '#ef5350';
const COLOR_PDH   = '#ef5350';
const COLOR_PDL   = '#26a69a';
const COLOR_PDC   = '#ffc107';
const COLOR_ORH   = '#a78bfa';   // purple  – Opening Range High
const COLOR_ORL   = '#60a5fa';   // blue    – Opening Range Low
const COLOR_BG    = '#16181f';
const COLOR_GRID  = 'rgba(255,255,255,0.06)';
const COLOR_TEXT  = '#aaaaaa';

// ─── Chart state ──────────────────────────────────────────────────────────────
let lwChart      = null;
let candleSeries = null;
const priceLineMap = {};
let previousClose = null;

// ─── DOM ready ────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    initChart();
    refresh();
    setInterval(refresh, REFRESH_MS);
});

// ─── Chart initialisation ─────────────────────────────────────────────────────
function initChart() {
    const container = document.getElementById('chartContainer');
    if (!container) return;

    lwChart = LightweightCharts.createChart(container, {
        width:  container.clientWidth,
        height: container.clientHeight,
        layout: {
            background: { type: 'solid', color: COLOR_BG },
            textColor:  COLOR_TEXT,
            fontSize:   12
        },
        grid: {
            vertLines: { color: COLOR_GRID },
            horzLines: { color: COLOR_GRID }
        },
        rightPriceScale: {
            borderColor:  '#2a2d3a',
            scaleMargins: { top: 0.1, bottom: 0.1 }
        },
        timeScale: {
            borderColor:    '#2a2d3a',
            timeVisible:    true,
            secondsVisible: false,
            rightOffset:    5,
            barSpacing:     8,
            minBarSpacing:  4
        },
        crosshair: { mode: LightweightCharts.CrosshairMode.Normal },
        handleScroll: { mouseWheel: true, pressedMouseMove: true },
        handleScale:  { mouseWheel: true, pinch: true }
    });

    candleSeries = lwChart.addCandlestickSeries({
        upColor:        COLOR_UP,
        downColor:      COLOR_DOWN,
        borderUpColor:  COLOR_UP,
        borderDownColor: COLOR_DOWN,
        wickUpColor:    COLOR_UP,
        wickDownColor:  COLOR_DOWN
    });

    new ResizeObserver(() => {
        if (lwChart && container) {
            lwChart.applyOptions({ width: container.clientWidth, height: container.clientHeight });
        }
    }).observe(container);
}

// ─── Main refresh loop ────────────────────────────────────────────────────────
async function refresh() {
    pulseRefreshDot();
    try {
        const resp = await fetch(SIGNAL_URL, { cache: 'no-store' });
        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

        const data = await resp.json();

        if (!data.success) {
            showError(data.errorMessage || 'Market data unavailable.');
            return;
        }

        hideError();
        hideLoading();

        updateChart(data);
        updateStatsBar(data);
        updateCurrentPrice(data.currentPrice, data.previousDayClose);
        updateSignalPanel(data);

        setText('lastUpdated', new Date().toLocaleTimeString('en-IN', { hour12: false }));

    } catch (err) {
        console.error('Refresh error:', err);
        showError('Could not reach the server. Will retry in 30 s.');
    }
}

// ─── Chart update ─────────────────────────────────────────────────────────────
function updateChart(data) {
    if (!candleSeries) return;

    const candles = (data.candles || []).map(c => ({
        time:  toDisplayTime(c.time),
        open:  c.open,
        high:  c.high,
        low:   c.low,
        close: c.close
    }));

    if (candles.length > 0) {
        candleSeries.setData(candles);
    }

    // PDH / PDC / PDL horizontal lines
    updatePriceLine('pdh', data.previousDayHigh,  COLOR_PDH, 'PDH',  LightweightCharts.LineStyle.Solid);
    updatePriceLine('pdc', data.previousDayClose, COLOR_PDC, 'PDC',  LightweightCharts.LineStyle.Dashed);
    updatePriceLine('pdl', data.previousDayLow,   COLOR_PDL, 'PDL',  LightweightCharts.LineStyle.Solid);

    // ORH / ORL lines — only once Opening Range period is locked
    if (data.openingRangeReady) {
        updatePriceLine('orh', data.openingRangeHigh, COLOR_ORH, 'ORH', LightweightCharts.LineStyle.Dashed);
        updatePriceLine('orl', data.openingRangeLow,  COLOR_ORL, 'ORL', LightweightCharts.LineStyle.Dashed);
    }

    // Chart markers (sweep / confirmation / buy / sell)
    updateMarkers(data.markers || []);

    lwChart.timeScale().fitContent();
}

function updatePriceLine(id, price, color, title, lineStyle) {
    if (!price || price === 0) return;
    const opts = { price, color, lineWidth: 1.5, lineStyle, axisLabelVisible: true, title };
    if (priceLineMap[id]) {
        priceLineMap[id].applyOptions(opts);
    } else {
        priceLineMap[id] = candleSeries.createPriceLine(opts);
    }
}

function updateMarkers(markers) {
    if (!candleSeries || !markers.length) return;
    const m = markers.map(mk => ({
        time:     mk.time,
        position: mk.position || 'aboveBar',
        color:    mk.color    || '#ffffff',
        shape:    mk.shape    || 'circle',
        text:     mk.text     || ''
    }));
    candleSeries.setMarkers(m);
}

// ─── Signal panel update ──────────────────────────────────────────────────────
function updateSignalPanel(data) {
    const bias  = (data.dailyBias || 'Neutral').toLowerCase();
    const state = data.signalState || '';

    // Top header bias badge
    const badge = document.getElementById('biasBadge');
    if (badge) {
        badge.textContent = (data.dailyBias || 'Neutral').toUpperCase();
        badge.className   = `bias-badge bias-${bias}`;
    }

    // Side panel bias
    const spBias = document.getElementById('spBias');
    if (spBias) {
        spBias.textContent = (data.dailyBias || 'Neutral').toUpperCase();
        spBias.className   = `sp-bias-${bias}`;
    }

    // Wick data
    setText('spUpperWick', fmt(data.prevDayUpperWick) + ' pts');
    setText('spLowerWick', fmt(data.prevDayLowerWick) + ' pts');

    // PDH / PDC / PDL in panel
    setText('spPDH', fmt(data.previousDayHigh));
    setText('spPDC', fmt(data.previousDayClose));
    setText('spPDL', fmt(data.previousDayLow));

    // Opening Range levels (show section once OR is ready)
    const orSection = document.getElementById('spORSection');
    const orLegend  = document.getElementById('orLegend');
    if (data.openingRangeReady) {
        setText('spORH', fmt(data.openingRangeHigh));
        setText('spORL', fmt(data.openingRangeLow));
        if (orSection) orSection.style.display = '';
        if (orLegend)  orLegend.style.display  = '';
    } else {
        if (orSection) orSection.style.display = 'none';
        if (orLegend)  orLegend.style.display  = 'none';
    }

    // Status
    setText('spStatus',   data.statusMessage  || '—');
    setText('spNextCond', data.nextCondition  || '');
    setText('spLastEvent', data.lastEvent     || '—');
    setText('spLastEventTime', data.lastEventTime ? '⏱ ' + data.lastEventTime : '');

    // Signals counter
    setText('statSignals', `${data.signalsToday} / ${data.maxSignalsPerDay}`);

    // Signal card
    renderSignalCard(data.activeSignal);
}

function renderSignalCard(signal) {
    const card   = document.getElementById('spSignalCard');
    const header = document.getElementById('spSignalCardHeader');
    const body   = document.getElementById('spSignalCardBody');
    if (!card || !header || !body) return;

    if (!signal) {
        card.className   = 'sp-signal-card sp-signal-none';
        header.textContent = 'NO SIGNAL YET';
        body.innerHTML   = '';
        return;
    }

    const isBuy = (signal.type === 'Buy' || signal.type === 0);
    card.className   = `sp-signal-card ${isBuy ? 'sp-signal-buy' : 'sp-signal-sell'}`;
    header.textContent = isBuy ? '🟢 BUY SIGNAL' : '🔴 SELL SIGNAL';

    body.innerHTML = `
        <div class="sp-sig-row"><span>Entry Price</span><strong>${fmt(signal.entryPrice)}</strong></div>
        <div class="sp-sig-row"><span>Time</span><strong>${signal.time}</strong></div>
        <div class="sp-sig-row"><span>Liquidity Level</span><strong>${signal.liquidityLevel}</strong></div>
        <div class="sp-sig-row"><span>Sweep Price</span><strong>${fmt(signal.sweepPrice)}</strong></div>
        <div class="sp-sig-row"><span>Sweep Time</span><strong>${signal.sweepTime}</strong></div>
        <div class="sp-sig-row"><span>Confirmation</span><strong>${signal.confirmationTime}</strong></div>
        <div class="sp-sig-reason">${signal.reason}</div>
    `;
}

// ─── Stats bar ────────────────────────────────────────────────────────────────
function updateStatsBar(data) {
    setText('statPDH',      fmt(data.previousDayHigh));
    setText('statPDC',      fmt(data.previousDayClose));
    setText('statPDL',      fmt(data.previousDayLow));
    setText('statCDH',      fmt(data.currentDayHigh));
    setText('statCDL',      fmt(data.currentDayLow));
    setText('statPrevDate', data.previousTradingDate || '—');
}

function updateCurrentPrice(price, pdClose) {
    const priceEl  = document.getElementById('currentPrice');
    const changeEl = document.getElementById('priceChange');
    if (!priceEl) return;
    priceEl.textContent = fmt(price);
    if (pdClose && pdClose > 0) {
        const diff = price - pdClose;
        const pct  = (diff / pdClose) * 100;
        const sign = diff >= 0 ? '+' : '';
        changeEl.textContent = `${sign}${fmt(diff)} (${sign}${pct.toFixed(2)}%)`;
        changeEl.className   = `nifty-price-change ${diff >= 0 ? 'text-success' : 'text-danger'}`;
    }
    if (previousClose !== null && price !== previousClose) {
        priceEl.classList.remove('price-flash-up', 'price-flash-down');
        void priceEl.offsetWidth;
        priceEl.classList.add(price >= previousClose ? 'price-flash-up' : 'price-flash-down');
    }
    previousClose = price;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────
function toDisplayTime(isoStr) {
    // Strip IST offset → treat local time as UTC for chart display
    return Math.floor(new Date(isoStr.slice(0, 19) + 'Z').getTime() / 1000);
}

function fmt(v) {
    if (!v && v !== 0) return '—';
    return Number(v).toLocaleString('en-IN', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });
}

function setText(id, text) {
    const el = document.getElementById(id);
    if (el) el.textContent = text ?? '';
}

function showError(msg) {
    setText('errorMsg', msg);
    document.getElementById('errorBanner')?.classList.remove('d-none');
}

function hideError() {
    document.getElementById('errorBanner')?.classList.add('d-none');
}

function hideLoading() {
    const el = document.getElementById('chartLoading');
    if (el) el.style.display = 'none';
}

function pulseRefreshDot() {
    const dot = document.getElementById('refreshDot');
    if (!dot) return;
    dot.classList.remove('dot-pulse');
    void dot.offsetWidth;
    dot.classList.add('dot-pulse');
}

// ═══════════════════════════════════════════════════════════════════
// OPTION CHAIN
// Polls GET /Nifty/GetOptionChain every 30 s.
// ATM is derived from the current NIFTY price obtained from signal data.
// ═══════════════════════════════════════════════════════════════════

let _latestAtm = 0;  // updated after each signal-data poll

// Hook into the main refresh: after chart data arrives, fetch option chain
const _origRefresh = refresh;
async function refresh() {
    await _origRefresh.apply(this, arguments);
    await refreshOptionChain();
}

async function refreshOptionChain() {
    const dot = document.getElementById('ocDot');
    if (dot) { dot.classList.remove('dot-pulse'); void dot.offsetWidth; dot.classList.add('dot-pulse'); }

    try {
        const url = _latestAtm > 0
            ? `/Nifty/GetOptionChain?atm=${_latestAtm}`
            : '/Nifty/GetOptionChain';

        const resp = await fetch(url, { cache: 'no-store' });
        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
        const data = await resp.json();

        document.getElementById('ocLoading').style.display = 'none';

        if (!data.success) {
            const errEl = document.getElementById('ocError');
            errEl.textContent = data.errorMessage || 'Option chain unavailable.';
            errEl.classList.remove('d-none');
            return;
        }

        document.getElementById('ocError').classList.add('d-none');
        document.getElementById('ocTableWrap').style.display = 'block';

        // Header meta
        setText('ocExpiry', 'Expiry: ' + (data.expiry || '—'));
        setText('ocAtm',    'ATM: '    + fmtK(data.atmStrike));
        setText('ocTime',   'Updated: ' + (data.fetchedAt || '—'));

        renderOptionChain(data.rows || [], data.atmStrike);

    } catch (err) {
        console.error('Option chain error:', err);
        document.getElementById('ocLoading').style.display = 'none';
    }
}

// Hook: capture current price after each main refresh
const _origUpdateCurrentPrice = updateCurrentPrice;
function updateCurrentPrice(price, pdClose) {
    _origUpdateCurrentPrice(price, pdClose);
    if (price && price > 0) _latestAtm = price;
}

function renderOptionChain(rows, atmStrike) {
    const tbody = document.getElementById('ocBody');
    if (!tbody) return;
    tbody.innerHTML = '';

    rows.forEach(row => {
        const isAtm = row.isAtm;
        const tr = document.createElement('tr');
        if (isAtm) tr.classList.add('oc-atm-row');

        // CE OI change color
        const ceOiCls  = row.ceOiChangePct > 0  ? 'oc-bull' : row.ceOiChangePct < 0  ? 'oc-bear' : '';
        const peOiCls  = row.peOiChangePct > 0  ? 'oc-bull' : row.peOiChangePct < 0  ? 'oc-bear' : '';
        const ceChgCls = row.ceChangePct   > 0  ? 'oc-bull' : row.ceChangePct   < 0  ? 'oc-bear' : '';
        const peChgCls = row.peChangePct   > 0  ? 'oc-bull' : row.peChangePct   < 0  ? 'oc-bear' : '';

        tr.innerHTML =
            // ── CE side (calls) ───────────────────────────────────────────
            `<td class="oc-ce ${ceOiCls}">${fmtPct(row.ceOiChangePct)}</td>` +
            `<td class="oc-ce">${fmtOi(row.ceOi)}</td>` +
            `<td class="oc-ce">${fmtVol(row.ceVolume)}</td>` +
            `<td class="oc-ce ${ceChgCls}">${fmtPct(row.ceChangePct)}</td>` +
            `<td class="oc-ce oc-ltp">${fmt(row.ceLtp)}</td>` +
            // ── Strike ───────────────────────────────────────────────────
            `<td class="oc-strike${isAtm ? ' oc-atm-strike' : ''}">${row.strike}</td>` +
            // ── PE side (puts) ────────────────────────────────────────────
            `<td class="oc-pe oc-ltp">${fmt(row.peLtp)}</td>` +
            `<td class="oc-pe ${peChgCls}">${fmtPct(row.peChangePct)}</td>` +
            `<td class="oc-pe">${fmtVol(row.peVolume)}</td>` +
            `<td class="oc-pe">${fmtOi(row.peOi)}</td>` +
            `<td class="oc-pe ${peOiCls}">${fmtPct(row.peOiChangePct)}</td>`;

        tbody.appendChild(tr);
    });
}

// Format helpers for option chain
function fmtPct(v)  { if (!v && v !== 0) return '—'; const s = v >= 0 ? '+' : ''; return s + v.toFixed(1) + '%'; }
function fmtOi(v)   { if (!v) return '—'; if (v >= 1e5) return (v/1e5).toFixed(1)+'L'; if (v >= 1e3) return (v/1e3).toFixed(1)+'K'; return v.toFixed(0); }
function fmtVol(v)  { if (!v) return '—'; if (v >= 1e6) return (v/1e6).toFixed(1)+'M'; if (v >= 1e3) return (v/1e3).toFixed(0)+'K'; return v; }
function fmtK(v)    { if (!v) return '—'; return Number(v).toLocaleString('en-IN'); }
