// Forecast and budget section: runs forecasts and chooses the budget
import { chooseBudget as saveBudgetChoice, getLatestForecast, runForecasts } from "./api.js";
import { changeClass, numberFormat, percentChange, randFormat, yearsText } from "./format.js";
import { METHOD_NAMES, compareRuns, describeMethod, fyStartDate } from "./forecast.js";
import { describeSources } from "./importer.js";
import { can, getCategory, indicatorWarnings, on, reloadBudget, showRoleNote, state, subtotalName } from "./store.js";
import { showToast } from "./toast.js";
import { categoryChips, changeCell, checksHtml, columnHead, figureItem, headingRow, html, raw, teamTag } from "./ui.js";

const CHART_JS_URL = "https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js";
const CHART_JS_INTEGRITY = "sha384-9nhczxUqK87bcKHh20fSQcTGD4qq5GhayNYSYWqwBkINBhOfQLg/P5HG5lF1urn4";
const METHOD_ORDER = ["moving_average", "exponential_smoothing"];
const BUDGET_OPTIONS = [...METHOD_ORDER, "combined"];
const METHOD_CLASS = { moving_average: "ma", exponential_smoothing: "es", combined: "avg" };
const DEFAULT_FORECAST_SETTINGS = { windowSize: 3, alpha: 0.5, horizon: 1 };

const fc = {
  comparison: null,
  warnings: [],
  chartKey: "cat:revenue",
  chosen: null,
  chart: null,
  running: false,
};

function compactRand(value) {
  const abs = Math.abs(value);
  if (abs >= 1e6) return `R${(value / 1e6).toFixed(1)}m`;
  if (abs >= 1e3) return `R${Math.round(value / 1e3)}k`;
  return `R${Math.round(value)}`;
}

function seriesFor(key) {
  const [type, code] = key.split(":");
  const category = type === "cat" ? getCategory(code) : null;
  const pick = (source) => (category ? source.totals[code] : source.subtotals[code]);
  return {
    name: category ? category.name : subtotalName(code),
    kind: category ? category.kind : "total",
    history: pick(state.processed),
    forecasts: Object.fromEntries(BUDGET_OPTIONS.map((m) => [m, pick(fc.comparison.methods[m])])),
  };
}

function warningsFor(categoryCode) {
  return fc.warnings.filter((w) => w.triggered && w.categories.includes(categoryCode));
}

function readSettings() {
  const form = document.getElementById("fcSettings");
  return {
    windowSize: Number(form.querySelector('input[name="fcWindow"]:checked').value),
    alpha: Number(document.getElementById("fcAlpha").value),
    horizon: Number(form.querySelector('input[name="fcHorizon"]:checked').value),
  };
}

function writeSettings(settings) {
  const form = document.getElementById("fcSettings");
  form.querySelector(`input[name="fcWindow"][value="${settings.windowSize}"]`).checked = true;
  form.querySelector(`input[name="fcHorizon"][value="${settings.horizon}"]`).checked = true;
  document.getElementById("fcAlpha").value = settings.alpha;
}

function settingsOfComparison() {
  const c = fc.comparison;
  return c ? { windowSize: c.windowSize, alpha: c.alpha, horizon: c.horizon } : null;
}

function isStale() {
  const shown = settingsOfComparison();
  const now = readSettings();
  return Boolean(shown) && Object.keys(now).some((key) => shown[key] !== now[key]);
}

function renderExplanations() {
  const { windowSize, alpha } = readSettings();
  const n = state.processed?.years.length ?? 5;
  const weight = (k) => Math.round(alpha * Math.pow(1 - alpha, k) * 100);
  document.getElementById("fcAlphaOut").textContent = alpha.toFixed(1);

  document.getElementById("fcMaExplain").textContent =
    `Adds up the last ${windowSize} years and divides by ${windowSize}. Each of those years counts the same and older years are ignored. ` +
    "Steady and easy to check, but slow to catch up when figures keep rising.";

  document.getElementById("fcEsExplain").textContent =
    `A weighted average of all ${n} years where recent years count more. With α ${alpha.toFixed(1)}, the latest year counts about ` +
    `${weight(0)}%, the year before ${weight(1)}%, then ${weight(2)}%, and so on. ` +
    "A higher α reacts faster to recent changes; a lower α is steadier.";
}

