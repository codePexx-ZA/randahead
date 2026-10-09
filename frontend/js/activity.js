// Activity log section: who did what and when; the admin sees the whole business, everyone else only their own actions
const AC_AREAS = {
  account: { label: "Sign-ins and account", icon: "fa-right-to-bracket" },
  upload: { label: "Uploads", icon: "fa-file-arrow-up" },
  forecast: { label: "Forecasts", icon: "fa-chart-line" },
  budget: { label: "Budgets", icon: "fa-scale-balanced" },
  target: { label: "KPI targets", icon: "fa-bullseye" },
  rule: { label: "Warning rules", icon: "fa-triangle-exclamation" },
  business: { label: "Business details", icon: "fa-building" },
  user: { label: "Users", icon: "fa-user" },
};
const AC_ALERT_ACTIONS = ["sign_in_failed", "rejected", "deleted", "switched_off"];
const ac = { data: null, request: 0 };

const acDateFormat = new Intl.DateTimeFormat("en-ZA", { day: "numeric", month: "short", year: "numeric" });
const acTimeFormat = new Intl.DateTimeFormat("en-ZA", { hour: "2-digit", minute: "2-digit" });

function acIsAdmin() {
  return Boolean(state.user?.isAdmin);
}

function acFilters() {
  return {
    area: document.getElementById("acArea").value,
    user: acIsAdmin() ? document.getElementById("acPerson").value : "",
    from: document.getElementById("acFrom").value,
    to: document.getElementById("acTo").value,
  };
}

function acValueHtml(d) {
  if (d.before != null && d.after != null) {
    return `<span class="ac-before">${escapeHtml(d.before)}</span><i class="fas fa-arrow-right"></i>${escapeHtml(d.after)}`;
  }
  return d.after != null ? escapeHtml(d.after) : `<span class="ac-before">${escapeHtml(d.before)}</span>`;
}

function acDetailsHtml(details) {
  if (!details.length) return "";
  const items = details
    .map((d) => `<li><span class="ac-field">${escapeHtml(d.field)}:</span> ${acValueHtml(d)}</li>`)
    .join("");
  return `<ul class="ac-details">${items}</ul>`;
}

function acRowHtml(e) {
  const area = AC_AREAS[e.area] || { label: e.area, icon: "fa-circle" };
  const at = new Date(e.at);
  const alert = AC_ALERT_ACTIONS.includes(e.action) ? " ac-alert" : "";
  const who = acIsAdmin() ? `<td class="ac-who">${escapeHtml(e.who)}</td>` : "";
  return `<tr class="ac-row${alert}">
    <td class="ac-when">${acDateFormat.format(at)}<small>${acTimeFormat.format(at)}</small></td>
    ${who}
    <td>
      <div class="ac-entry">
        <span class="ac-icon" title="${escapeHtml(area.label)}"><i class="fas ${area.icon}"></i></span>
        <div><span class="ac-summary">${escapeHtml(e.summary)}</span>${acDetailsHtml(e.details)}</div>
      </div>
    </td>
  </tr>`;
}

function renderPeople(people) {
  const select = document.getElementById("acPerson");
  const current = select.value;
  select.innerHTML = '<option value="">Everyone</option>' + people
    .map((p) => `<option value="${escapeHtml(p.id)}">${escapeHtml(p.name)}</option>`)
    .join("");
  select.value = current;
}

function renderActivity() {
  const { entries, more, people } = ac.data;
  const filtered = Object.values(acFilters()).some(Boolean);
  if (people) renderPeople(people);

  const badge = document.getElementById("acBadge");
  badge.hidden = false;
  badge.textContent = more ? `Newest ${entries.length}` : `${entries.length} entr${entries.length === 1 ? "y" : "ies"}`;

  document.getElementById("acNote").textContent = more
    ? `Showing the newest ${entries.length} entries. Narrow the area or dates to see older ones.`
    : "Entries are kept for good and can't be edited or deleted. Times are shown in your computer's time zone.";

  const table = document.getElementById("acTable");
  if (!entries.length) {
    const text = filtered ? "No activity matches these filters." : "No activity yet.";
    table.innerHTML = `<tbody><tr class="row-empty"><td>${text}</td></tr></tbody>`;
    return;
  }
  const whoHead = acIsAdmin() ? "<th>Who</th>" : "";
  table.innerHTML = `<thead><tr><th>When</th>${whoHead}<th>What happened</th></tr></thead>
    <tbody>${entries.map(acRowHtml).join("")}</tbody>`;
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
    const data = await Api.getActivity(filters);
    if (request !== ac.request) return;
    ac.data = data;
    renderActivity();
  } catch (err) {
    if (request !== ac.request) return;
    showToast(err.message || "Could not load the activity log.", "error");
  }
}

function setupActivity() {
  const admin = acIsAdmin();
  document.getElementById("view-activity").dataset.subtitle = admin
    ? "Everything that happens in this business"
    : "What you've done in RandAhead";
  document.getElementById("acHeading").textContent = admin ? "All activity" : "Your activity";
  document.getElementById("acMeta").textContent = admin
    ? "Sign-ins, uploads, decisions and every change, by everyone in the business."
    : "Your sign-ins, uploads, decisions and changes. Only the admin sees everyone's activity.";
  document.getElementById("acPersonField").hidden = !admin;

  document.getElementById("acArea").innerHTML = '<option value="">All areas</option>' + Object.entries(AC_AREAS)
    .map(([value, area]) => `<option value="${value}">${escapeHtml(area.label)}</option>`)
    .join("");

  const form = document.getElementById("acFilters");
  form.addEventListener("change", refreshActivity);
  form.addEventListener("submit", (e) => e.preventDefault());
  document.getElementById("acRefresh").addEventListener("click", refreshActivity);
  document.getElementById("acClear").addEventListener("click", () => {
    form.reset();
    refreshActivity();
  });
}
