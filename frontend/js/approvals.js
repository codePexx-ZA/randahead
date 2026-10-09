// Approvals section: approve or reject uploads and budgets
const AP_HISTORY_SIZE = 6;

const ap = {
  pending: [],
  uploads: [],
  warnings: [],
};

function checksHtml(items) {
  return items
    .map((i) => `<li class="check ${i.level}"><i class="fas ${CHECK_ICONS[i.level]}"></i><span>${escapeHtml(i.text)}</span></li>`)
    .join("");
}

function decisionHtml(id, placeholder) {
  if (!can("decide")) {
    return `<p class="ap-locked"><i class="fas fa-lock"></i>Only an approver can approve or reject this.</p>`;
  }
  const fieldId = `apComment-${escapeHtml(id)}`;
  return `<div class="ap-decision">
    <label class="ap-comment-label" for="${fieldId}">Comment <small>needed to reject, optional to approve</small></label>
    <textarea id="${fieldId}" class="ap-comment" rows="2" maxlength="1000" placeholder="${escapeHtml(placeholder)}"></textarea>
    <div class="ap-buttons">
      <button type="button" class="btn-reject" data-decision="rejected"><i class="fas fa-xmark"></i><span>Reject</span></button>
      <button type="button" class="btn-primary btn-approve" data-decision="approved"><i class="fas fa-check"></i><span>Approve</span></button>
    </div>
  </div>`;
}

function emptyHtml(text) {
  return `<p class="ap-empty"><i class="fas fa-circle-check"></i><span>${escapeHtml(text)}</span></p>`;
}

function mappingText(parts) {
  if (!parts.length) return '<span class="map-fixed">Not used</span>';
  if (parts.length === 1) return escapeHtml(getCategory(parts[0].category).name);
  return parts.map((p) => `${escapeHtml(getCategory(p.category).name)} <strong>${p.percent}%</strong>`).join(" · ");
}

function uploadTableHtml(u, subtotals) {
  const cols = u.periods.length + 2;
  const head = `<thead><tr><th>Line (as in file)</th>${u.periods.map((y) => `<th class="num">${escapeHtml(y)}</th>`).join("")}<th>Category</th></tr></thead>`;
  const body = u.rows.map((r) => {
    const cells = `<td>${escapeHtml(r.label)}</td>${valueCellsFor(r)}`;
    if (r.kind === "heading") {
      return `<tr class="row-heading"><td colspan="${cols}"><span class="heading-text">${escapeHtml(r.label)}</span></td></tr>`;
    }
    if (r.kind === "subtotal") {
      const status = subtotals[r.subtotal]?.matches
        ? chip("ok", "Matches", "fa-check")
        : chip("bad", "Doesn't match", "fa-xmark", "Our recalculation from the categories differs from the file");
      return `<tr class="row-subtotal">${cells}<td>${status}</td></tr>`;
    }
    if (r.kind === "total") {
      return `<tr class="row-total">${cells}<td><span class="map-fixed">Total line, skipped</span></td></tr>`;
    }
    return `<tr class="row-line">${cells}<td class="ap-map">${mappingText(r.mapping || [])}</td></tr>`;
  }).join("");

  return `<table class="statement ap-table">${head}<tbody>${body}</tbody></table>`;
}