function refreshStale() {
  const stale = isStale();
  document.getElementById("fcStale").hidden = !stale;
  document.getElementById("fcRunBtn").disabled = fc.running || !state.processed;
  updateChooseState();
}

function setRunning(running) {
  fc.running = running;
  const btn = document.getElementById("fcRunBtn");
  btn.classList.toggle("busy", running);
  btn.querySelector("i").className = running ? "fas fa-spinner fa-spin" : "fas fa-play";
  btn.querySelector("span").textContent = running ? "Running…" : "Run forecasts";
  document.getElementById("view-forecast").classList.toggle("fc-updating", running);
  refreshStale();
}

async function runForecast(announce = true) {
  if (fc.running || !state.processed) return;
  setRunning(true);
  try {
    applyRuns(await runForecasts(readSettings()));
    if (announce) showToast(`Forecasts updated: both methods ran on ${yearsText(state.processed.years)}.`, "success");
  } catch (err) {
    showToast(err.message, "error", 6000);
  } finally {
    setRunning(false);
  }
}

function applyRuns(pair) {
  fc.comparison = compareRuns(pair);
  writeSettings(settingsOfComparison());
  if (!fc.chosen && state.budget) fc.chosen = state.budget.method;
  renderAll();
}

function runsMatchHistory(pair) {
  const years = state.processed.years;
  return Array.isArray(pair?.runs) && pair.runs.length === 2 &&
    pair.runs[0].history_end === fyStartDate(years[years.length - 1], state.business.yearEndMonth);
}

function renderAll() {
  renderExplanations();
  renderMeta();
  renderFlatNote();
  renderDataChecks();
  renderTable();
  renderChoices();
  renderChart();
  refreshStale();
}

function renderMeta() {
  const p = state.processed;
  const c = fc.comparison;
  const s = state.submission;
  const origin = s.sources?.length > 1
    ? `combined from ${describeSources(s)}`
    : `from ${s.fileName}, approved by ${s.approvedBy}`;
  document.getElementById("fcHistoryMeta").textContent = `History: ${yearsText(p.years)} (${p.years.length} years) ${origin}`;
  document.getElementById("fcNoteYear").textContent = p.years[p.years.length - 1];
  if (!c) return;

  const flat = c.horizon > 1 ? ", same figure each year" : "";
  document.getElementById("fcTableMeta").textContent =
    `Forecast for ${yearsText(c.years)}${flat} · ${describeMethod(c.methods.moving_average)} vs ${describeMethod(c.methods.exponential_smoothing)}`;
}

function renderFlatNote() {
  const c = fc.comparison;
  const p = state.processed;
  if (!c) return;
  const lastIndex = p.years.length - 1;
  const lastYear = p.years[lastIndex];

  const repeat = c.horizon > 1
    ? `${c.years.slice(1).join(" and ")} repeat the ${c.years[0]} figure`
    : "if you forecast 2 or 3 years, the later years repeat the first";
  const { categories } = state.definitions;
  const below = categories.filter((cat) =>
    METHOD_ORDER.every((m) => c.methods[m].totals[cat.code][0] < p.totals[cat.code][lastIndex])
  ).length;
  const lag = below
    ? ` When figures have been rising, both methods lag behind: ${below} of ${categories.length} categories forecast below ${lastYear}.`
    : "";

  document.getElementById("fcFlatNote").textContent =
    `Both methods forecast one level, not a trend, so ${repeat} (no growth or inflation is added).${lag}`;
}

