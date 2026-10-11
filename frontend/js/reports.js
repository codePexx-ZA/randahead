// Reports section: the Decision Maker's combined report of every approved year and the approved budget, with KPI targets, warnings, CSV download and print
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
  const change = changeFrom ? `<th class="num">Change<small>${escapeHtml(changeFrom)}–${escapeHtml(changeTo)}</small></th>` : "";
  const header = `<th>Category (R)</th>${columns
    .map((c) => `<th class="num${rpColumnClass(c)}">${escapeHtml(c.year)}<small>${SOURCE_LABELS[c.source]}</small></th>`)
    .join("")}${change}`;

  const lineRows = lines.map((line) => {
    const team = line.teamManaged
      ? `<span class="tag" title="Set by the business; no external indicator applies">Team-managed</span>`
      : "";
    const values = line.values.map((v, i) => `<td class="num${rpColumnClass(columns[i])}">${numberFormat.format(v)}</td>`).join("");
    const changeCell = changeFrom
      ? `<td class="num forecast-cell ${changeClass(line.change, line.kind)}">${pctText(line.change)}</td>`
      : "";
    return `<tr class="${line.subtotal ? "row-subtotal" : "row-line"}">
      <td><span class="cat-name">${escapeHtml(line.name)}</span>${team}</td>${values}${changeCell}</tr>`;
  });

  const ratioRows = ratios.map((ratio) => {
    const values = ratio.values.map((v, i) => `<td class="num${rpColumnClass(columns[i])}">${rpPercent(v)}</td>`).join("");
    return `<tr class="row-line rp-ratio"><td>${escapeHtml(ratio.name)}</td>${values}${changeFrom ? "<td></td>" : ""}</tr>`;
  });

  const cols = columns.length + (changeFrom ? 2 : 1);
  return `<thead><tr>${header}</tr></thead>
    <tbody>${lineRows.join("")}${headingRow("Key ratios", cols)}${ratioRows.join("")}</tbody>`;
}

function rpTargetsHtml() {
  return `<thead><tr><th>KPI</th><th>Year</th><th class="num">Target</th><th class="num">Result</th><th class="num">Gap</th><th>Status</th></tr></thead>
    <tbody>${rp.data.targets.map((t) => {
      const status = TG_STATUS[t.status];
      const way = t.higherIsBetter ? "at least" : "at most";
      const result = t.source
        ? `${formatKpiValue(t.value, t.unit)}<small>${SOURCE_LABELS[t.source]}</small>`
        : '<span class="tg-muted">–</span>';
      const gap = t.source ? `<span class="tg-gap ${t.reached ? "good" : "bad"}">${formatGap(t.gap, t.unit)}</span>` : "–";
      return `<tr class="row-line">
        <td><span class="cat-name">${escapeHtml(t.name)}</span></td>
        <td>${escapeHtml(t.year)}</td>
        <td class="num">${way} ${formatKpiValue(t.targetValue, t.unit)}</td>
        <td class="num">${result}</td>
        <td class="num">${gap}</td>
        <td>${chip(status.cls, status.label, status.icon)}</td>
      </tr>`;
    }).join("")}</tbody>`;
}

function renderReportTargets() {
  const { targets } = rp.data;
  const table = document.getElementById("rpTargets");
  const badge = document.getElementById("rpTargetsBadge");
  if (!targets.length) {
    document.getElementById("rpTargetsMeta").textContent = "No targets set yet.";
    badge.hidden = true;
    table.innerHTML = '<tbody><tr class="row-empty"><td>No KPI targets yet. Add them in Targets &amp; rules.</td></tr></tbody>';
    return;
  }
  const years = [...new Set(targets.map((t) => t.year))].reverse();
  const checked = targets.filter((t) => t.source);
  const reached = checked.filter((t) => t.reached).length;
  document.getElementById("rpTargetsMeta").textContent =
    `${targets.length} target${targets.length === 1 ? "" : "s"} for ${yearsText(years)}`;
  badge.hidden = false;
  badge.className = `badge ${checked.length && reached === checked.length ? "approved" : "warning"}`;
  badge.textContent = checked.length ? `${reached} of ${checked.length} on target` : `${targets.length} set`;
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
    ? triggered.map((w) => `<li class="rp-warning"><i class="fas fa-triangle-exclamation"></i><span>${escapeHtml(rpWarningText(w))}</span></li>`).join("")
    : `<li class="rp-warning ok"><i class="fas fa-circle-check"></i><span>${checked ? "Every active warning rule is within its threshold." : "No indicators were checked."}</span></li>`;
}

function renderReport() {
  const { business, unit, generatedOn, budget, columns } = rp.data;
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
  const changeNote = rp.data.changeFrom
    ? ` Change compares ${escapeHtml(rp.data.changeTo)} with ${escapeHtml(rp.data.changeFrom)}. <strong class="note-good">Green</strong> is better for the business, <strong class="note-bad">red</strong> is worse.`
    : "";
  document.getElementById("rpNote").innerHTML =
    `Amounts in rands from the approved income statements. ${escapeHtml(budgetNote)}${changeNote}`;

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
  document.getElementById("rpStatement").innerHTML =
    '<tbody><tr class="row-empty"><td>No approved income statement yet.</td></tr></tbody>';
  ["rpTargets", "rpWarnings"].forEach((id) => (document.getElementById(id).innerHTML = ""));
  ["rpTargetsMeta", "rpWarningsMeta"].forEach((id) => (document.getElementById(id).textContent = "Checked once history is approved."));
  ["rpTargetsBadge", "rpWarningsBadge"].forEach((id) => (document.getElementById(id).hidden = true));
}

async function refreshReports() {
  if (!can("viewReports") || !rpActive() || !state.business) return;
  if (!state.processed) {
    renderReportEmpty();
    return;
  }
  const request = ++rp.request;
  rpSetActions(false);
  try {
    const [data, warnings] = await Promise.all([
      Api.getReport(state.processed, state.budget, state.business),
      Api.getIndicatorWarnings(historyYears(), state.business.yearEndMonth),
    ]);
    if (request !== rp.request) return;
    rp.data = data;
    rp.warnings = warnings;
    renderReport();
  } catch (err) {
    if (request === rp.request) showToast(err.message || "Could not load the report.", "error");
  }
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

function setupReports() {
  document.getElementById("rpCsv").addEventListener("click", downloadReportCsv);
  document.getElementById("rpPrint").addEventListener("click", () => window.print());
}
