import { getActivity } from "./api.js";
import { showToast } from "./toast.js";
import { emptyRow, html } from "./ui.js";

const AC_AREAS = {
  upload: { label: "Uploads", icon: "fa-file-arrow-up" },
  forecast: { label: "Forecasts", icon: "fa-chart-line" },
  budget: { label: "Budgets", icon: "fa-scale-balanced" },
  target: { label: "KPI targets", icon: "fa-bullseye" },
  rule: { label: "Warning rules", icon: "fa-triangle-exclamation" },
  business: { label: "Business details", icon: "fa-building" },
};
const AC_ALERT_ACTIONS = ["deleted", "switched_off"];
const ac = { data: null, request: 0 };

const acDateFormat = new Intl.DateTimeFormat("en-ZA", { day: "numeric", month: "short", year: "numeric" });
const acTimeFormat = new Intl.DateTimeFormat("en-ZA", { hour: "2-digit", minute: "2-digit" });

function acFilters() {
  return {
    area: document.getElementById("acArea").value,
    from: document.getElementById("acFrom").value,
    to: document.getElementById("acTo").value,
  };
}

function acValueHtml(d) {
  if (d.before != null && d.after != null) {
    return html`<span class="ac-before">${d.before}</span><i class="fas fa-arrow-right"></i>${d.after}`;
  }
  return d.after != null ? html`${d.after}` : html`<span class="ac-before">${d.before}</span>`;
}

function acDetailsHtml(details) {
  if (!details.length) return "";
  return html`<ul class="ac-details">${details.map((d) => html`<li><span class="ac-field">${d.field}:</span> ${acValueHtml(d)}</li>`)}</ul>`;
}

function acRowHtml(e) {
  const area = AC_AREAS[e.area] || { label: e.area, icon: "fa-circle" };
  const at = new Date(e.at);
  const alert = AC_ALERT_ACTIONS.includes(e.action) ? " ac-alert" : "";
  return html`<tr class="ac-row${alert}">
    <td class="ac-when">${acDateFormat.format(at)}<small>${acTimeFormat.format(at)}</small></td>
    <td>
      <div class="ac-entry">
        <span class="ac-icon" title="${area.label}"><i class="fas ${area.icon}"></i></span>
        <div><span class="ac-summary">${e.summary}</span>${acDetailsHtml(e.details)}</div>
      </div>
    </td>
  </tr>`;
}

function renderActivity() {
  const { entries, more } = ac.data;
  const filtered = Object.values(acFilters()).some(Boolean);

  const badge = document.getElementById("acBadge");
  badge.hidden = false;
  badge.textContent = more ? `Newest ${entries.length}` : `${entries.length} entr${entries.length === 1 ? "y" : "ies"}`;

  document.getElementById("acNote").textContent = more
    ? `Showing the newest ${entries.length} entries. Narrow the area or dates to see older ones.`
    : "Entries are kept for good and can't be edited or deleted. Times are shown in your computer's time zone.";

  const table = document.getElementById("acTable");
  if (!entries.length) {
    table.innerHTML = emptyRow(filtered ? "No activity matches these filters." : "No activity yet.");
    return;
  }
  table.innerHTML = html`<thead><tr><th>When</th><th>What happened</th></tr></thead>
    <tbody>${entries.map(acRowHtml)}</tbody>`;
}

function renderAi(entries) {
  const report = entries.find((entry) => entry.action === "report");
  const names = entries.filter((entry) => entry.action === "categorised");
  const note = document.getElementById("aiReport");
  note.innerHTML = report
    ? html`<i class="fas fa-circle-info"></i><span>${report.summary}</span>`
    : html`<i class="fas fa-circle-info"></i><span>The Sunday 23:59 report appears here after the first classified name.</span>`;

  const table = document.getElementById("aiTable");
  if (!names.length) {
    table.innerHTML = emptyRow("No names yet. AI runs only when an upload has a label the importer does not recognise.");
    return;
  }
  table.innerHTML = html`<thead><tr><th>When</th><th>Name</th><th>Category</th></tr></thead>
    <tbody>${names.map((entry) => {
      const at = new Date(entry.at);
      const category = entry.details.find((detail) => detail.field === "Category")?.after || "";
      const label = entry.summary.split(" → ")[0];
      return html`<tr class="ac-row"><td class="ac-when">${acDateFormat.format(at)}<small>${acTimeFormat.format(at)}</small></td><td>${label}</td><td>${category}</td></tr>`;
    })}</tbody>`;
}

async function refreshActivity() {
  const filters = acFilters();
  if (filters.from && filters.to && filters.from > filters.to) {
    document.getElementById("acTo").classList.add("invalid");
    showToast("The start date must be on or before the end date.", "error");
    return;
  }
  document.getElementById("acTo").classList.remove("invalid");

  const request = ++ac.request;
  try {
    const [data, ai] = await Promise.all([getActivity(filters), getActivity({ area: "ai" })]);
    if (request !== ac.request) return;
    ac.data = data;
    renderAi(ai.entries);
    renderActivity();
  } catch (err) {
    if (request !== ac.request) return;
    showToast(err.message, "error");
  }
}

export function setup() {
  document.getElementById("acArea").innerHTML = html`<option value="">All areas</option>${Object.entries(AC_AREAS)
    .map(([value, area]) => html`<option value="${value}">${area.label}</option>`)}`;

  const form = document.getElementById("acFilters");
  form.addEventListener("change", refreshActivity);
  form.addEventListener("submit", (e) => e.preventDefault());
  document.getElementById("acRefresh").addEventListener("click", refreshActivity);
  document.getElementById("acClear").addEventListener("click", () => {
    form.reset();
    refreshActivity();
  });
}

export const onShow = refreshActivity;