function uploadItemHtml(u) {
  const change = historyChange(ap.uploads, u);
  const review = reviewUpload(u, change);
  const fixes = ap.uploads.find((x) => x.id === u.replacesId && x.status === "rejected");
  const lineCount = u.rows.filter((r) => r.kind === "line").length;

  let effect = change.before.years.length
    ? `Once approved, its years join the history behind the dashboard and forecasts (${yearList(change.after.years)}); for a year in more than one upload, the newest upload is used.`
    : "Once approved, it becomes the history behind the forecasts and the dashboard.";
  if (fixes) effect = `Corrected version of ${fixes.fileName}, which was rejected. ${effect}`;

  return `<div class="ap-item" data-kind="upload" data-id="${escapeHtml(u.id)}">
    <div class="ap-item-head">
      <span class="ap-icon"><i class="fas fa-file-lines"></i></span>
      <div class="ap-item-title">
        <strong>${escapeHtml(u.fileName)}</strong>
        <small>${escapeHtml(yearList(u.periods))} · ${lineCount} lines · ${escapeHtml(SCALE_LABELS[u.scale])} · uploaded by ${escapeHtml(u.submittedBy)}, ${formatDate(u.submittedOn)}</small>
      </div>
      <span class="badge submitted">Waiting for approval</span>
    </div>
    <p class="ap-effect"><i class="fas fa-rotate"></i><span>${escapeHtml(effect)}</span></p>
    <ul class="checks ap-checks">${checksHtml(review.items)}</ul>
    <details class="ap-details">
      <summary><i class="fas fa-chevron-right"></i>Show the statement and its categories</summary>
      <div class="table-wrap">${uploadTableHtml(u, review.subtotals)}</div>
    </details>
    ${decisionHtml(u.id, "e.g. Interest paid is missing for FY2023. Please add it and upload again.")}
  </div>`;
}

function budgetChecks(b) {
  const triggered = ap.warnings.filter((w) => w.triggered);
  const items = triggered.map((w) => {
    const names = w.categories.map((code) => getCategory(code).name).join(", ");
    return {
      level: "warning",
      text: `${w.indicatorName} was ${w.value.toFixed(1)}% in ${w.period}, above its ${w.threshold}% threshold: review ${names}. The figures were not changed.`,
    };
  });
  if (b.outOfDate) {
    items.unshift({
      level: "warning",
      text: `Out of date: the approved history now runs to ${state.processed.years.at(-1)}. Reject it so the decision maker chooses a new budget.`,
    });
  }
  if (ap.warnings.length && triggered.length < ap.warnings.length) {
    const n = ap.warnings.length;
    items.push({
      level: "ok",
      text: triggered.length
        ? "The other warning rules are within their thresholds."
        : `All ${n} active warning rule${n === 1 ? " is" : "s are"} within ${n === 1 ? "its threshold" : "their thresholds"}.`,
    });
  } else if (!ap.warnings.length) {
    items.push({ level: "info", text: "Every economic warning rule is switched off, so no indicators were checked." });
  }
  items.push({
    level: "info",
    text: "Revenue and employee costs are team-managed: the business usually sets these itself, so compare them with its plans.",
  });
  if (b.years.length > 1) {
    items.push({ level: "info", text: `Same figure each year for ${yearsText(b.years)}: no growth or inflation is added.` });
  }
  return items;
}

function budgetTableHtml() {
  const p = state.processed;
  const lastYear = p.years[p.years.length - 1];
  const span = yearsText(state.budget.years);

  const rows = PROCESSED_LAYOUT.map((item) => {
    if (item.heading) {
      return `<tr class="row-heading"><td colspan="5"><span class="heading-text">${escapeHtml(item.heading)}</span></td></tr>`;
    }
    if (item.subtotal) {
      return `<tr class="row-subtotal"><td>${escapeHtml(SUBTOTAL_NAMES[item.subtotal])}</td>
        ${budgetCells(p.subtotals[item.subtotal], (f) => f.subtotals[item.subtotal], "total")}</tr>`;
    }
    const category = getCategory(item.category);
    const team = category.teamManaged ? '<span class="tag">Team-managed</span>' : "";
    return `<tr class="row-line"><td><span class="cat-name">${escapeHtml(category.name)}</span>${team}</td>
      ${budgetCells(p.totals[item.category], (f) => f.totals[item.category], category.kind)}</tr>`;
  }).join("");

  return `<table class="statement ap-table"><thead><tr>
      <th>Category (R)</th>
      <th class="num col-actual">${escapeHtml(lastYear)}<small>Actual</small></th>
      <th class="num col-method">MA<small>${escapeHtml(span)}</small></th>
      <th class="num col-method">ES<small>${escapeHtml(span)}</small></th>
      <th class="num col-budget">Budget<small>${escapeHtml(span)}</small></th>
    </tr></thead><tbody>${rows}</tbody></table>`;
}