function renderWarnings() {
  const list = document.getElementById("fcWarnings");
  const triggered = fc.warnings.filter((w) => w.triggered).length;

  const badge = document.getElementById("fcWarnBadge");
  badge.className = `badge ${triggered ? "warning" : "approved"}`;
  badge.textContent = triggered ? `${triggered} warning${triggered === 1 ? "" : "s"}` : "No warnings";

  if (!fc.warnings.length) {
    list.innerHTML = `<li class="indicator-empty"><i class="fas fa-toggle-off"></i>
      <span>Every warning rule is switched off. Turn them on in <a href="#targets">Targets &amp; rules</a>.</span></li>`;
    return;
  }

  list.innerHTML = html`${fc.warnings.map((w) => {
    const status = w.value === null
      ? "No data for your history years"
      : `${w.value.toFixed(1)}% in ${w.period}, ${w.triggered ? "above" : "within"} the ${w.threshold}% threshold`;

    return html`<li class="indicator ${w.triggered ? "warn" : "ok"}">
      <div class="indicator-head">
        <i class="fas ${w.triggered ? "fa-triangle-exclamation" : "fa-circle-check"}"></i>
        <strong>${w.indicatorName}</strong>
      </div>
      <p class="indicator-value">${status}</p>
      <div class="indicator-cats"><span>${w.triggered ? "Review:" : "Affects:"}</span>${categoryChips(w.categories)}</div>
    </li>`;
  })}`;
}

function renderDataChecks() {
  const list = document.getElementById("fcDataChecks");
  const items = fc.comparison?.warnings ?? [];
  list.hidden = items.length === 0;
  list.innerHTML = checksHtml(items.map((text) => ({ level: "warning", text })));
}

function forecastCell(value, base, kind, method) {
  const pct = percentChange(value, base);
  const chosen = method === fc.chosen ? " col-chosen" : "";
  return changeCell(numberFormat.format(value), pct, `${changeClass(pct, kind)}${chosen}`);
}

function comparisonRow(label, kind, history, forecasts, extra = "") {
  const last = history[history.length - 1];
  return html`<td>${label}${extra}</td>
    <td class="num col-actual">${numberFormat.format(last)}</td>
    ${BUDGET_OPTIONS.map((m) => forecastCell(forecasts[m][0], last, kind, m))}`;
}

function renderTable() {
  const c = fc.comparison;
  const p = state.processed;
  const lastYear = p.years[p.years.length - 1];
  const forecastYears = `${yearsText(c.years)}${c.horizon > 1 ? " · each year" : ""}`;

  const header = html`<thead><tr>
    <th>Category (R)</th>
    ${columnHead(lastYear, "Actual", "col-actual")}
    ${columnHead(raw('<span class="swatch swatch-ma"></span>Moving Average'), forecastYears)}
    ${columnHead(raw('<span class="swatch swatch-es"></span>Exp. Smoothing'), forecastYears)}
    ${columnHead(raw('<span class="swatch swatch-avg"></span>Both (average)'), "(MA + ES) ÷ 2")}
  </tr></thead>`;

  const rows = state.definitions.layout.map((item) => {
    if (item.heading) return headingRow(item.heading, 5);

    const key = item.subtotal ? `sub:${item.subtotal}` : `cat:${item.category}`;
    const s = seriesFor(key);
    const selected = key === fc.chartKey ? " row-selected" : "";

    if (item.subtotal) {
      return html`<tr class="row-subtotal${selected}" data-key="${key}" tabindex="0">
        ${comparisonRow(s.name, "total", s.history, s.forecasts)}</tr>`;
    }

    const category = getCategory(item.category);
    const team = category.teamManaged ? teamTag("Set by the business itself; no external indicator applies") : "";
    const warns = warningsFor(item.category).map((w) => html`<span class="fc-warn" title="${`${w.indicatorName}: ${w.value.toFixed(1)}% in ${w.period} (threshold ${w.threshold}%). The figures are not changed.`}">
        <i class="fas fa-triangle-exclamation"></i>${w.indicatorName} ${w.value.toFixed(1)}%</span>`);
    const label = html`<span class="cat-name">${category.name}</span>${team}`;
    const extra = warns.length ? html`<span class="fc-warns">${warns}</span>` : "";

    return html`<tr class="row-line${warns.length ? " row-warned" : ""}${selected}" data-key="${key}" tabindex="0">
      ${comparisonRow(label, category.kind, s.history, s.forecasts, extra)}</tr>`;
  });

  document.getElementById("fcTable").innerHTML = html`${header}<tbody>${rows}</tbody>`;
}

