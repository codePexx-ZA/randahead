// Targets and rules section: KPI targets and warning rules
function percentOfRevenue(amount, revenue) {
  return revenue ? (amount / revenue) * 100 : null;
}

const KPI_GROUPS = ["Profit and margins", "Income and expenses"];
const KPI_METRICS = [
  ...["net_profit", "operating_profit", "gross_profit", "profit_before_tax"].map((key) => ({
    code: key,
    name: SUBTOTAL_NAMES[key],
    group: KPI_GROUPS[0],
    unit: "rand",
    better: "higher",
    value: (f, i) => f.subtotals[key][i],
  })),
  ...[
    ["net_margin", "Net profit margin", "net_profit"],
    ["operating_margin", "Operating profit margin", "operating_profit"],
    ["gross_margin", "Gross profit margin", "gross_profit"],
  ].map(([code, name, key]) => ({
    code,
    name,
    group: KPI_GROUPS[0],
    unit: "percent",
    better: "higher",
    value: (f, i) => percentOfRevenue(f.subtotals[key][i], f.totals.revenue[i]),
  })),
  ...LINE_CATEGORIES.map((c) => ({
    code: c.code,
    name: c.name,
    group: KPI_GROUPS[1],
    unit: "rand",
    better: c.kind === "income" ? "higher" : "lower",
    value: (f, i) => f.totals[c.code][i],
  })),
];

function getKpiMetric(code) {
  return KPI_METRICS.find((m) => m.code === code);
}

function kpiFigure(metric, year, processed, budget) {
  const i = processed.years.indexOf(year);
  if (i >= 0) return { source: "actual", value: metric.value(processed, i) };
  const j = budget?.years.indexOf(year) ?? -1;
  if (j >= 0) return { source: "budget", value: metric.value(budget, j) };
  return null;
}

function evaluateTarget(target, processed, budget, yearEndMonth) {
  const metric = getKpiMetric(target.metricName);
  const year = fyLabelFromDate(target.targetPeriod, yearEndMonth);
  const figure = metric ? kpiFigure(metric, year, processed, budget) : null;
  if (!figure || figure.value === null) return { target, metric, year, figure: null, status: "none" };

  const gap = figure.value - target.targetValue;
  const reached = metric.better === "higher" ? gap >= 0 : gap <= 0;
  const status = figure.source === "actual" ? (reached ? "met" : "missed") : (reached ? "on_track" : "off_track");
  return { target, metric, year, figure, gap, reached, status };
}

function targetYearOptions(processed) {
  const last = processed.years.at(-1);
  return [last, ...nextYearLabels(last, 3)];
}

function parseKpiValue(text, unit) {
  const cleaned = unit === "rand"
    ? text.replace(/[\sR,]/gi, "")
    : text.replace("%", "").replace(",", ".").trim();
  if (cleaned === "" || !/^-?\d+(\.\d+)?$/.test(cleaned)) return NaN;
  return unit === "rand" ? Math.round(Number(cleaned)) : Math.round(Number(cleaned) * 10) / 10;
}

const TG_STATUS = {
  met: { label: "Met", cls: "ok", icon: "fa-circle-check" },
  missed: { label: "Missed", cls: "bad", icon: "fa-circle-xmark" },
  on_track: { label: "On track", cls: "ok", icon: "fa-circle-check" },
  off_track: { label: "Off track", cls: "warn", icon: "fa-triangle-exclamation" },
  none: { label: "No figures yet", cls: "muted", icon: "fa-hourglass-half" },
};

const tg = {
  loaded: false,
  targets: [],
  rules: [],
  editingId: null,
  confirmTarget: null,
  confirmRule: null,
  dirtyRules: {},
  busy: false,
};

function formatKpiValue(value, unit) {
  return unit === "percent" ? `${value.toFixed(1)}%` : randFormat.format(value);
}

