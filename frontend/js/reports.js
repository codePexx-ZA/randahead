// Reports section: the Decision Maker's combined report of every approved year and the approved budget, with KPI targets, warnings, CSV download and print
import { getReport } from "./api.js";
import { formatKpiValue, numberFormat, yearsText } from "./format.js";
import { METHOD_NAMES } from "./forecast.js";
import { getCategory } from "./store.js";
import { TG_STATUS } from "./ui.js";
import { can, indicatorWarnings, state } from "./store.js";
import { showToast } from "./toast.js";
import { chip, emptyRow, headingRow, html, kpiFigureHtml, kpiGapHtml, pctCell, raw, setTargetBadge, teamTag } from "./ui.js";

const rp = { data: null, warnings: [], request: 0 };

const SOURCE_LABELS = { actual: "Actual", budget: "Budget" };

function rpActive() {
  return document.getElementById("view-reports").classList.contains("active");
}

function rpSetActions(enabled) {
  document.getElementById("rpCsv").disabled = !enabled;
  document.getElementById("rpPrint").disabled = !enabled;
}

function rpColumnClass(column) {
  return column.source === "budget" ? " col-budget" : "";
}

function rpRange(source) {
  const years = rp.data.columns.filter((c) => c.source === source).map((c) => c.year);
  return years.length ? yearsText(years) : null;
}

function rpPercent(value) {
  return value === null ? "–" : `${value.toFixed(1)}%`;
}

function rpStatementHtml() {
  const { columns, lines, ratios, changeFrom, changeTo } = rp.data;
  const header = html`<th>Category (R)</th>${columns
    .map((c) => html`<th class="num${rpColumnClass(c)}">${c.year}<small>${SOURCE_LABELS[c.source]}</small></th>`)}${changeFrom
    ? html`<th class="num">Change<small>${changeFrom}–${changeTo}</small></th>`
    : ""}`;

  const lineRows = lines.map((line) => html`<tr class="${line.subtotal ? "row-subtotal" : "row-line"}">
      <td><span class="cat-name">${line.name}</span>${line.teamManaged ? teamTag() : ""}</td>${line.values
    .map((v, i) => html`<td class="num${rpColumnClass(columns[i])}">${numberFormat.format(v)}</td>`)}${changeFrom
    ? pctCell(line.change, line.kind)
    : ""}</tr>`);

  const ratioRows = ratios.map((ratio) => html`<tr class="row-line rp-ratio"><td>${ratio.name}</td>${ratio.values
    .map((v, i) => html`<td class="num${rpColumnClass(columns[i])}">${rpPercent(v)}</td>`)}${changeFrom ? raw("<td></td>") : ""}</tr>`);

  const cols = columns.length + (changeFrom ? 2 : 1);
  return html`<thead><tr>${header}</tr></thead>
    <tbody>${lineRows}${headingRow("Key ratios", cols)}${ratioRows}</tbody>`;
}

function rpTargetsHtml() {
  return html`<thead><tr><th>KPI</th><th>Year</th><th class="num">Target</th><th class="num">Result</th><th class="num">Gap</th><th>Status</th></tr></thead>
    <tbody>${rp.data.targets.map((t) => {
      const status = TG_STATUS[t.status];
      const way = t.higherIsBetter ? "at least" : "at most";
      return html`<tr class="row-line">
        <td><span class="cat-name">${t.name}</span></td>
        <td>${t.year}</td>
        <td class="num">${way} ${formatKpiValue(t.targetValue, t.unit)}</td>
        <td class="num">${t.source ? kpiFigureHtml(t.value, t.unit, t.source) : raw('<span class="tg-muted">–</span>')}</td>
        <td class="num">${t.source ? kpiGapHtml(t.gap, t.unit, t.reached) : "–"}</td>
        <td>${chip(status.cls, status.label, status.icon)}</td>
      </tr>`;
    })}</tbody>`;
}

function renderReportTargets() {
  const { targets } = rp.data;
  const table = document.getElementById("rpTargets");
  const badge = document.getElementById("rpTargetsBadge");
  if (!targets.length) {
    document.getElementById("rpTargetsMeta").textContent = "No targets set yet.";
    badge.hidden = true;
    table.innerHTML = emptyRow("No KPI targets yet. Add them in Targets & rules.");
    return;
  }
  const years = [...new Set(targets.map((t) => t.year))].reverse();
  const checked = targets.filter((t) => t.source);
  document.getElementById("rpTargetsMeta").textContent =
    `${targets.length} target${targets.length === 1 ? "" : "s"} for ${yearsText(years)}`;
  badge.hidden = false;
  setTargetBadge(badge, targets.length, checked.length, checked.filter((t) => t.reached).length);
  table.innerHTML = rpTargetsHtml();
}

function rpWarningText(w) {
  const names = w.categories.map((code) => getCategory(code).name).join(", ");
  return `${w.indicatorName} was ${w.value.toFixed(1)}% in ${w.period}, above its ${w.threshold}% threshold: review ${names}.`;
}

