// Targets and rules section: KPI targets and warning rules
import { createKpiTarget, createRule, deleteKpiTarget, deleteRule as removeRule, getKpiTargets, updateKpiTarget, updateRule as saveRule } from "./api.js";
import { formatKpiValue, numberFormat } from "./format.js";
import { INDICATOR_NAMES, fyStartDate, nextYearLabels } from "./forecast.js";
import { can, emit, getKpi, indicatorWarnings, on, reloadRules, showRoleNote, state } from "./store.js";
import { showToast } from "./toast.js";
import {
  TG_STATUS, categoryChips, chip, confirmDelete, emptyNote, emptyRow, html, kpiFigureHtml, kpiGapHtml, lockIcon, raw,
  setTargetBadge, withBusy,
} from "./ui.js";

const tg = {
  loaded: false,
  targets: [],
  figures: [],
  rules: [],
  editingId: null,
  confirmTarget: null,
  confirmRule: null,
  dirtyRules: {},
  busy: false,
};

function formatThreshold(value) {
  return `${Number(value)}%`;
}

function parseKpiValue(text, unit) {
  const cleaned = unit === "rand"
    ? text.replace(/[\sR,]/gi, "")
    : text.replace("%", "").replace(",", ".").trim();
  if (cleaned === "" || !/^-?\d+(\.\d+)?$/.test(cleaned)) return NaN;
  return unit === "rand" ? Math.round(Number(cleaned)) : Math.round(Number(cleaned) * 10) / 10;
}

function kpiMetricOptionsHtml(selected) {
  const { kpis } = state.definitions;
  const groups = [...new Set(kpis.map((k) => k.group))];
  return html`${groups.map((group) => html`<optgroup label="${group}">${kpis.filter((k) => k.group === group)
    .map((k) => html`<option value="${k.code}"${raw(k.code === selected ? " selected" : "")}>${k.name}</option>`)}</optgroup>`)}`;
}

function kpiYearOptionsHtml(selected) {
  const p = state.processed;
  const last = p.years.at(-1);
  const years = [last, ...nextYearLabels(last, 3)];
  if (selected && !years.includes(selected)) years.unshift(selected);
  return html`${years.map((year) => {
    const note = p.years.includes(year) ? " · actual" : state.budget?.years.includes(year) ? " · budget" : "";
    return html`<option value="${year}"${raw(year === selected ? " selected" : "")}>${year}${note}</option>`;
  })}`;
}

function updateKpiHint() {
  const metric = getKpi(document.getElementById("kpiMetric").value);
  const year = document.getElementById("kpiYear").value;
  const field = document.getElementById("kpiValueField");
  field.dataset.unit = metric.unit;
  document.getElementById("kpiValue").placeholder = metric.unit === "rand" ? "e.g. 1 200 000" : "e.g. 7.5";

  const way = metric.higherIsBetter
    ? "Higher is better: reached when the figure is at least the target."
    : "Lower is better: reached when the figure is at most the target.";
  const figure = tg.figures.find((f) => f.year === year);
  const value = figure?.values[metric.code] ?? null;
  const current = value !== null
    ? ` ${figure.source === "actual" ? "Actual" : "Budget"} ${year}: ${formatKpiValue(value, metric.unit)}.`
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
  tg.confirmTarget = null;
  fillKpiForm(
    id,
    t.metricName,
    t.year,
    t.unit === "rand" ? numberFormat.format(t.targetValue) : String(t.targetValue),
    `Edit target: ${t.name}`
  );
  renderTargetTable();
  document.getElementById("kpiForm").scrollIntoView({ behavior: "smooth", block: "center" });
  document.getElementById("kpiValue").focus({ preventScroll: true });
}

function targetActionsHtml(id) {
  if (!can("editTargets")) return "";
  if (tg.confirmTarget === id) return html`<td class="action-cell">${confirmDelete("Delete this target?", "target", id)}</td>`;
  return html`<td class="action-cell">
    <button type="button" class="icon-btn" data-action="edit-target" data-id="${id}" title="Edit" aria-label="Edit target"><i class="fas fa-pen"></i></button>
    <button type="button" class="icon-btn danger" data-action="ask-delete-target" data-id="${id}" title="Delete" aria-label="Delete target"><i class="fas fa-trash-can"></i></button>
  </td>`;
}