function budgetItemHtml(b) {
  const p = state.processed;
  const last = p.years.length - 1;
  const actualExpenses = totalExpenses(Object.fromEntries(
    LINE_CATEGORIES.map((c) => [c.code, [p.totals[c.code][last]]])
  ));
  const figure = (label, value, base, kind) => {
    const pct = percentChange(value, base);
    return `<div><dt>${label}</dt><dd class="${changeClass(pct, kind)}">${randFormat.format(value)}<small>${pctText(pct)}</small></dd></div>`;
  };

  return `<div class="ap-item" data-kind="budget" data-id="${escapeHtml(b.id)}">
    <div class="ap-item-head">
      <span class="ap-icon"><i class="fas fa-scale-balanced"></i></span>
      <div class="ap-item-title">
        <strong>${escapeHtml(describeMethod(b))}</strong>
        <small>Budget ${escapeHtml(yearsText(b.years))} · chosen by ${escapeHtml(b.chosenBy || "a decision maker")}${b.chosenOn ? `, ${formatDate(b.chosenOn)}` : ""}</small>
      </div>
      <span class="badge submitted">Waiting for approval</span>
    </div>
    <dl class="ap-figures">
      ${figure("Revenue", b.totals.revenue[0], p.totals.revenue[last], "income")}
      ${figure("Total expenses", totalExpenses(b.totals), actualExpenses, "expense")}
      ${figure("Net profit", b.subtotals.net_profit[0], p.subtotals.net_profit[last], "total")}
    </dl>
    <p class="ap-figures-note">% change from ${escapeHtml(p.years[last])}'s actual. Green is better for the business, red is worse.</p>
    <ul class="checks ap-checks">${checksHtml(budgetChecks(b))}</ul>
    <details class="ap-details">
      <summary><i class="fas fa-chevron-right"></i>Show every category</summary>
      <div class="table-wrap">${budgetTableHtml()}</div>
    </details>
    ${decisionHtml(b.id, "e.g. Use Moving Average instead: last year's electricity jump was a once-off.")}
  </div>`;
}

function historyEntries() {
  const entries = ap.uploads
    .filter((u) => u.decidedOn)
    .map((u) => ({
      icon: "fa-file-lines",
      name: u.fileName,
      detail: `Upload · ${yearList(uploadPeriods(u))}`,
      decision: u.decision || (u.status === "rejected" ? "rejected" : "approved"),
      replaced: u.status === "superseded",
      by: u.decidedBy,
      on: u.decidedOn,
      comment: u.comment,
    }));

  const b = state.budget;
  if (b && (b.decidedOn || b.approvedOn)) {
    entries.push({
      icon: "fa-scale-balanced",
      name: describeMethod(b),
      detail: `Budget · ${yearsText(b.years)}`,
      decision: b.status === "rejected" ? "rejected" : "approved",
      replaced: false,
      by: b.decidedBy || b.approvedBy,
      on: b.decidedOn || b.approvedOn,
      comment: b.comment,
    });
  }
  return entries.sort((a, b2) => b2.on.localeCompare(a.on)).slice(0, AP_HISTORY_SIZE);
}

function renderHistory() {
  const table = document.getElementById("apHistory");
  const entries = historyEntries();
  if (!entries.length) {
    table.innerHTML = '<tbody><tr class="row-empty"><td>No decisions yet.</td></tr></tbody>';
    return;
  }

  const rows = entries.map((e) => {
    const label = e.decision === "rejected" ? "Rejected" : "Approved";
    const replaced = e.replaced ? "<small>Replaced since</small>" : "";
    const comment = e.comment ? commentRow(3, e.decision, e.by, e.comment) : "";
    return `<tr>
      <td><i class="fas ${e.icon} file-icon"></i>${escapeHtml(e.name)}<small>${escapeHtml(e.detail)}</small></td>
      <td><span class="badge ${e.decision}">${label}</span>${replaced}</td>
      <td>${formatDate(e.on)}<small>${escapeHtml(e.by)}</small></td>
    </tr>${comment}`;
  }).join("");

  table.innerHTML = `<thead><tr><th>Item</th><th>Decision</th><th>Decided</th></tr></thead><tbody>${rows}</tbody>`;
}