function formatGap(gap, unit) {
  const sign = gap > 0 ? "+" : gap < 0 ? "−" : "";
  return unit === "percent" ? `${sign}${Math.abs(gap).toFixed(1)} pts` : `${sign}${randFormat.format(Math.abs(gap))}`;
}

function formatThreshold(value) {
  return `${Number(value)}%`;
}

function kpiMetricOptionsHtml(selected) {
  return KPI_GROUPS.map((group) => {
    const options = KPI_METRICS.filter((m) => m.group === group)
      .map((m) => `<option value="${m.code}"${m.code === selected ? " selected" : ""}>${escapeHtml(m.name)}</option>`)
      .join("");
    return `<optgroup label="${group}">${options}</optgroup>`;
  }).join("");
}

function kpiYearOptionsHtml(selected) {
  const p = state.processed;
  const years = targetYearOptions(p);
  if (selected && !years.includes(selected)) years.unshift(selected);
  return years.map((year) => {
    const note = p.years.includes(year) ? " · actual" : state.budget?.years.includes(year) ? " · budget" : "";
    return `<option value="${year}"${year === selected ? " selected" : ""}>${year}${note}</option>`;
  }).join("");
}

function updateKpiHint() {
  const metric = getKpiMetric(document.getElementById("kpiMetric").value);
  const year = document.getElementById("kpiYear").value;
  const field = document.getElementById("kpiValueField");
  field.dataset.unit = metric.unit;
  document.getElementById("kpiValue").placeholder = metric.unit === "rand" ? "e.g. 1 200 000" : "e.g. 7.5";

  const way = metric.better === "higher"
    ? "Higher is better: reached when the figure is at least the target."
    : "Lower is better: reached when the figure is at most the target.";
  const figure = kpiFigure(metric, year, state.processed, state.budget);
  const current = figure && figure.value !== null
    ? ` ${figure.source === "actual" ? "Actual" : "Budget"} ${year}: ${formatKpiValue(figure.value, metric.unit)}.`
    : ` No actual or budget figure for ${year} yet.`;
  document.getElementById("kpiHint").textContent = way + current;
}

function fillKpiForm(id, metricCode, year, value, title) {
  tg.editingId = id;
  document.getElementById("kpiMetric").innerHTML = kpiMetricOptionsHtml(metricCode);
  document.getElementById("kpiYear").innerHTML = kpiYearOptionsHtml(year);
  document.getElementById("kpiValue").value = value;
  document.getElementById("kpiSaveText").textContent = id ? "Save changes" : "Add target";
  document.getElementById("kpiCancel").hidden = !id;
  document.getElementById("kpiFormTitle").textContent = title;
  updateKpiHint();
}

function resetKpiForm() {
  const firstFuture = state.budget && !state.budget.outOfDate
    ? state.budget.years[0]
    : nextYearLabels(state.processed.years.at(-1), 1)[0];
  document.getElementById("kpiValue").classList.remove("invalid");
  fillKpiForm(null, "net_profit", firstFuture, "", "Add a target");
}

function startEditTarget(id) {
  const t = tg.targets.find((x) => x.id === id);
  const metric = getKpiMetric(t.metricName);
  tg.confirmTarget = null;
  fillKpiForm(
    id,
    t.metricName,
    fyLabelFromDate(t.targetPeriod, state.business.yearEndMonth),
    metric.unit === "rand" ? numberFormat.format(t.targetValue) : String(t.targetValue),
    `Edit target: ${metric.name}`
  );
  renderTargetTable();
  document.getElementById("kpiForm").scrollIntoView({ behavior: "smooth", block: "center" });
  document.getElementById("kpiValue").focus({ preventScroll: true });
}

