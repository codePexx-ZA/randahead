// Dashboard: sections, metric cards and statements
const ROLE_LABELS = { submitter: "Submitter", approver: "Approver", decision_maker: "Decision Maker" };
const ROLE_HOME = { submitter: "upload", approver: "approvals", decision_maker: "dashboard" };
const ROLE_PERMISSIONS = {
  upload: ["submitter"],
  chooseBudget: ["decision_maker"],
  decide: ["approver"],
  editTargets: ["decision_maker"],
  editRules: ["approver", "decision_maker"],
  manageUsers: ["decision_maker", "approver"],
  editBusiness: ["decision_maker"],
  deleteAnyUser: [],
};
const DEMO_ACCOUNTS = { submitter: "submitter@demo.co.za", approver: "approver@demo.co.za", decision_maker: "owner@demo.co.za" };
const KIND_LABELS = { income: "Income", expense: "Expense", total: "Total" };
const METRICS_KEY = "dashboardMetrics";
const DEFAULT_METRICS = ["line:Sales", "line:Rent", "line:Electricity", "total:net_profit"];

const state = {
  user: null,
  business: null,
  submission: null,
  processed: null,
  budget: null,
  metricOptions: [],
  selectedMetrics: [],
  yearIndex: 0,
  search: "",
};

const randFormat = new Intl.NumberFormat("en-ZA", { style: "currency", currency: "ZAR", maximumFractionDigits: 0 });
const numberFormat = new Intl.NumberFormat("en-ZA", { maximumFractionDigits: 0 });

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (c) => (
    { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]
  ));
}

function formatSheet(value) {
  if (value === null) return "–";
  return value < 0 ? `(${numberFormat.format(-value)})` : numberFormat.format(value);
}

const views = document.querySelectorAll(".view");
const navItems = document.querySelectorAll(".nav-item");

function homeView() {
  if (state.user?.isAdmin) return "dashboard";
  return ROLE_HOME[state.user?.role] || "dashboard";
}

function userRoleLabel(user) {
  return user.isAdmin ? "Admin" : ROLE_LABELS[user.role] || user.role;
}

function showView(name) {
  const view = document.getElementById(`view-${name}`) || document.getElementById(`view-${homeView()}`);
  const viewName = view.id.replace("view-", "");
  const opening = !view.classList.contains("active");

  views.forEach((v) => v.classList.toggle("active", v === view));
  navItems.forEach((item) => item.classList.toggle("active", item.dataset.view === viewName));

  document.getElementById("viewTitle").textContent = view.dataset.title;
  document.getElementById("viewSubtitle").textContent =
    view.dataset.subtitle || (state.business?.name ?? "");
  document.getElementById("yearPicker").hidden = viewName !== "dashboard" || !state.submission;
  document.title = `${view.dataset.title} | RandAhead`;
  if (opening && viewName === "activity") refreshActivity();

  document.body.classList.remove("sidebar-open");
}

function setupNav() {
  window.addEventListener("hashchange", () => showView(location.hash.slice(1)));
  showView(location.hash.slice(1) || homeView());

  document.getElementById("menuToggle").addEventListener("click", () => document.body.classList.toggle("sidebar-open"));
  document.getElementById("sidebarBackdrop").addEventListener("click", () => document.body.classList.remove("sidebar-open"));
}