function renderApprovals() {
  const budget = state.budget?.status === "submitted" ? state.budget : null;

  document.getElementById("apUploads").innerHTML = ap.pending.length
    ? ap.pending.map(uploadItemHtml).join("")
    : emptyHtml("No uploads waiting. New uploads appear here when a submitter sends them.");
  document.getElementById("apBudgets").innerHTML = budget
    ? budgetItemHtml(budget)
    : emptyHtml("No budget waiting. A budget appears here when a decision maker chooses one in Forecast & budget.");
  renderHistory();

  document.getElementById("apUploadCount").textContent = ap.pending.length;
  document.getElementById("apBudgetCount").textContent = budget ? 1 : 0;
  const waiting = ap.pending.length + (budget ? 1 : 0);
  const navCount = document.getElementById("apNavCount");
  navCount.hidden = waiting === 0;
  navCount.textContent = waiting;
  navCount.title = `${waiting} waiting for approval`;
}

async function refreshApprovals() {
  if (!state.business) return;
  try {
    [ap.pending, ap.uploads, ap.warnings] = await Promise.all([
      Api.getPendingSubmissions(),
      Api.getSubmissions(),
      Api.getIndicatorWarnings(historyYears(), state.business.yearEndMonth),
    ]);
    renderApprovals();
  } catch (err) {
    showToast(err.message || "Could not load the approval queue.", "error");
  }
}

function setItemBusy(item, busy, decision) {
  item.classList.toggle("busy", busy);
  item.querySelectorAll(".ap-buttons button, .ap-comment").forEach((el) => (el.disabled = busy));
  const btn = item.querySelector(`[data-decision="${decision}"] i`);
  if (btn) btn.className = busy ? "fas fa-spinner fa-spin" : `fas ${decision === "approved" ? "fa-check" : "fa-xmark"}`;
}

async function decide(item, decision) {
  const { kind, id } = item.dataset;
  const textarea = item.querySelector(".ap-comment");
  const comments = textarea.value.trim();

  if (decision === "rejected" && !comments) {
    textarea.classList.add("invalid");
    textarea.focus();
    showToast("Add a comment so the submitter knows what to fix.", "error");
    return;
  }

  setItemBusy(item, true, decision);
  try {
    if (kind === "upload") await Api.decideSubmission(id, decision, comments);
    else await Api.decideBudget(id, decision, comments);

    item.classList.add("leaving");
    await new Promise((r) => setTimeout(r, 300));

    if (kind === "upload" && decision === "approved") {
      await loadData();
      showToast("Upload approved. The dashboard and forecasts now use it.", "success", 5000);
    } else if (kind === "upload") {
      await refreshApprovals();
      showToast("Upload rejected. The submitter sees your comment and can fix it.", "success", 5000);
    } else {
      state.budget = await Api.getLatestBudget(state.processed, state.business);
      renderProcessed();
      if (fc.comparison) renderChoices();
      await refreshApprovals();
      showToast(decision === "approved" ? "Budget approved." : "Budget rejected. A new choice is needed in Forecast & budget.", "success", 5000);
    }
    loadUploads();
  } catch (err) {
    showToast(err.message || "Could not save the decision.", "error");
    item.classList.remove("leaving");
    setItemBusy(item, false, decision);
  }
}

function setupApprovals() {
  const view = document.getElementById("view-approvals");

  showRoleNote("apRoleNote", "decide", "you can see what's waiting, but only an Approver can approve or reject.");

  view.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-decision]");
    if (btn && can("decide")) decide(btn.closest(".ap-item"), btn.dataset.decision);
  });
  view.addEventListener("input", (e) => {
    if (e.target.classList.contains("ap-comment")) e.target.classList.remove("invalid");
  });
}