function targetActionsHtml(id) {
  if (!can("editTargets")) return "";
  if (tg.confirmTarget === id) {
    return `<td class="action-cell"><span class="tg-confirm">Delete this target?
      <button type="button" class="btn-reject btn-small" data-action="delete-target" data-id="${escapeHtml(id)}">Delete</button>
      <button type="button" class="btn-outline btn-small" data-action="keep-target">Keep</button></span></td>`;
  }
  return `<td class="action-cell">
    <button type="button" class="icon-btn" data-action="edit-target" data-id="${escapeHtml(id)}" title="Edit" aria-label="Edit target"><i class="fas fa-pen"></i></button>
    <button type="button" class="icon-btn danger" data-action="ask-delete-target" data-id="${escapeHtml(id)}" title="Delete" aria-label="Delete target"><i class="fas fa-trash-can"></i></button>
  </td>`;
}

function targetRowHtml(r) {
  const { target, metric, figure } = r;
  const status = TG_STATUS[r.status];
  const name = metric ? metric.name : target.metricName;
  const unit = metric ? metric.unit : target.measurementUnit;
  const way = metric ? (metric.better === "higher" ? "at least" : "at most") : "";
  const dash = '<span class="tg-muted">–</span>';
  const compared = figure
    ? `${formatKpiValue(figure.value, unit)}<small>${figure.source === "actual" ? "Actual" : "Budget"}</small>`
    : dash;
  const gap = figure ? `<span class="tg-gap ${r.reached ? "good" : "bad"}">${formatGap(r.gap, unit)}</span>` : dash;
  const editing = tg.editingId === target.id ? " row-editing" : "";

  return `<tr class="tg-row${editing}" data-id="${escapeHtml(target.id)}">
    <td><span class="cat-name">${escapeHtml(name)}</span><small>${way ? `Target: ${way}` : "Unknown KPI"}</small></td>
    <td class="num">${formatKpiValue(target.targetValue, unit)}</td>
    <td class="num">${compared}</td>
    <td class="num">${gap}</td>
    <td>${chip(status.cls, status.label, status.icon)}</td>
    ${targetActionsHtml(target.id)}
  </tr>`;
}

function renderTargetTable() {
  renderDashboardTargets();
  const table = document.getElementById("kpiTable");
  const yem = state.business.yearEndMonth;
  const results = tg.targets.map((t) => evaluateTarget(t, state.processed, state.budget, yem));
  document.getElementById("kpiMeta").textContent = state.budget?.outOfDate
    ? `A goal per KPI per financial year. Years with actual figures are checked against them; future years against the budget, which is out of date (the approved history now runs to ${state.processed.years.at(-1)}). Choose a new budget in Forecast & budget.`
    : "A goal per KPI per financial year. Years with actual figures are checked against them; future years are checked against the budget.";

  const checked = results.filter((r) => r.figure);
  const reached = checked.filter((r) => r.reached).length;
  const badge = document.getElementById("kpiBadge");
  badge.hidden = !results.length;
  badge.className = `badge ${checked.length && reached === checked.length ? "approved" : "warning"}`;
  badge.textContent = checked.length ? `${reached} of ${checked.length} on target` : `${results.length} set`;

  if (!results.length) {
    const hint = can("editTargets") ? " Add one above." : "";
    table.innerHTML = `<tbody><tr class="row-empty"><td>No KPI targets yet.${hint}</td></tr></tbody>`;
    return;
  }

  const order = (r) => (r.metric ? KPI_METRICS.indexOf(r.metric) : KPI_METRICS.length);
  results.sort((a, b) => b.target.targetPeriod.localeCompare(a.target.targetPeriod) || order(a) - order(b));

  const cols = can("editTargets") ? 6 : 5;
  let body = "";
  let lastYear = null;
  results.forEach((r) => {
    if (r.year !== lastYear) {
      lastYear = r.year;
      const source = r.figure ? (r.figure.source === "actual" ? "checked against the actual figures" : "checked against the budget") : "no actual or budget figures yet";
      body += `<tr class="tg-year"><td colspan="${cols}"><strong>${escapeHtml(r.year)}</strong> · ${source}</td></tr>`;
    }
    body += targetRowHtml(r);
  });

  table.innerHTML = `<thead><tr>
      <th>KPI</th><th class="num">Target</th><th class="num">Figure</th><th class="num">Difference</th><th>Status</th>
      ${can("editTargets") ? '<th class="action-cell"><span class="sr-only">Actions</span></th>' : ""}
    </tr></thead><tbody>${body}</tbody>`;
}