function initials(name) {
  return name
    .split(" ")
    .filter(Boolean)
    .map((part) => part[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();
}

function renderUserChip() {
  const { user } = state;
  if (!user) return;
  document.getElementById("userName").textContent = user.fullName;
  document.getElementById("userRole").textContent = userRoleLabel(user);
  document.getElementById("userAvatar").textContent = initials(user.fullName);  document.getElementById("supportName").value = user.fullName;
  document.getElementById("supportEmail").value = user.email;
}

function setupUser() {
  state.user = Session.get()?.user ?? null;
  if (!state.user) {
    window.location.replace("index.html");
    return false;
  }
  if (state.user.mustChangePassword) {
    window.location.replace("password.html?change");
    return false;
  }
  renderUserChip();
  refreshUser();

  document.getElementById("signOutBtn").addEventListener("click", () => {
    Session.clear();
    window.location.href = "index.html";
  });
  return true;
}

async function refreshUser() {
  try {
    const user = await Api.getCurrentUser();
    if (!user) return;
    const roleChanged = user.role !== state.user.role || Boolean(user.isAdmin) !== Boolean(state.user.isAdmin);
    Session.update(user);
    if (user.mustChangePassword) {
      window.location.replace("password.html?change");
      return;
    }
    if (roleChanged) {
      window.location.reload();
      return;
    }
    state.user = { ...state.user, ...user };
    renderUserChip();
  } catch {
    return;
  }
}

function can(action) {
  if (!state.user) return false;
  return Boolean(state.user.isAdmin) || ROLE_PERMISSIONS[action].includes(state.user.role);
}

function showRoleNote(noteId, action, text) {
  if (can(action)) return;
  const role = state.user ? userRoleLabel(state.user) : "a guest";
  const needed = ROLE_PERMISSIONS[action][0];
  const demo = CONFIG.SHOW_DEMO_ACCOUNTS ? ` To try it, sign in as ${DEMO_ACCOUNTS[needed]} (password demo123).` : "";
  const note = document.getElementById(noteId);
  note.querySelector("span").textContent = `You're signed in as ${role}: ${text}${demo}`;
  note.hidden = false;
}

function renderYearSelect() {
  const select = document.getElementById("yearSelect");
  select.innerHTML = state.submission.years
    .map((year, i) => `<option value="${i}"${i === state.yearIndex ? " selected" : ""}>${escapeHtml(year)}</option>`)
    .join("");
}

function setupYearPicker() {
  const select = document.getElementById("yearSelect");
  select.addEventListener("change", () => {
    state.yearIndex = Number(select.value);
    document.querySelectorAll(".metric-card").forEach((card) => updateMetricCard(card, false));
    renderSubmitted();
  });
}

function loadMetricSelection() {
  const saved = JSON.parse(localStorage.getItem(METRICS_KEY) || "null");
  const valid = (id) => state.metricOptions.some((o) => o.id === id);
  if (Array.isArray(saved) && saved.length === 4 && saved.every(valid)) return saved;
  return DEFAULT_METRICS.map((id, i) => (valid(id) ? id : state.metricOptions[i].id));
}

function saveMetricSelection() {
  localStorage.setItem(METRICS_KEY, JSON.stringify(state.selectedMetrics));
}

function metricOptionsHtml(selectedId) {
  const groups = [["Income statement lines", "line"], ["Totals", "total"]];
  return groups
    .map(([label, group]) => {
      const options = state.metricOptions
        .filter((o) => o.group === group)
        .map((o) => `<option value="${escapeHtml(o.id)}"${o.id === selectedId ? " selected" : ""}>${escapeHtml(o.label)}</option>`)
        .join("");
      return `<optgroup label="${label}">${options}</optgroup>`;
    })
    .join("");
}

function renderMetrics() {
  const wrap = document.getElementById("metrics");
  wrap.innerHTML = "";

  state.selectedMetrics.forEach((id, slot) => {
    const card = document.createElement("div");
    card.className = "metric-card";
    card.dataset.slot = slot;
    card.innerHTML = `
      <div class="select-wrap">
        <select class="metric-select" aria-label="Metric ${slot + 1}">${metricOptionsHtml(id)}</select>
      </div>
      <div class="metric-value">R 0</div>
      <div class="metric-change"></div>
      <div class="metric-kind"></div>`;

    card.querySelector("select").addEventListener("change", (e) => {
      state.selectedMetrics[slot] = e.target.value;
      saveMetricSelection();
      updateMetricCard(card, true);
    });

    wrap.appendChild(card);
    updateMetricCard(card, true);
  });
}

function updateMetricCard(card, fromZero) {
  const option = state.metricOptions.find((o) => o.id === state.selectedMetrics[card.dataset.slot]);
  const i = state.yearIndex;
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
    changeEl.innerHTML = `<i class="fas fa-arrow-${up ? "up" : "down"}"></i> ${Math.abs(pct).toFixed(1)}% vs ${escapeHtml(years[i - 1])}`;
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
  return i === state.yearIndex ? " col-selected" : "";
}

function headingRow(heading, cols) {
  return `<tr class="row-heading"><td colspan="${cols}"><span class="heading-text">${escapeHtml(heading)}</span></td></tr>`;
}

function yearHeaderCells(sources) {
  return state.submission.years
    .map((year, i) => {
      const source = sources?.find((s) => s.years.includes(year));
      const title = source ? ` title="From ${escapeHtml(source.fileName)}"` : "";
      return `<th class="num${selectedCol(i)}"${title}>${escapeHtml(year)}</th>`;
    })
    .join("");
}

function valueCells(values, format) {
  return values.map((v, i) => `<td class="num${selectedCol(i)}">${format(v)}</td>`).join("");
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

  const rows = s.rows
    .map((row) => {
      if (row.heading) return headingRow(row.heading, cols);
      const cls = row.subtotal ? "row-subtotal" : "row-line";
      return `<tr class="${cls}" data-search="${escapeHtml(row.label.toLowerCase())}">
        <td>${escapeHtml(row.label)}</td>${valueCells(row.values, formatSheet)}</tr>`;
    })
    .join("");

  const table = document.getElementById("submittedTable");
  table.innerHTML = `<thead><tr><th>Line (as in file)</th>${yearHeaderCells(combined ? s.sources : null)}</tr></thead><tbody>${rows}</tbody>`;
  applySearch();
  scrollToSelectedYear(table);
}

function budgetCells(actuals, pick, kind) {
  const b = state.budget;
  const lastActual = actuals[actuals.length - 1];
  const methodCell = (method) => `<td class="num col-method">${numberFormat.format(pick(b.methods[method])[0])}</td>`;

  const value = pick(b)[0];
  const pct = percentChange(value, lastActual);

  return `<td class="num col-actual">${numberFormat.format(lastActual)}</td>
    ${methodCell("moving_average")}${methodCell("exponential_smoothing")}
    <td class="num forecast-cell col-budget ${changeClass(pct, kind)}">
      <span class="fc-value">${numberFormat.format(value)}</span>
      <small class="fc-change">(${pctText(pct)})</small></td>`;
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

  const rows = PROCESSED_LAYOUT.map((item) => {
    if (item.heading) return headingRow(item.heading, 5);

    if (item.subtotal) {
      const name = SUBTOTAL_NAMES[item.subtotal];
      return `<tr class="row-subtotal" data-search="${escapeHtml(name.toLowerCase())}">
        <td>${escapeHtml(name)}</td>${budgetCells(p.subtotals[item.subtotal], (f) => f.subtotals[item.subtotal], "total")}</tr>`;
    }

    const category = getCategory(item.category);
    const sources = p.sources[item.category];
    const sourceText = sources.length ? `from: ${sources.join(", ")}` : "no lines in this upload";
    const team = category.teamManaged
      ? `<span class="tag" title="Set by the business; no external indicator applies">Team-managed</span>`
      : "";
    const searchText = `${category.name} ${sources.join(" ")}`.toLowerCase();

    return `<tr class="row-line${sources.length ? "" : " row-empty-category"}" data-search="${escapeHtml(searchText)}">
      <td>
        <span class="cat-name">${escapeHtml(category.name)}</span>${team}
        <small class="cat-sources">${escapeHtml(sourceText)}</small>
      </td>${budgetCells(p.totals[item.category], (f) => f.totals[item.category], category.kind)}</tr>`;
  }).join("");

  const header = `<th>Category (R)</th>
    <th class="num col-actual">${escapeHtml(lastYear)}<small>Actual</small></th>
    <th class="num col-method">MA<small>${escapeHtml(budgetYears)}</small></th>
    <th class="num col-method">ES<small>${escapeHtml(budgetYears)}</small></th>
    <th class="num col-budget">Budget<small>${escapeHtml(budgetYears)}${eachYear}</small></th>`;

  const table = document.getElementById("processedTable");
  table.innerHTML = `<thead><tr>${header}</tr></thead><tbody>${rows}</tbody>`;
  applySearch();
  renderDashboardTargets();
}

function renderNoBudget(lastYear) {
  document.getElementById("processedMeta").textContent = "No budget chosen yet";
  const badge = document.getElementById("processedBadge");
  badge.className = "badge draft";
  badge.textContent = "No budget";
  badge.removeAttribute("title");
  document.getElementById("processedNoteYear").textContent = lastYear;
  document.getElementById("processedTable").innerHTML =
    '<tbody><tr class="row-empty"><td>Run the forecasts and choose a budget in Forecast &amp; budget to see it here.</td></tr></tbody>';
  renderDashboardTargets();
}

function targetProgress(r) {
  if (!r.figure) return 0;
  const { value } = r.figure;
  const goal = r.target.targetValue;
  if (goal <= 0 || value <= 0) return r.reached ? 1 : 0;
  const ratio = r.metric.better === "higher" ? value / goal : goal / value;
  return Math.min(Math.max(ratio, 0), 1);
}

function dashTargetCardHtml(r) {
  const { metric, target } = r;
  const status = TG_STATUS[r.status];
  const unit = metric ? metric.unit : target.measurementUnit;
  const name = metric ? metric.name : target.metricName;
  const way = metric ? (metric.better === "higher" ? "at least" : "at most") : "";
  const figure = r.figure
    ? `${formatKpiValue(r.figure.value, unit)}<small>${r.figure.source === "actual" ? "Actual" : "Budget"}</small>`
    : '<span class="tg-muted">No figures yet</span>';
  const gap = r.figure ? `<span class="tg-gap ${r.reached ? "good" : "bad"}">${formatGap(r.gap, unit)}</span>` : "";

  return `<div class="dash-kpi ${status.cls}">
    <div class="dash-kpi-head">
      <span class="dash-kpi-name">${escapeHtml(name)}</span>
      <span class="dash-kpi-year">${escapeHtml(r.year)}</span>
    </div>
    <div class="dash-kpi-figure">${figure}</div>
    <div class="dash-kpi-bar"><span data-fill="${targetProgress(r)}"></span></div>
    <div class="dash-kpi-target">Target ${way} ${formatKpiValue(target.targetValue, unit)} ${gap}</div>
    ${chip(status.cls, status.label, status.icon)}
  </div>`;
}

function renderDashboardTargets() {
  if (!state.processed || !tg.loaded) return;
  const list = document.getElementById("dashKpiList");
  const badge = document.getElementById("dashKpiBadge");
  const yem = state.business.yearEndMonth;
  const results = tg.targets.map((t) => evaluateTarget(t, state.processed, state.budget, yem));

  const manage = can("editTargets")
    ? 'Add or change targets in <a href="#targets">Targets &amp; rules</a>.'
    : 'Targets are set by the Decision Maker in <a href="#targets">Targets &amp; rules</a>.';
  document.getElementById("dashKpiNote").innerHTML =
    `Years with actual figures are checked against them; future years against the budget. ${manage}`;

  if (!results.length) {
    document.getElementById("dashKpiMeta").textContent = "No targets set yet.";
    badge.hidden = true;
    list.innerHTML = '<p class="ap-empty"><i class="fas fa-circle-info"></i><span>No KPI targets yet.</span></p>';
    return;
  }

  const order = (r) => (r.metric ? KPI_METRICS.indexOf(r.metric) : KPI_METRICS.length);
  results.sort((a, b) => b.target.targetPeriod.localeCompare(a.target.targetPeriod) || order(a) - order(b));

  const years = [...new Set(results.map((r) => r.year))].reverse();
  document.getElementById("dashKpiMeta").textContent =
    `${results.length} target${results.length === 1 ? "" : "s"} for ${yearsText(years)}`;

  const checked = results.filter((r) => r.figure);
  const reached = checked.filter((r) => r.reached).length;
  badge.hidden = false;
  badge.className = `badge ${checked.length && reached === checked.length ? "approved" : "warning"}`;
  badge.textContent = checked.length ? `${reached} of ${checked.length} on target` : `${results.length} set`;

  list.innerHTML = results.map(dashTargetCardHtml).join("");
  list.querySelectorAll(".dash-kpi-bar span").forEach((bar) => {
    bar.style.width = `${Number(bar.dataset.fill) * 100}%`;
  });
}

function applySearch() {
  const q = state.search;

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
      body.insertAdjacentHTML(
        "beforeend",
        `<tr class="row-no-results"><td colspan="${cols}">No lines match "${escapeHtml(q)}"</td></tr>`
      );
    }
  });
}