function targetRowHtml(t) {
  const status = TG_STATUS[t.status];
  const known = getKpi(t.metricName);
  const way = t.higherIsBetter ? "at least" : "at most";
  const dash = raw('<span class="tg-muted">–</span>');
  const checked = t.value !== null;

  return html`<tr class="tg-row${tg.editingId === t.id ? " row-editing" : ""}" data-id="${t.id}">
    <td><span class="cat-name">${t.name}</span><small>${known ? `Target: ${way}` : "Unknown KPI"}</small></td>
    <td class="num">${formatKpiValue(t.targetValue, t.unit)}</td>
    <td class="num">${checked ? kpiFigureHtml(t.value, t.unit, t.source) : dash}</td>
    <td class="num">${checked ? kpiGapHtml(t.gap, t.unit, t.reached) : dash}</td>
    <td>${chip(status.cls, status.label, status.icon)}</td>
    ${targetActionsHtml(t.id)}
  </tr>`;
}

function renderTargetTable() {
  const table = document.getElementById("kpiTable");
  const results = tg.targets;
  document.getElementById("kpiMeta").textContent = state.budget?.outOfDate
    ? `A goal per KPI per financial year. Years with actual figures are checked against them; future years against the budget, which is out of date (the approved history now runs to ${state.processed.years.at(-1)}). Choose a new budget in Forecast & budget.`
    : "A goal per KPI per financial year. Years with actual figures are checked against them; future years are checked against the budget.";

  const checked = results.filter((r) => r.value !== null);
  const badge = document.getElementById("kpiBadge");
  badge.hidden = !results.length;
  setTargetBadge(badge, results.length, checked.length, checked.filter((r) => r.reached).length);

  if (!results.length) {
    table.innerHTML = emptyRow(`No KPI targets yet.${can("editTargets") ? " Add one above." : ""}`);
    return;
  }

  const cols = can("editTargets") ? 6 : 5;
  let lastYear = null;
  const body = results.map((r) => {
    let yearRow = "";
    if (r.year !== lastYear) {
      lastYear = r.year;
      const source = r.value !== null ? (r.source === "actual" ? "checked against the actual figures" : "checked against the budget") : "no actual or budget figures yet";
      yearRow = html`<tr class="tg-year"><td colspan="${cols}"><strong>${r.year}</strong> · ${source}</td></tr>`;
    }
    return html`${yearRow}${targetRowHtml(r)}`;
  });

  table.innerHTML = html`<thead><tr>
      <th>KPI</th><th class="num">Target</th><th class="num">Figure</th><th class="num">Difference</th><th>Status</th>
      ${can("editTargets") ? raw('<th class="action-cell"><span class="sr-only">Actions</span></th>') : ""}
    </tr></thead><tbody>${body}</tbody>`;
}

async function loadTargets() {
  ({ targets: tg.targets, figures: tg.figures } = await getKpiTargets());
}

async function reloadTargets(highlightId) {
  await loadTargets();
  renderTargetTable();
  if (highlightId) document.querySelector(`#kpiTable tr[data-id="${CSS.escape(highlightId)}"]`)?.classList.add("row-new");
  await emit("targets");
}

function rejectField(input, message) {
  input.classList.add("invalid");
  input.focus();
  showToast(message, "error");
}

async function saveTarget(e) {
  e.preventDefault();
  if (tg.busy || !can("editTargets")) return;

  const metric = getKpi(document.getElementById("kpiMetric").value);
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
  tg.busy = true;
  try {
    await withBusy(document.getElementById("kpiSave"), async () => {
      const editing = tg.editingId;
      const saved = editing ? await updateKpiTarget(editing, target) : await createKpiTarget(target);
      resetKpiForm();
      await reloadTargets(saved.id);
      showToast(`${metric.name} target for ${year} ${editing ? "updated" : "added"}.`, "success");
    });
  } catch (err) {
    showToast(err.message, "error");
  } finally {
    tg.busy = false;
  }
}