async function reloadTargets(highlightId) {
  tg.targets = await Api.getKpiTargets();
  renderTargetTable();
  if (highlightId) document.querySelector(`#kpiTable tr[data-id="${CSS.escape(highlightId)}"]`)?.classList.add("row-new");
}

function rejectField(input, message) {
  input.classList.add("invalid");
  input.focus();
  showToast(message, "error");
}

async function saveTarget(e) {
  e.preventDefault();
  if (tg.busy || !can("editTargets")) return;

  const metric = getKpiMetric(document.getElementById("kpiMetric").value);
  const year = document.getElementById("kpiYear").value;
  const input = document.getElementById("kpiValue");
  const value = parseKpiValue(input.value, metric.unit);

  let problem = "";
  if (Number.isNaN(value)) problem = metric.unit === "rand" ? "Type the target in rands, e.g. 1 200 000." : "Type the target as a percentage, e.g. 7.5.";
  else if (metric.unit === "rand" && value < 0) problem = "A rand target can't be negative.";
  else if (metric.unit === "percent" && (value < -100 || value > 100)) problem = "A margin target must be between -100% and 100%.";
  if (problem) return rejectField(input, problem);

  const target = {
    metricName: metric.code,
    targetValue: value,
    measurementUnit: metric.unit,
    targetPeriod: fyStartDate(year, state.business.yearEndMonth),
  };
  const btn = document.getElementById("kpiSave");
  tg.busy = true;
  btn.disabled = true;
  try {
    const editing = tg.editingId;
    const saved = editing ? await Api.updateKpiTarget(editing, target) : await Api.createKpiTarget(target);
    resetKpiForm();
    await reloadTargets(saved.id);
    showToast(`${metric.name} target for ${year} ${editing ? "updated" : "added"}.`, "success");
  } catch (err) {
    showToast(err.message || "Could not save the target.", "error");
  } finally {
    tg.busy = false;
    btn.disabled = false;
  }
}

async function deleteTarget(id) {
  try {
    await Api.deleteKpiTarget(id);
    tg.confirmTarget = null;
    if (tg.editingId === id) resetKpiForm();
    await reloadTargets();
    showToast("Target deleted.", "success");
  } catch (err) {
    showToast(err.message || "Could not delete the target.", "error");
  }
}

function rulePreview(value, period, threshold, active = true) {
  if (value === null) return { cls: "none", text: "No indicator data for your history years yet." };
  if (Number.isNaN(threshold)) return { cls: "none", text: `Latest in your history: ${value.toFixed(1)}% in ${period}.` };
  const latest = `Latest in your history: ${value.toFixed(1)}% in ${period}`;
  if (value <= threshold) return { cls: "ok", text: `${latest}, within ${formatThreshold(threshold)}, no warning.` };
  return { cls: "warn", text: `${latest}, above ${formatThreshold(threshold)}, ${active ? "so it warns" : "so it would warn if switched on"}.` };
}

function previewHtml(p) {
  const icon = { warn: "fa-triangle-exclamation", ok: "fa-circle-check", none: "fa-circle-info" }[p.cls];
  return `<i class="fas ${icon}"></i><span>${escapeHtml(p.text)}</span>`;
}

function setPreview(el, preview) {
  el.className = `rule-preview ${preview.cls}`;
  el.innerHTML = previewHtml(preview);
}

function parseThreshold(text) {
  const value = parseKpiValue(text, "percent");
  return value >= 0 && value <= 100 ? value : NaN;
}

