// Dashboard section: metric cards, KPI target cards, budget vs actual and both income statements
import { getDashboard } from "./api.js";
import { changeClass, formatKpiValue, formatSheet, numberFormat, randFormat, yearsText } from "./format.js";
import { METHOD_NAMES, describeMethod } from "./forecast.js";
import { describeSources } from "./importer.js";
import { can, getCategory, getKpi, on, state, subtotalName } from "./store.js";
import { showToast } from "./toast.js";
import {
  TG_STATUS, budgetCells, changeCell, chip, columnHead, emptyNote, emptyRow, headingRow, html, kpiFigureHtml, kpiGapHtml, pctCell, raw,
  setTargetBadge, teamTag,
} from "./ui.js";

const KIND_LABELS = { income: "Income", expense: "Expense", total: "Total" };
const METRICS_KEY = "dashboardMetrics";
const DEFAULT_METRICS = ["line:Sales", "line:Rent", "line:Electricity", "total:net_profit"];

const dash = {
  metricOptions: [],
  selectedMetrics: [],
  yearIndex: 0,
  search: "",
  summaryTimer: null,
  summaryToken: 0,
};

function renderYearSelect() {
  const select = document.getElementById("yearSelect");
  select.innerHTML = html`${state.submission.years
    .map((year, i) => html`<option value="${i}"${i === dash.yearIndex ? " selected" : ""}>${year}</option>`)}`;
}

function setupYearPicker() {
  const select = document.getElementById("yearSelect");
  select.addEventListener("change", () => {
    dash.yearIndex = Number(select.value);
    document.querySelectorAll(".metric-card").forEach((card) => updateMetricCard(card, false));
    renderSubmitted();
    refreshDashboardSummary();
  });
}

function buildMetricOptions(submission, processed) {
  const lines = submission.rows
    .filter((row) => row.category)
    .map((row) => ({
      id: `line:${row.label}`,
      label: row.label,
      group: "line",
      kind: getCategory(row.category).kind,
      values: row.values.map((v) => Math.abs(v) * submission.scale),
    }));
  const totals = state.definitions.subtotals.map(({ code, name }) => ({
    id: `total:${code}`,
    label: name,
    group: "total",
    kind: "total",
    values: processed.subtotals[code],
  }));
  return [...lines, ...totals];
}

function loadMetricSelection() {
  const saved = JSON.parse(localStorage.getItem(METRICS_KEY) || "null");
  const valid = (id) => dash.metricOptions.some((o) => o.id === id);
  if (Array.isArray(saved) && saved.length === 4 && saved.every(valid)) return saved;
  return DEFAULT_METRICS.map((id, i) => (valid(id) ? id : dash.metricOptions[i].id));
}

function saveMetricSelection() {
  localStorage.setItem(METRICS_KEY, JSON.stringify(dash.selectedMetrics));
}

function metricOptionsHtml(selectedId) {
  const groups = [["Income statement lines", "line"], ["Totals", "total"]];
  return html`${groups.map(([label, group]) => html`<optgroup label="${label}">${dash.metricOptions
    .filter((o) => o.group === group)
    .map((o) => html`<option value="${o.id}"${o.id === selectedId ? " selected" : ""}>${o.label}</option>`)}</optgroup>`)}`;
}

function renderMetrics() {
  const wrap = document.getElementById("metrics");
  wrap.innerHTML = "";

  dash.selectedMetrics.forEach((id, slot) => {
    const card = document.createElement("div");
    card.className = "metric-card";
    card.dataset.slot = slot;
    card.innerHTML = html`
      <div class="select-wrap">
        <select class="metric-select" aria-label="Metric ${slot + 1}">${metricOptionsHtml(id)}</select>
      </div>
      <div class="metric-value">R 0</div>
      <div class="metric-change"></div>
      <div class="metric-kind"></div>`;

    card.querySelector("select").addEventListener("change", (e) => {
      dash.selectedMetrics[slot] = e.target.value;
      saveMetricSelection();
      updateMetricCard(card, true);
    });

    wrap.appendChild(card);
    updateMetricCard(card, true);
  });
}