function isCurrentBudget(m) {
  const b = state.budget;
  if (!b || b.outOfDate || b.method !== m.method || b.horizon !== m.horizon) return false;
  const sameWindow = b.parameters.window_size === m.parameters.window_size;
  const sameAlpha = b.parameters.alpha === m.parameters.alpha;
  if (m.method === "moving_average") return sameWindow;
  if (m.method === "exponential_smoothing") return sameAlpha;
  return sameWindow && sameAlpha;
}

function renderCurrentBudget() {
  const b = state.budget;
  const el = document.getElementById("fcCurrentBudget");
  document.getElementById("fcBudgetBadge").hidden = !b?.outOfDate;
  if (!b) {
    el.textContent = "No budget chosen yet.";
    return;
  }
  if (b.outOfDate) {
    const lastYear = state.processed.years[state.processed.years.length - 1];
    el.textContent = `${describeMethod(b)} for ${yearsText(b.years)} is out of date: the approved history now runs to ${lastYear}. Choose a new budget below.`;
    return;
  }
  if (b.status === "rejected") {
    el.textContent = `${describeMethod(b)} for ${yearsText(b.years)} was rejected by ${b.decidedBy}: "${b.comment}" Choose again below.`;
    return;
  }
  const status = b.status === "approved"
    ? `approved by ${b.approvedBy}`
    : `chosen by ${b.chosenBy || "you"}, waiting for approval`;
  el.textContent = `Current budget: ${describeMethod(b)} for ${yearsText(b.years)}, ${status}.`;
}

function renderChoices() {
  const c = fc.comparison;
  const p = state.processed;
  const last = p.years.length - 1;
  renderCurrentBudget();

  const actualExpenses = p.totalExpenses[last];

  document.getElementById("fcChoices").innerHTML = html`${BUDGET_OPTIONS.map((method) => {
    const m = c.methods[method];
    const selected = fc.chosen === method;
    let current = "";
    if (isCurrentBudget(m)) {
      current = state.budget.status === "rejected"
        ? raw('<span class="badge rejected">Rejected</span>')
        : raw('<span class="badge approved">Current budget</span>');
    }
    const setting = method === "combined"
      ? "(MA + ES) ÷ 2, one number from both"
      : describeMethod(m).replace(`${METHOD_NAMES[method]} (`, "").replace(/\)$/, "");
    return html`<label class="choice-card ${METHOD_CLASS[method]}${selected ? " selected" : ""}">
      <input type="radio" name="fcMethod" value="${method}"${selected ? " checked" : ""} />
      <span class="choice-head">
        <span class="swatch swatch-${METHOD_CLASS[method]}"></span>
        <strong>${METHOD_NAMES[method]}</strong>
        ${current}
        <i class="fas fa-circle-check choice-tick"></i>
      </span>
      <span class="choice-setting">${setting} · ${yearsText(c.years)}${c.horizon > 1 ? " (same each year)" : ""}</span>
      <dl class="choice-figures">
        ${figureItem("Revenue", m.totals.revenue[0], p.totals.revenue[last], "income")}
        ${figureItem("Total expenses", m.totalExpenses[0], actualExpenses, "expense")}
        ${figureItem("Net profit", m.subtotals.net_profit[0], p.subtotals.net_profit[last], "total")}
      </dl>
    </label>`;
  })}`;

  updateChooseState();
}

function updateChooseState() {
  const btn = document.getElementById("fcChooseBtn");
  const note = document.getElementById("fcChooseNote");
  const c = fc.comparison;
  if (!c || btn.classList.contains("busy")) return;

  const m = fc.chosen ? c.methods[fc.chosen] : null;
  const blocked = [
    [!can("chooseBudget"), "Only a Decision Maker can choose the budget. You can still pick an option to highlight it in the table."],
    [isStale(), "Run the forecast with your new settings first, or set them back."],
    [!m, "Pick the forecast that looks most realistic to you, or both for their average."],
    [m && isCurrentBudget(m) && state.budget.status !== "rejected", "This option is already your budget."],
  ].find(([when]) => when);
  note.textContent = blocked
    ? blocked[1]
    : `The % is the change from ${state.processed.years[state.processed.years.length - 1]}'s actual. The dashboard always shows both forecasts; your choice sets its Budget column and goes to an approver.`;
  btn.disabled = Boolean(blocked);
}

function loadChartJs() {
  if (window.Chart) return Promise.resolve(window.Chart);
  return new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = CHART_JS_URL;
    script.integrity = CHART_JS_INTEGRITY;
    script.crossOrigin = "anonymous";
    script.onload = () => resolve(window.Chart);
    script.onerror = () => reject(new Error("Chart.js could not load"));
    document.head.appendChild(script);
  });
}