function ruleActionsHtml(rule) {
  if (!can("editRules")) return "";
  if (tg.confirmRule === rule.id) {
    return `<div class="rule-actions"><span class="tg-confirm">Delete this rule?
      <button type="button" class="btn-reject btn-small" data-action="delete-rule">Delete</button>
      <button type="button" class="btn-outline btn-small" data-action="keep-rule">Keep</button></span></div>`;
  }
  const dirty = rule.id in tg.dirtyRules;
  const del = rule.deletable
    ? '<button type="button" class="icon-btn danger" data-action="ask-delete-rule" title="Delete" aria-label="Delete rule"><i class="fas fa-trash-can"></i></button>'
    : '<span class="icon-btn locked" title="This rule has raised warnings before, so it can be switched off but not deleted"><i class="fas fa-lock"></i></span>';
  return `<div class="rule-actions">
    <button type="button" class="btn-outline btn-small" data-action="undo-rule"${dirty ? "" : " hidden"}>Undo</button>
    <button type="button" class="btn-primary btn-small" data-action="save-rule"${dirty ? "" : " hidden"}><i class="fas fa-check"></i><span>Save</span></button>
    ${del}
  </div>`;
}

function ruleRowHtml(rule) {
  const editable = can("editRules");
  const typed = tg.dirtyRules[rule.id];
  const threshold = typed ?? String(Number(rule.threshold));
  const preview = rulePreview(rule.value, rule.period, parseThreshold(threshold), rule.active);
  const cats = rule.categories.map((code) => `<span class="cat-chip">${escapeHtml(getCategory(code).name)}</span>`).join("");
  const fieldId = `ruleThreshold-${escapeHtml(rule.id)}`;

  return `<div class="rule-row${rule.active ? "" : " inactive"}" data-id="${escapeHtml(rule.id)}">
    <label class="switch" title="${rule.active ? "On: raises warnings" : "Off: raises no warnings"}">
      <input type="checkbox" class="rule-active"${rule.active ? " checked" : ""}${editable ? "" : " disabled"} aria-label="Rule switched on" />
      <span class="switch-track"></span>
    </label>
    <div class="rule-main">
      <strong>${escapeHtml(rule.name)}</strong>
      <small>${escapeHtml(rule.indicatorName)}${rule.active ? "" : " · switched off"}</small>
      <div class="indicator-cats"><span>Affects:</span>${cats}</div>
    </div>
    <div class="rule-threshold">
      <label for="${fieldId}">Warn above</label>
      <div class="input-affix" data-unit="percent">
        <input type="text" inputmode="decimal" id="${fieldId}" class="rule-threshold-input" value="${escapeHtml(threshold)}"${editable ? "" : " disabled"} />
      </div>
    </div>
    <p class="rule-preview ${preview.cls}">${previewHtml(preview)}</p>
    ${ruleActionsHtml(rule)}
  </div>`;
}

function renderRules() {
  const list = document.getElementById("ruleList");
  list.innerHTML = tg.rules.length
    ? tg.rules.map(ruleRowHtml).join("")
    : '<p class="ap-empty"><i class="fas fa-circle-info"></i><span>No warning rules yet.</span></p>';

  const active = tg.rules.filter((r) => r.active);
  const warning = active.filter((r) => r.triggered).length;
  const badge = document.getElementById("ruleBadge");
  badge.className = `badge ${warning ? "warning" : "approved"}`;
  badge.textContent = `${active.length} of ${tg.rules.length} on · ${warning} warning${warning === 1 ? "" : "s"}`;
  updateAddRulePreview();
}

function ruleOfRow(row) {
  return tg.rules.find((r) => r.id === row.dataset.id);
}

function updateRulePreview(row) {
  const rule = ruleOfRow(row);
  const input = row.querySelector(".rule-threshold-input");
  const typed = input.value.trim();
  const threshold = parseThreshold(typed);
  const changed = threshold !== Number(rule.threshold);
  if (changed) tg.dirtyRules[rule.id] = typed;
  else delete tg.dirtyRules[rule.id];

  input.classList.toggle("invalid", typed !== "" && Number.isNaN(threshold));
  setPreview(row.querySelector(".rule-preview"), rulePreview(rule.value, rule.period, threshold, rule.active));
  row.querySelectorAll('[data-action="save-rule"], [data-action="undo-rule"]').forEach((b) => (b.hidden = !changed));
}

