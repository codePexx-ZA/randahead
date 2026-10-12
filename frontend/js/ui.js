// UI helpers: an auto-escaping html template, the render pieces shared by sections, and busy buttons
import { changeClass, escapeHtml, formatGap, formatKpiValue, formatSheet, numberFormat, pctText, percentChange, randFormat } from "./format.js";
import { getCategory } from "./store.js";

const CHECK_ICONS = { ok: "fa-circle-check", info: "fa-circle-info", warning: "fa-triangle-exclamation", error: "fa-circle-xmark" };
const TEAM_TITLE = "Set by the business; no external indicator applies";

export const TG_STATUS = {
  met: { label: "Met", cls: "ok", icon: "fa-circle-check" },
  missed: { label: "Missed", cls: "bad", icon: "fa-circle-xmark" },
  on_track: { label: "On track", cls: "ok", icon: "fa-circle-check" },
  off_track: { label: "Off track", cls: "warn", icon: "fa-triangle-exclamation" },
  none: { label: "No figures yet", cls: "muted", icon: "fa-hourglass-half" },
};

class Raw {
  constructor(text) {
    this.text = text;
  }

  toString() {
    return this.text;
  }
}

export function raw(text) {
  return new Raw(String(text));
}

function render(value) {
  if (value instanceof Raw) return value.text;
  if (Array.isArray(value)) return value.map(render).join("");
  if (value === null || value === undefined || value === false) return "";
  return escapeHtml(value);
}

export function html(strings, ...values) {
  return raw(strings.reduce((out, text, i) => out + render(values[i - 1]) + text));
}

export async function withBusy(button, fn) {
  button.disabled = true;
  try {
    return await fn();
  } finally {
    button.disabled = false;
  }
}

export function emptyRow(text) {
  return html`<tbody><tr class="row-empty"><td>${text}</td></tr></tbody>`;
}

export function emptyNote(content, icon = "fa-circle-info", extraClass = "") {
  return html`<p class="ap-empty${extraClass ? ` ${extraClass}` : ""}"><i class="fas ${icon}"></i><span>${content}</span></p>`;
}

export function teamTag(title = TEAM_TITLE) {
  return title ? html`<span class="tag" title="${title}">Team-managed</span>` : raw('<span class="tag">Team-managed</span>');
}

export function lockIcon(title) {
  return html`<span class="icon-btn locked" title="${title}"><i class="fas fa-lock"></i></span>`;
}

export function confirmDelete(question, kind, id) {
  const idAttr = id === undefined ? "" : html` data-id="${id}"`;
  return html`<span class="tg-confirm">${question}
      <button type="button" class="btn-reject btn-small" data-action="delete-${kind}"${idAttr}>Delete</button>
      <button type="button" class="btn-outline btn-small" data-action="keep-${kind}">Keep</button></span>`;
}

export function chip(type, text, icon, title = "") {
  const titleAttr = title ? html` title="${title}"` : "";
  return html`<span class="chip ${type}"${titleAttr}><i class="fas ${icon}"></i>${text}</span>`;
}

export function categoryChips(codes) {
  return html`${codes.map((code) => html`<span class="cat-chip">${getCategory(code).name}</span>`)}`;
}

export function headingRow(heading, cols) {
  return html`<tr class="row-heading"><td colspan="${cols}"><span class="heading-text">${heading}</span></td></tr>`;
}

export function columnHead(text, sub, cls = "") {
  return html`<th class="num${cls ? ` ${cls}` : ""}">${text}<small>${sub}</small></th>`;
}

export function changeCell(text, pct, classes) {
  return html`<td class="num forecast-cell ${classes}">
      <span class="fc-value">${text}</span>
      <small class="fc-change">(${pctText(pct)})</small></td>`;
}

export function budgetCells(budget, actuals, pick, kind) {
  const lastActual = actuals[actuals.length - 1];
  const methodCell = (method) => html`<td class="num col-method">${numberFormat.format(pick(budget.methods[method])[0])}</td>`;
  const value = pick(budget)[0];
  const pct = percentChange(value, lastActual);
  return html`<td class="num col-actual">${numberFormat.format(lastActual)}</td>
    ${methodCell("moving_average")}${methodCell("exponential_smoothing")}
    ${changeCell(numberFormat.format(value), pct, `col-budget ${changeClass(pct, kind)}`)}`;
}

export function pctCell(pct, kind) {
  return html`<td class="num forecast-cell ${changeClass(pct, kind)}">${pctText(pct)}</td>`;
}

export function sheetCells(values) {
  return html`${values.map((v) => (v === null
    ? raw('<td class="num cell-blank">blank</td>')
    : html`<td class="num">${formatSheet(v)}</td>`))}`;
}

export function figureItem(label, value, base, kind) {
  const pct = percentChange(value, base);
  return html`<div><dt>${label}</dt><dd class="${changeClass(pct, kind)}">${randFormat.format(value)}<small>${pctText(pct)}</small></dd></div>`;
}

export function kpiFigureHtml(value, unit, source) {
  return html`${formatKpiValue(value, unit)}<small>${source === "actual" ? "Actual" : "Budget"}</small>`;
}

export function kpiGapHtml(gap, unit, reached) {
  return html`<span class="tg-gap ${reached ? "good" : "bad"}">${formatGap(gap, unit)}</span>`;
}

export function setTargetBadge(badge, total, checked, reached) {
  badge.className = `badge ${checked && reached === checked ? "approved" : "warning"}`;
  badge.textContent = checked ? `${reached} of ${checked} on target` : `${total} set`;
}

export function checksHtml(items) {
  return html`${items.map((i) => html`<li class="check ${i.level}"><i class="fas ${CHECK_ICONS[i.level]}"></i><span>${i.text}</span></li>`)}`;
}

export function commentRow(cols, decision, by, comment) {
  return html`<tr class="row-comment"><td colspan="${cols}"><div class="comment-box ${decision}"><i class="fas fa-comment-dots"></i><span><strong>${by}:</strong> ${comment}</span></div></td></tr>`;
}