function renderReportWarnings() {
  const list = document.getElementById("rpWarnings");
  const badge = document.getElementById("rpWarningsBadge");
  const checked = rp.warnings.length;
  const triggered = rp.warnings.filter((w) => w.triggered);
  document.getElementById("rpWarningsMeta").textContent = checked
    ? `${checked} active warning rule${checked === 1 ? "" : "s"} checked against the approved years`
    : "Every warning rule is switched off";
  badge.hidden = !checked;
  badge.className = `badge ${triggered.length ? "warning" : "approved"}`;
  badge.textContent = triggered.length ? `${triggered.length} warning${triggered.length === 1 ? "" : "s"}` : "No warnings";

  list.innerHTML = triggered.length
    ? html`${triggered.map((w) => html`<li class="rp-warning"><i class="fas fa-triangle-exclamation"></i><span>${rpWarningText(w)}</span></li>`)}`
    : html`<li class="rp-warning ok"><i class="fas fa-circle-check"></i><span>${checked ? "Every active warning rule is within its threshold." : "No indicators were checked."}</span></li>`;
}

function renderReport() {
  const { business, unit, generatedOn, budget, columns, changeFrom, changeTo } = rp.data;
  const actual = rpRange("actual");
  const planned = rpRange("budget");
  document.getElementById("rpTitle").textContent = `Combined report · ${business}`;
  document.getElementById("rpMeta").textContent =
    `${unit} · ${actual} actual${planned ? ` and ${planned} budget` : ""} · prepared ${generatedOn}`;

  const budgetNote = budget
    ? planned
      ? `Budget columns: ${METHOD_NAMES[budget.method]}, approved by ${budget.approvedBy} on ${budget.approvedOn}.`
      : `The approved budget (${METHOD_NAMES[budget.method]}) covers years that now have actual figures; compare them on the Dashboard.`
    : "No approved budget yet, so only actual years are shown.";
  const changeNote = changeFrom
    ? html` Change compares ${changeTo} with ${changeFrom}. <strong class="note-good">Green</strong> is better for the business, <strong class="note-bad">red</strong> is worse.`
    : "";
  document.getElementById("rpNote").innerHTML =
    html`Amounts in rands from the approved income statements. ${budgetNote}${changeNote}`;

  document.getElementById("rpStatement").innerHTML = columns.length ? rpStatementHtml() : "";
  renderReportTargets();
  renderReportWarnings();
  rpSetActions(true);
}

function renderReportEmpty() {
  rp.data = null;
  rpSetActions(false);
  document.getElementById("rpTitle").textContent = "Combined report";
  document.getElementById("rpMeta").textContent = "Nothing approved yet";
  document.getElementById("rpNote").textContent = "The report fills in once the first income statement upload is approved.";
  document.getElementById("rpStatement").innerHTML = emptyRow("No approved income statement yet.");
  ["rpTargets", "rpWarnings"].forEach((id) => (document.getElementById(id).innerHTML = ""));
  ["rpTargetsMeta", "rpWarningsMeta"].forEach((id) => (document.getElementById(id).textContent = "Checked once history is approved."));
  ["rpTargetsBadge", "rpWarningsBadge"].forEach((id) => (document.getElementById(id).hidden = true));
}

function csvCell(value) {
  const text = value === null || value === undefined ? "" : String(value);
  return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function reportCsv() {
  const { business, unit, generatedOn, columns, lines, ratios, targets, changeFrom, changeTo } = rp.data;
  const yearHeads = columns.map((c) => `${c.year} ${SOURCE_LABELS[c.source]}`);
  const rows = [
    [`RandAhead combined report: ${business} (${unit})`],
    [`Prepared ${generatedOn}`],
    [],
    ["Category (R)", ...yearHeads, ...(changeFrom ? [`Change ${changeFrom}-${changeTo} (%)`] : [])],
    ...lines.map((l) => [l.name, ...l.values.map(Math.round), ...(changeFrom ? [l.change] : [])]),
    [],
    ["Key ratios (%)", ...yearHeads],
    ...ratios.map((r) => [r.name, ...r.values]),
    [],
    ["KPI target", "Year", "Target", "Unit", "Result", "Source", "Gap", "Status"],
    ...targets.map((t) => [t.name, t.year, t.targetValue, t.unit, t.value, t.source, t.gap, TG_STATUS[t.status].label]),
    [],
    ["Economic warnings"],
    ...rp.warnings.filter((w) => w.triggered).map((w) => [rpWarningText(w)]),
  ];
  return rows.map((row) => row.map(csvCell).join(",")).join("\r\n");
}

function downloadReportCsv() {
  if (!rp.data) return;
  const blob = new Blob(["\ufeff", reportCsv()], { type: "text/csv;charset=utf-8" });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = `randahead-report-${rp.data.generatedOn}.csv`;
  link.click();
  URL.revokeObjectURL(link.href);
}

export function setup() {
  document.getElementById("rpCsv").addEventListener("click", downloadReportCsv);
  document.getElementById("rpPrint").addEventListener("click", () => window.print());
}

export async function refresh() {
  if (!can("viewReports") || !rpActive() || !state.business) return;
  if (!state.processed) {
    renderReportEmpty();
    return;
  }
  const request = ++rp.request;
  rpSetActions(false);
  try {
    const data = await getReport();
    if (request !== rp.request) return;
    rp.data = data;
    rp.warnings = indicatorWarnings(true);
    renderReport();
  } catch (err) {
    if (request === rp.request) showToast(err.message, "error");
  }
}

export const onShow = refresh;