async function reloadRules() {
  tg.rules = await Api.getRules(historyYears(), state.business.yearEndMonth);
  renderRules();
  reloadWarnings();
  refreshApprovals();
}

async function updateRule(rule, changes, message) {
  const row = document.querySelector(`.rule-row[data-id="${CSS.escape(rule.id)}"]`);
  row?.classList.add("busy");
  try {
    await Api.updateRule(rule.id, {
      name: rule.name,
      indicatorCode: rule.indicatorCode,
      threshold: rule.threshold,
      active: rule.active,
      ...changes,
    });
    delete tg.dirtyRules[rule.id];
    await reloadRules();
    showToast(message, "success");
  } catch (err) {
    row?.classList.remove("busy");
    showToast(err.message || "Could not save the rule.", "error");
  }
}

function saveRuleThreshold(row) {
  const rule = ruleOfRow(row);
  const input = row.querySelector(".rule-threshold-input");
  const threshold = parseThreshold(input.value);
  if (Number.isNaN(threshold)) return rejectField(input, "Type a threshold between 0 and 100, e.g. 6 or 7.5.");
  updateRule(rule, { threshold }, `${rule.name}: now warns above ${formatThreshold(threshold)}.`);
}

function toggleRule(row, active) {
  const rule = ruleOfRow(row);
  updateRule(rule, { active }, active ? `${rule.name} switched on.` : `${rule.name} switched off: it won't raise warnings.`);
}

async function deleteRule(id) {
  try {
    await Api.deleteRule(id);
    tg.confirmRule = null;
    delete tg.dirtyRules[id];
    await reloadRules();
    showToast("Rule deleted.", "success");
  } catch (err) {
    tg.confirmRule = null;
    renderRules();
    showToast(err.message || "Could not delete the rule.", "error");
  }
}

function suggestedRuleName() {
  const type = getIndicatorType(document.getElementById("ruleIndicator").value);
  const threshold = parseThreshold(document.getElementById("ruleThreshold").value);
  return Number.isNaN(threshold) ? `${type.name} above threshold` : `${type.name} above ${formatThreshold(threshold)}`;
}

function updateAddRulePreview() {
  const code = document.getElementById("ruleIndicator").value;
  const known = tg.rules.find((r) => r.indicatorCode === code);
  const threshold = parseThreshold(document.getElementById("ruleThreshold").value);
  const preview = known ? rulePreview(known.value, known.period, threshold) : rulePreview(null, null, threshold);
  setPreview(document.getElementById("ruleAddPreview"), preview);
  document.getElementById("ruleName").placeholder = suggestedRuleName();
}

async function addRule(e) {
  e.preventDefault();
  if (tg.busy || !can("editRules")) return;

  const thresholdInput = document.getElementById("ruleThreshold");
  const threshold = parseThreshold(thresholdInput.value);
  if (Number.isNaN(threshold)) return rejectField(thresholdInput, "Type a threshold between 0 and 100, e.g. 15.");
  const nameInput = document.getElementById("ruleName");
  const name = nameInput.value.trim() || suggestedRuleName();
  if (name.length > 200) {
    nameInput.classList.add("invalid");
    showToast("Keep the rule name under 200 characters.", "error");
    return;
  }

  const btn = document.getElementById("ruleAddBtn");
  tg.busy = true;
  btn.disabled = true;
  try {
    await Api.createRule({ name, indicatorCode: document.getElementById("ruleIndicator").value, threshold, active: true });
    nameInput.value = "";
    thresholdInput.value = "";
    await reloadRules();
    showToast(`Rule "${name}" added and switched on.`, "success");
  } catch (err) {
    nameInput.classList.add("invalid");
    showToast(err.message || "Could not add the rule.", "error");
  } finally {
    tg.busy = false;
    btn.disabled = false;
  }
}