async function deleteTarget(id) {
  try {
    await deleteKpiTarget(id);
    tg.confirmTarget = null;
    if (tg.editingId === id) resetKpiForm();
    await reloadTargets();
    showToast("Target deleted.", "success");
  } catch (err) {
    showToast(err.message, "error");
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
  return html`<i class="fas ${icon}"></i><span>${p.text}</span>`;
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
  if (tg.confirmRule === rule.id) return html`<div class="rule-actions">${confirmDelete("Delete this rule?", "rule")}</div>`;
  const hidden = raw(rule.id in tg.dirtyRules ? "" : " hidden");
  const del = rule.deletable
    ? raw('<button type="button" class="icon-btn danger" data-action="ask-delete-rule" title="Delete" aria-label="Delete rule"><i class="fas fa-trash-can"></i></button>')
    : lockIcon("This rule has raised warnings before, so it can be switched off but not deleted");
  return html`<div class="rule-actions">
    <button type="button" class="btn-outline btn-small" data-action="undo-rule"${hidden}>Undo</button>
    <button type="button" class="btn-primary btn-small" data-action="save-rule"${hidden}><i class="fas fa-check"></i><span>Save</span></button>
    ${del}
  </div>`;
}

function ruleRowHtml(rule) {
  const disabled = raw(can("editRules") ? "" : " disabled");
  const threshold = tg.dirtyRules[rule.id] ?? String(Number(rule.threshold));
  const preview = rulePreview(rule.value, rule.period, parseThreshold(threshold), rule.active);
  const fieldId = `ruleThreshold-${rule.id}`;

  return html`<div class="rule-row${rule.active ? "" : " inactive"}" data-id="${rule.id}">
    <label class="switch" title="${rule.active ? "On: raises warnings" : "Off: raises no warnings"}">
      <input type="checkbox" class="rule-active"${raw(rule.active ? " checked" : "")}${disabled} aria-label="Rule switched on" />
      <span class="switch-track"></span>
    </label>
    <div class="rule-main">
      <strong>${rule.name}</strong>
      <small>${rule.indicatorName}${rule.active ? "" : " · switched off"}</small>
      <div class="indicator-cats"><span>Affects:</span>${categoryChips(rule.categories)}</div>
    </div>
    <div class="rule-threshold">
      <label for="${fieldId}">Warn above</label>
      <div class="input-affix" data-unit="percent">
        <input type="text" inputmode="decimal" id="${fieldId}" class="rule-threshold-input" value="${threshold}"${disabled} />
      </div>
    </div>
    <p class="rule-preview ${preview.cls}">${previewHtml(preview)}</p>
    ${ruleActionsHtml(rule)}
  </div>`;
}

function renderRules() {
  if (!state.rules || !state.business) return;
  tg.rules = indicatorWarnings(false);
  const list = document.getElementById("ruleList");
  list.innerHTML = tg.rules.length ? html`${tg.rules.map(ruleRowHtml)}` : emptyNote("No warning rules yet.");

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

async function updateRule(rule, changes, message) {
  const row = document.querySelector(`.rule-row[data-id="${CSS.escape(rule.id)}"]`);
  row?.classList.add("busy");
  try {
    await saveRule(rule.id, {
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
    showToast(err.message, "error");
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
    await removeRule(id);
    tg.confirmRule = null;
    delete tg.dirtyRules[id];
    await reloadRules();
    showToast("Rule deleted.", "success");
  } catch (err) {
    tg.confirmRule = null;
    renderRules();
    showToast(err.message, "error");
  }
}

function suggestedRuleName() {
  const name = INDICATOR_NAMES[document.getElementById("ruleIndicator").value];
  const threshold = parseThreshold(document.getElementById("ruleThreshold").value);
  return Number.isNaN(threshold) ? `${name} above threshold` : `${name} above ${formatThreshold(threshold)}`;
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

  tg.busy = true;
  try {
    await withBusy(document.getElementById("ruleAddBtn"), async () => {
      await createRule({ name, indicatorCode: document.getElementById("ruleIndicator").value, threshold, active: true });
      nameInput.value = "";
      thresholdInput.value = "";
      await reloadRules();
      showToast(`Rule "${name}" added and switched on.`, "success");
    });
  } catch (err) {
    nameInput.classList.add("invalid");
    showToast(err.message, "error");
  } finally {
    tg.busy = false;
  }
}

function renderNoTargetHistory() {
  document.getElementById("kpiMeta").textContent =
    "Targets are set per financial year, so they open once the first income statement is approved.";
  document.getElementById("kpiBadge").hidden = true;
  document.getElementById("kpiTable").innerHTML = "";
}

export function setup() {
  showRoleNote("kpiRoleNote", "editTargets", "you can see the targets, but only a Decision Maker can add or change them.");
  showRoleNote("ruleRoleNote", "editRules", "you can see the rules, but only an Approver or Decision Maker can change them.");
  document.getElementById("kpiForm").hidden = !can("editTargets");
  document.getElementById("ruleForm").hidden = !can("editRules");

  document.getElementById("ruleIndicator").innerHTML = html`${Object.entries(INDICATOR_NAMES).map(([code, name]) => html`<option value="${code}">${name}</option>`)}`;
  on("rules", renderRules);

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
}

export async function refresh() {
  if (!state.business) return;
  try {
    await loadTargets();
    tg.loaded = true;
    renderRules();
    document.getElementById("kpiForm").hidden = !can("editTargets") || !state.processed;
    if (!state.processed) return renderNoTargetHistory();
    if (!tg.editingId) resetKpiForm();
    renderTargetTable();
  } catch (err) {
    showToast(err.message, "error");
  }
}

export function onShow() {
  if (!tg.loaded || !state.processed) return;
  renderTargetTable();
  updateKpiHint();
}