function renderChartSelect() {
  const option = (value, label) => html`<option value="${value}"${value === fc.chartKey ? " selected" : ""}>${label}</option>`;
  document.getElementById("fcChartSelect").innerHTML =
    html`<optgroup label="Categories">${state.definitions.categories.map((c) => option(`cat:${c.code}`, c.name))}</optgroup>` +
    html`<optgroup label="Totals">${state.definitions.subtotals.map((s) => option(`sub:${s.code}`, s.name))}</optgroup>`;
}

async function renderChart() {
  const c = fc.comparison;
  const p = state.processed;
  const s = seriesFor(fc.chartKey);
  const box = document.getElementById("fcChartBox");

  document.getElementById("fcChartSelect").value = fc.chartKey;
  const warnText = fc.chartKey.startsWith("cat:") && warningsFor(fc.chartKey.slice(4)).length ? " · has an economic warning" : "";
  document.getElementById("fcChartMeta").textContent =
    `${s.name}: ${yearsText(p.years)} actual, ${yearsText(c.years)} forecast${warnText}`;

  let Chart;
  try {
    Chart = await loadChartJs();
  } catch {
    box.classList.remove("skeleton");
    document.getElementById("fcChart").hidden = true;
    document.getElementById("fcChartError").hidden = false;
    return;
  }

  const css = getComputedStyle(document.documentElement);
  const color = (name) => css.getPropertyValue(name).trim();

  const n = p.years.length;
  const pad = new Array(n - 1).fill(null);
  const last = s.history[n - 1];
  const forecastData = (m) => [...pad, last, ...s.forecasts[m]];
  const settingOf = (m) => describeMethod(c.methods[m]).replace(METHOD_NAMES[m], "").trim();
  const lineStyle = {
    moving_average: { color: "--accent", dash: [7, 5], point: "rectRot", width: 2.5 },
    exponential_smoothing: { color: "--accent-light", dash: [7, 5], point: "circle", width: 2.5 },
    combined: { color: "--text-muted", dash: [2, 4], point: "triangle", width: 2 },
  };

  const data = {
    labels: [...p.years, ...c.years],
    datasets: [
      {
        label: "Actual",
        data: [...s.history, ...new Array(c.years.length).fill(null)],
        borderColor: color("--text"),
        backgroundColor: color("--text"),
        borderWidth: 2.5,
        pointRadius: 4,
        tension: 0.25,
      },
      ...BUDGET_OPTIONS.map((m) => ({
        label: m === "combined" ? METHOD_NAMES[m] : `${METHOD_NAMES[m]} ${settingOf(m)}`,
        data: forecastData(m),
        borderColor: color(lineStyle[m].color),
        backgroundColor: color(lineStyle[m].color),
        borderWidth: lineStyle[m].width,
        borderDash: lineStyle[m].dash,
        pointRadius: (ctx) => (ctx.dataIndex < n ? 0 : 5),
        pointStyle: lineStyle[m].point,
      })),
    ],
  };

  box.classList.remove("skeleton");
  if (fc.chart) {
    fc.chart.data = data;
    fc.chart.update();
    return;
  }

  Chart.defaults.font.family = "Poppins, sans-serif";
  Chart.defaults.color = color("--text-muted");
  fc.chart = new Chart(document.getElementById("fcChart"), {
    type: "line",
    data,
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { mode: "index", intersect: false },
      plugins: {
        legend: { position: "bottom", labels: { usePointStyle: true, boxWidth: 8, padding: 16 } },
        tooltip: {
          backgroundColor: color("--text"),
          padding: 10,
          filter: (item) => item.datasetIndex === 0 || item.dataIndex >= state.processed.years.length,
          callbacks: { label: (item) => ` ${item.dataset.label}: ${randFormat.format(item.parsed.y)}` },
        },
      },
      scales: {
        x: { grid: { display: false } },
        y: {
          grid: { color: color("--border") },
          ticks: { callback: (value) => compactRand(value) },
        },
      },
    },
  });
}