function renderNoTargetHistory() {
  document.getElementById("kpiMeta").textContent =
    "Targets are set per financial year, so they open once the first income statement is approved.";
  document.getElementById("kpiBadge").hidden = true;
  document.getElementById("kpiTable").innerHTML = "";
}

async function refreshTargets() {
  if (!state.business) return;
  try {
    [tg.targets, tg.rules] = await Promise.all([
      Api.getKpiTargets(),
      Api.getRules(historyYears(), state.business.yearEndMonth),
    ]);
    tg.loaded = true;
    renderRules();
    document.getElementById("kpiForm").hidden = !can("editTargets") || !state.processed;
    if (!state.processed) return renderNoTargetHistory();
    if (!tg.editingId) resetKpiForm();
    renderTargetTable();
  } catch (err) {
    showToast(err.message || "Could not load the targets and rules.", "error");
  }
}

function setupTargets() {
  showRoleNote("kpiRoleNote", "editTargets", "you can see the targets, but only a Decision Maker can add or change them.");
  showRoleNote("ruleRoleNote", "editRules", "you can see the rules, but only an Approver or Decision Maker can change them.");
  document.getElementById("kpiForm").hidden = !can("editTargets");
  document.getElementById("ruleForm").hidden = !can("editRules");

  document.getElementById("ruleIndicator").innerHTML = INDICATOR_TYPES
    .map((t) => `<option value="${t.code}">${escapeHtml(t.name)}</option>`)
    .join("");

  const kpiForm = document.getElementById("kpiForm");
  kpiForm.addEventListener("submit", saveTarget);
  kpiForm.addEventListener("change", (e) => {
    if (e.target.id === "kpiMetric" || e.target.id === "kpiYear") updateKpiHint();
  });
  document.getElementById("kpiValue").addEventListener("input", (e) => e.target.classList.remove("invalid"));
  document.getElementById("kpiCancel").addEventListener("click", () => {
    resetKpiForm();
    renderTargetTable();
  });

  document.getElementById("kpiTable").addEventListener("click", (e) => {
    const btn = e.target.closest("[data-action]");
    if (!btn || !can("editTargets")) return;
    const action = btn.dataset.action;
    if (action === "edit-target") startEditTarget(btn.dataset.id);
    if (action === "ask-delete-target") tg.confirmTarget = btn.dataset.id;
    if (action === "keep-target") tg.confirmTarget = null;
    if (action === "delete-target") return deleteTarget(btn.dataset.id);
    renderTargetTable();
  });

  const list = document.getElementById("ruleList");
  list.addEventListener("input", (e) => {
    if (e.target.classList.contains("rule-threshold-input")) updateRulePreview(e.target.closest(".rule-row"));
  });
  list.addEventListener("keydown", (e) => {
    if (e.key === "Enter" && e.target.classList.contains("rule-threshold-input")) saveRuleThreshold(e.target.closest(".rule-row"));
  });
  list.addEventListener("change", (e) => {
    if (e.target.classList.contains("rule-active") && can("editRules")) toggleRule(e.target.closest(".rule-row"), e.target.checked);
  });
  list.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-action]");
    if (!btn || !can("editRules")) return;
    const row = btn.closest(".rule-row");
    const id = row.dataset.id;
    const action = btn.dataset.action;
    if (action === "save-rule") return saveRuleThreshold(row);
    if (action === "delete-rule") return deleteRule(id);
    if (action === "undo-rule") delete tg.dirtyRules[id];
    if (action === "ask-delete-rule") tg.confirmRule = id;
    if (action === "keep-rule") tg.confirmRule = null;
    renderRules();
  });

  const ruleForm = document.getElementById("ruleForm");
  ruleForm.addEventListener("submit", addRule);
  ruleForm.addEventListener("input", (e) => {
    e.target.classList.remove("invalid");
    updateAddRulePreview();
  });

  window.addEventListener("hashchange", () => {
    if (location.hash !== "#targets" || !state.processed) return;
    renderTargetTable();
    updateKpiHint();
  });
}