function setupSearch() {
  document.getElementById("searchInput").addEventListener("input", (e) => {
    state.search = e.target.value.trim().toLowerCase();
    applySearch();
  });
}

function setupSupportForm() {
  const form = document.getElementById("supportForm");
  const btn = document.getElementById("supportBtn");

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const fields = ["supportName", "supportEmail", "supportMessage"].map((id) => document.getElementById(id));
    const invalid = fields.filter((f) => !f.checkValidity() || f.value.trim() === "");
    fields.forEach((f) => f.classList.toggle("invalid", invalid.includes(f)));

    if (invalid.length) {
      showToast("Please fill in your name, a valid email and a message.", "error");
      return;
    }

    btn.disabled = true;
    await new Promise((r) => setTimeout(r, 700));
    btn.disabled = false;
    document.getElementById("supportMessage").value = "";
    showToast("Message sent. We'll get back to you within one business day.", "success");
  });
}

function historyYears() {
  return state.processed?.years ?? [];
}

function renderNoHistory() {
  const next = can("upload")
    ? 'Upload your first one under <a href="#upload">Upload statement</a>; an Approver then approves it.'
    : can("manageUsers")
      ? 'A Submitter uploads it and an Approver approves it: add them under <a href="#settings">Users &amp; business</a>.'
      : "A Submitter uploads it and an Approver approves it.";
  document.getElementById("metrics").innerHTML =
    `<p class="ap-empty dash-empty"><i class="fas fa-circle-info"></i><span>No approved income statement yet. ${next}</span></p>`;
  ["submitted", "processed"].forEach((id) => {
    document.getElementById(`${id}Meta`).textContent = "Nothing approved yet";
    document.getElementById(`${id}Badge`).hidden = true;
    document.getElementById(`${id}Table`).innerHTML = "";
  });
  document.getElementById("dashKpiMeta").textContent = "Checked once the first upload is approved.";
  document.getElementById("dashKpiBadge").hidden = true;
  document.getElementById("dashKpiList").innerHTML = "";
}

async function loadData() {
  try {
    const { business, submission } = await Api.getLatestSubmission();
    state.business = business;
    state.submission = submission;
    state.processed = submission ? processStatement(submission) : null;
    state.budget = submission ? await Api.getLatestBudget(state.processed, state.business) : null;

    document.getElementById("sidebarBusiness").textContent = business.name;
    showView(location.hash.slice(1) || homeView());

    if (submission) {
      state.metricOptions = buildMetricOptions(submission, state.processed);
      state.yearIndex = submission.years.length - 1;
      state.selectedMetrics = loadMetricSelection();
      renderYearSelect();
      renderMetrics();
      renderSubmitted();
      renderProcessed();
    } else {
      renderNoHistory();
    }
    loadForecast();
    refreshApprovals();
    refreshTargets();
    refreshSettings();
  } catch (err) {
    showToast(err.message || "Could not load the income statements.", "error");
  }
}

async function init() {
  if (!setupUser()) return;
  setupActivity();
  setupNav();
  setupSearch();
  setupYearPicker();
  setupSupportForm();
  setupUpload();
  setupForecast();
  setupApprovals();
  setupTargets();
  setupSettings();
  await loadData();
}

init();