function showOnChart(key, scroll) {
  fc.chartKey = key;
  document.querySelectorAll("#fcTable tbody tr[data-key]").forEach((tr) => {
    tr.classList.toggle("row-selected", tr.dataset.key === key);
  });
  renderChart();

  const panel = document.getElementById("fcChartPanel");
  if (scroll && panel.getBoundingClientRect().bottom < 120) {
    panel.scrollIntoView({ behavior: "smooth", block: "start" });
  }
}

async function chooseBudget() {
  if (!can("chooseBudget")) return;
  const { methods } = fc.comparison;
  const m = methods[fc.chosen];
  const btn = document.getElementById("fcChooseBtn");
  btn.disabled = true;
  btn.classList.add("busy");
  btn.querySelector("i").className = "fas fa-spinner fa-spin";

  try {
    await saveBudgetChoice(fc.chosen, [methods.moving_average.runId, methods.exponential_smoothing.runId]);
    await reloadBudget();
    btn.classList.remove("busy");
    renderChoices();
    showToast(`${describeMethod(m)} is now your budget for ${yearsText(fc.comparison.years)}. Sent for approval.`, "success", 5000);
  } catch (err) {
    btn.classList.remove("busy");
    showToast(err.message, "error");
    updateChooseState();
  } finally {
    btn.querySelector("i").className = "fas fa-check";
  }
}

function onRulesChanged() {
  if (!state.processed) return;
  fc.warnings = indicatorWarnings(true);
  renderWarnings();
  if (fc.comparison) renderTable();
}

export function setup() {
  showRoleNote("fcRoleNote", "chooseBudget", "you can run and compare forecasts, but only a Decision Maker can choose the budget.");
  writeSettings(DEFAULT_FORECAST_SETTINGS);
  renderExplanations();
  renderChartSelect();
  on("rules", onRulesChanged);
  on("budget", () => fc.comparison && renderChoices());

  const form = document.getElementById("fcSettings");
  form.addEventListener("input", () => {
    renderExplanations();
    refreshStale();
  });
  form.addEventListener("submit", (e) => {
    e.preventDefault();
    runForecast();
  });

  document.getElementById("fcChartSelect").addEventListener("change", (e) => {
    if (fc.comparison) showOnChart(e.target.value, false);
  });

  const table = document.getElementById("fcTable");
  table.addEventListener("click", (e) => {
    const tr = e.target.closest("tr[data-key]");
    if (tr) showOnChart(tr.dataset.key, true);
  });
  table.addEventListener("keydown", (e) => {
    const tr = e.target.closest("tr[data-key]");
    if (tr && (e.key === "Enter" || e.key === " ")) {
      e.preventDefault();
      showOnChart(tr.dataset.key, true);
    }
  });

  document.getElementById("fcChoices").addEventListener("change", (e) => {
    if (e.target.name !== "fcMethod") return;
    fc.chosen = e.target.value;
    document.querySelectorAll("#fcChoices .choice-card").forEach((card) => {
      card.classList.toggle("selected", card.querySelector("input").checked);
    });
    renderTable();
    updateChooseState();
  });
  document.getElementById("fcChooseBtn").addEventListener("click", chooseBudget);
}

export async function refresh() {
  refreshStale();
  if (!state.processed) {
    document.getElementById("fcHistoryMeta").textContent =
      "No approved history yet: the forecasts run once the first income statement is approved.";
    return;
  }
  renderMeta();
  fc.warnings = indicatorWarnings(true);
  renderWarnings();
  try {
    const pair = await getLatestForecast();
    if (runsMatchHistory(pair)) applyRuns(pair);
    else await runForecast(false);
  } catch (err) {
    showToast(err.message, "error");
  }
}