function updateMetricCard(card, fromZero) {
  const option = dash.metricOptions.find((o) => o.id === dash.selectedMetrics[card.dataset.slot]);
  const i = dash.yearIndex;
  const { years } = state.submission;
  const value = option.values[i];

  card.dataset.kind = option.kind;

  const valueEl = card.querySelector(".metric-value");
  animateValue(valueEl, fromZero ? 0 : Number(valueEl.dataset.value) || 0, value);

  const changeEl = card.querySelector(".metric-change");
  if (i === 0) {
    changeEl.className = "metric-change neutral";
    changeEl.textContent = "No earlier year to compare";
  } else {
    const previous = option.values[i - 1];
    const pct = previous === 0 ? 0 : ((value - previous) / Math.abs(previous)) * 100;
    const up = pct >= 0;
    const good = option.kind === "expense" ? !up : up;
    changeEl.className = `metric-change ${good ? "good" : "bad"}`;
    changeEl.innerHTML = html`<i class="fas fa-arrow-${up ? "up" : "down"}"></i> ${Math.abs(pct).toFixed(1)}% vs ${years[i - 1]}`;
  }

  card.querySelector(".metric-kind").textContent = `${KIND_LABELS[option.kind]} · ${years[i]}`;

  card.classList.remove("pulse");
  void card.offsetWidth;
  card.classList.add("pulse");
}

function animateValue(el, from, to, duration = 700) {
  el.dataset.value = to;
  const token = (Number(el.dataset.token) || 0) + 1;
  el.dataset.token = token;
  const start = performance.now();

  const step = (now) => {
    if (Number(el.dataset.token) !== token) return;
    const t = Math.min((now - start) / duration, 1);
    const eased = 1 - Math.pow(1 - t, 3);
    el.textContent = randFormat.format(from + (to - from) * eased);
    if (t < 1) requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
}

function selectedCol(i) {
  return i === dash.yearIndex ? " col-selected" : "";
}

function yearHeaderCells(sources) {
  return html`${state.submission.years.map((year, i) => {
    const source = sources?.find((s) => s.years.includes(year));
    const title = source ? html` title="From ${source.fileName}"` : "";
    return html`<th class="num${selectedCol(i)}"${title}>${year}</th>`;
  })}`;
}

function valueCells(values, format) {
  return html`${values.map((v, i) => html`<td class="num${selectedCol(i)}">${format(v)}</td>`)}`;
}

function scrollToSelectedYear(table) {
  const wrap = table.parentElement;
  const cell = table.querySelector("th.col-selected");
  if (!cell || wrap.scrollWidth <= wrap.clientWidth) return;
  const target = cell.offsetLeft + cell.offsetWidth - wrap.clientWidth + 16;
  wrap.scrollTo({ left: Math.max(0, target), behavior: "smooth" });
}

function renderSubmitted() {
  const s = state.submission;
  const cols = s.years.length + 1;

  const combined = s.sources?.length > 1;
  document.getElementById("submittedMeta").textContent = combined
    ? `Combined from ${s.sources.length} uploads: ${describeSources(s)}`
    : `${s.fileName} · uploaded by ${s.submittedBy}`;
  const badge = document.getElementById("submittedBadge");
  badge.hidden = false;
  badge.className = `badge ${s.status}`;
  badge.textContent = s.status.charAt(0).toUpperCase() + s.status.slice(1);
  if (s.approvedBy) badge.title = `${combined ? "Newest upload approved" : "Approved"} by ${s.approvedBy} on ${s.approvedOn}`;

  const rows = s.rows.map((row) => {
    if (row.heading) return headingRow(row.heading, cols);
    const cls = row.subtotal ? "row-subtotal" : "row-line";
    return html`<tr class="${cls}" data-search="${row.label.toLowerCase()}">
        <td>${row.label}</td>${valueCells(row.values, formatSheet)}</tr>`;
  });

  const table = document.getElementById("submittedTable");
  table.innerHTML = html`<thead><tr><th>Line (as in file)</th>${yearHeaderCells(combined ? s.sources : null)}</tr></thead><tbody>${rows}</tbody>`;
  applySearch();
  scrollToSelectedYear(table);
}

function renderProcessed() {
  const p = state.processed;
  const b = state.budget;
  const lastYear = p.years[p.years.length - 1];
  document.getElementById("processedBadge").hidden = false;
  if (!b) {
    renderNoBudget(lastYear);
    return;
  }
  const budgetYears = yearsText(b.years);
  const eachYear = b.years.length > 1 ? " · each year" : "";

  document.getElementById("processedMeta").textContent = b.outOfDate
    ? `Budget ${budgetYears} · ${describeMethod(b)} · out of date: the approved history now runs to ${lastYear}. Choose a new budget in Forecast & budget.`
    : `Budget ${budgetYears} · ${describeMethod(b)}`;
  const badge = document.getElementById("processedBadge");
  badge.className = `badge ${b.outOfDate ? "warning" : b.status}`;
  badge.textContent = b.outOfDate ? "Out of date" : {
    approved: "Approved budget",
    submitted: "Waiting for approval",
    rejected: "Budget rejected",
  }[b.status] || "Draft budget";
  if (b.status === "rejected") badge.title = `Rejected by ${b.decidedBy} on ${b.decidedOn}: ${b.comment}`;
  else if (b.approvedBy) badge.title = `Approved by ${b.approvedBy} on ${b.approvedOn}`;
  else if (b.chosenBy) badge.title = `Chosen by ${b.chosenBy} on ${b.chosenOn}`;
  document.getElementById("processedNoteYear").textContent = lastYear;

  const rows = state.definitions.layout.map((item) => {
    if (item.heading) return headingRow(item.heading, 5);

    if (item.subtotal) {
      const name = subtotalName(item.subtotal);
      return html`<tr class="row-subtotal" data-search="${name.toLowerCase()}">
        <td>${name}</td>${budgetCells(b, p.subtotals[item.subtotal], (f) => f.subtotals[item.subtotal], "total")}</tr>`;
    }

    const category = getCategory(item.category);
    const sources = p.sources[item.category];
    const sourceText = sources.length ? `from: ${sources.join(", ")}` : "no lines in this upload";
    const searchText = `${category.name} ${sources.join(" ")}`.toLowerCase();

    return html`<tr class="row-line${sources.length ? "" : " row-empty-category"}" data-search="${searchText}">
      <td>
        <span class="cat-name">${category.name}</span>${category.teamManaged ? teamTag() : ""}
        <small class="cat-sources">${sourceText}</small>
      </td>${budgetCells(b, p.totals[item.category], (f) => f.totals[item.category], category.kind)}</tr>`;
  });

  const header = html`<th>Category (R)</th>
    ${columnHead(lastYear, "Actual", "col-actual")}
    ${columnHead("MA", budgetYears, "col-method")}
    ${columnHead("ES", budgetYears, "col-method")}
    ${columnHead("Budget", `${budgetYears}${eachYear}`, "col-budget")}`;

  const table = document.getElementById("processedTable");
  table.innerHTML = html`<thead><tr>${header}</tr></thead><tbody>${rows}</tbody>`;
  applySearch();
  refreshDashboardSummary();
}

function renderNoBudget(lastYear) {
  document.getElementById("processedMeta").textContent = "No budget chosen yet";
  const badge = document.getElementById("processedBadge");
  badge.className = "badge draft";
  badge.textContent = "No budget";
  badge.removeAttribute("title");
  document.getElementById("processedNoteYear").textContent = lastYear;
  document.getElementById("processedTable").innerHTML =
    emptyRow("Run the forecasts and choose a budget in Forecast & budget to see it here.");
  refreshDashboardSummary();
}

function targetProgress(t) {
  if (t.value === null) return 0;
  if (t.targetValue <= 0 || t.value <= 0) return t.reached ? 1 : 0;
  const ratio = t.higherIsBetter ? t.value / t.targetValue : t.targetValue / t.value;
  return Math.min(Math.max(ratio, 0), 1);
}

function dashTargetCardHtml(t) {
  const status = TG_STATUS[t.status];
  const way = getKpi(t.metricName) ? (t.higherIsBetter ? "at least" : "at most") : "";
  const figure = t.value !== null
    ? kpiFigureHtml(t.value, t.unit, t.source)
    : raw('<span class="tg-muted">No figures yet</span>');
  const gap = t.value !== null ? kpiGapHtml(t.gap, t.unit, t.reached) : "";

  return html`<div class="dash-kpi ${status.cls}">
    <div class="dash-kpi-head">
      <span class="dash-kpi-name">${t.name}</span>
      <span class="dash-kpi-year">${t.year}</span>
    </div>
    <div class="dash-kpi-figure">${figure}</div>
    <div class="dash-kpi-bar"><span data-fill="${targetProgress(t)}"></span></div>
    <div class="dash-kpi-target">Target ${way} ${formatKpiValue(t.targetValue, t.unit)} ${gap}</div>
    ${chip(status.cls, status.label, status.icon)}
  </div>`;
}

function refreshDashboardSummary() {
  clearTimeout(dash.summaryTimer);
  dash.summaryTimer = setTimeout(loadDashboardSummary, 50);
}

async function loadDashboardSummary() {
  if (!state.processed) return;
  const token = ++dash.summaryToken;
  const year = state.submission.years[dash.yearIndex];
  try {
    const summary = await getDashboard(year);
    if (token !== dash.summaryToken) return;
    renderDashboardTargets(summary.targets);
    renderBudgetCheck(summary);
  } catch (err) {
    if (token === dash.summaryToken) showToast(err.message, "error");
  }
}

function signedRand(value) {
  const sign = value > 0 ? "+" : value < 0 ? "−" : "";
  return `${sign}${numberFormat.format(Math.abs(value))}`;
}

function renderBudgetCheck(summary) {
  const { year, previousYear, budget, lines } = summary;
  document.getElementById("checkTitle").textContent = budget ? `Budget vs actual · ${year}` : `Year on year · ${year}`;
  document.getElementById("checkMeta").textContent = budget
    ? `${METHOD_NAMES[budget.method]} budget, approved by ${budget.approvedBy} on ${budget.approvedOn}`
    : `No approved budget forecast ${year}${previousYear ? `, so it's compared with ${previousYear}` : ""}`;

  const badge = document.getElementById("checkBadge");
  const net = lines.find((l) => l.code === "net_profit");
  badge.hidden = !budget;
  if (budget) {
    badge.className = `badge ${net.variance >= 0 ? "approved" : "rejected"}`;
    badge.textContent = `Net profit ${signedRand(net.variance)} vs budget`;
  }

  const header = html`<th>Category (R)</th>
    ${previousYear ? columnHead(previousYear, "Actual", "col-actual") : ""}
    ${columnHead(year, "Actual")}
    ${previousYear ? columnHead("Change", `vs ${previousYear}`) : ""}
    ${budget ? html`${columnHead(year, "Budget", "col-actual")}
      ${columnHead("Difference", "vs budget", "col-budget")}` : ""}`;

  const rows = lines.map((line) => {
    const before = previousYear ? html`<td class="num col-actual">${numberFormat.format(line.previous)}</td>` : "";
    const change = previousYear ? pctCell(line.change, line.kind) : "";
    const versusBudget = budget
      ? html`<td class="num col-actual">${numberFormat.format(line.budget)}</td>
        ${changeCell(signedRand(line.variance), line.variancePercent, `col-budget ${changeClass(line.variancePercent, line.kind)}`)}`
      : "";
    return html`<tr class="${line.subtotal ? "row-subtotal" : "row-line"}" data-search="${line.name.toLowerCase()}">
      <td><span class="cat-name">${line.name}</span>${line.teamManaged ? teamTag() : ""}</td>
      ${before}<td class="num">${numberFormat.format(line.actual)}</td>${change}${versusBudget}</tr>`;
  });

  const table = document.getElementById("checkTable");
  table.innerHTML = html`<thead><tr>${header}</tr></thead><tbody>${rows}</tbody>`;
  applySearch();
}

function renderDashboardTargets(results) {
  const list = document.getElementById("dashKpiList");
  const badge = document.getElementById("dashKpiBadge");
  const manage = can("editTargets")
    ? raw('Add or change targets in <a href="#targets">Targets &amp; rules</a>.')
    : raw('Targets are set by the Decision Maker in <a href="#targets">Targets &amp; rules</a>.');
  document.getElementById("dashKpiNote").innerHTML =
    html`Years with actual figures are checked against them; future years against the budget. ${manage}`;

  if (!results.length) {
    document.getElementById("dashKpiMeta").textContent = "No targets set yet.";
    badge.hidden = true;
    list.innerHTML = emptyNote("No KPI targets yet.");
    return;
  }

  const years = [...new Set(results.map((r) => r.year))].reverse();
  document.getElementById("dashKpiMeta").textContent =
    `${results.length} target${results.length === 1 ? "" : "s"} for ${yearsText(years)}`;

  const checked = results.filter((r) => r.value !== null);
  badge.hidden = false;
  setTargetBadge(badge, results.length, checked.length, checked.filter((r) => r.reached).length);

  list.innerHTML = html`${results.map(dashTargetCardHtml)}`;
  list.querySelectorAll(".dash-kpi-bar span").forEach((bar) => {
    bar.style.width = `${Number(bar.dataset.fill) * 100}%`;
  });
}

function applySearch() {
  const q = dash.search;

  document.querySelectorAll("#view-dashboard .statement").forEach((table) => {
    const body = table.querySelector("tbody");
    if (!body) return;
    body.querySelectorAll(".row-no-results").forEach((tr) => tr.remove());

    let shown = 0;
    body.querySelectorAll("tr").forEach((tr) => {
      const match = !q || (tr.dataset.search || "").includes(q);
      const hide = Boolean(q) && (!match || tr.classList.contains("row-heading"));
      tr.hidden = hide;
      tr.classList.toggle("row-match", Boolean(q) && !hide);
      if (!hide) shown++;
    });

    if (shown === 0) {
      const cols = table.querySelectorAll("thead th").length;
      body.insertAdjacentHTML("beforeend", html`<tr class="row-no-results"><td colspan="${cols}">No lines match "${q}"</td></tr>`);
    }
  });
}

function setupSearch() {
  document.getElementById("searchInput").addEventListener("input", (e) => {
    dash.search = e.target.value.trim().toLowerCase();
    applySearch();
  });
}

function renderNoHistory() {
  const next = can("upload")
    ? raw('Upload your first one under <a href="#upload">Upload statement</a>; an Approver then approves it.')
    : can("manageUsers")
      ? raw('A Submitter uploads it and an Approver approves it: add them under <a href="#settings">Users &amp; business</a>.')
      : "A Submitter uploads it and an Approver approves it.";
  document.getElementById("metrics").innerHTML =
    emptyNote(html`No approved income statement yet. ${next}`, "fa-circle-info", "dash-empty");
  ["submitted", "processed", "check"].forEach((id) => {
    document.getElementById(`${id}Meta`).textContent = "Nothing approved yet";
    document.getElementById(`${id}Badge`).hidden = true;
    document.getElementById(`${id}Table`).innerHTML = "";
  });
  document.getElementById("dashKpiMeta").textContent = "Checked once the first upload is approved.";
  document.getElementById("dashKpiBadge").hidden = true;
  document.getElementById("dashKpiList").innerHTML = "";
}

export function setup() {
  setupSearch();
  setupYearPicker();
  on("budget", () => state.processed && renderProcessed());
  on("targets", refreshDashboardSummary);
}

export function refresh() {
  const { submission } = state;
  if (!submission) {
    renderNoHistory();
    return;
  }
  dash.metricOptions = buildMetricOptions(submission, state.processed);
  dash.yearIndex = submission.years.length - 1;
  dash.selectedMetrics = loadMetricSelection();
  renderYearSelect();
  renderMetrics();
  renderSubmitted();
  renderProcessed();
}
