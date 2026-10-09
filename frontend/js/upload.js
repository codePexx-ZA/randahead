// Upload statement section: upload, map and send for approval
const UPLOAD_MAX_BYTES = 5 * 1024 * 1024;
const UPLOAD_TYPES = [".xlsx", ".csv"];
const TEMPLATE_FILE_NAME = "RandAhead_income_statement_template.xlsx";
const UNREADABLE_TYPES = [".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".heic", ".tif", ".tiff"];
const UPLOAD_STATUS = {
  draft: "Draft",
  submitted: "Waiting for approval",
  approved: "Approved",
  rejected: "Rejected",
  superseded: "Replaced",
};
const CHECK_ICONS = { ok: "fa-circle-check", info: "fa-circle-info", warning: "fa-triangle-exclamation", error: "fa-circle-xmark" };
const NEW_UPLOAD_ID = "new";

const dateFormat = new Intl.DateTimeFormat("en-ZA", { day: "numeric", month: "short", year: "numeric" });

const upload = {
  preview: null,
  check: null,
  replacesId: null,
  uploads: [],
};

function formatDate(iso) {
  return dateFormat.format(new Date(`${iso}T00:00:00`));
}

function chip(type, text, icon, title = "") {
  const titleAttr = title ? ` title="${escapeHtml(title)}"` : "";
  return `<span class="chip ${type}"${titleAttr}><i class="fas ${icon}"></i>${escapeHtml(text)}</span>`;
}

function commentRow(cols, decision, by, comment) {
  return `<tr class="row-comment"><td colspan="${cols}"><div class="comment-box ${decision}"><i class="fas fa-comment-dots"></i><span><strong>${escapeHtml(by)}:</strong> ${escapeHtml(comment)}</span></div></td></tr>`;
}

function goToStep(step) {
  [1, 2, 3].forEach((n) => (document.getElementById(`uploadStep${n}`).hidden = n !== step));
  document.querySelectorAll("#uploadSteps .step").forEach((li) => {
    const n = Number(li.dataset.step);
    li.classList.toggle("active", n === step);
    li.classList.toggle("done", n < step);
    li.querySelector(".step-dot").innerHTML = n < step ? '<i class="fas fa-check"></i>' : n;
  });
}

async function handleFile(file) {
  if (!file) return;
  const ext = fileExtension(file.name);

  if (UNREADABLE_TYPES.includes(ext)) {
    showToast("PDFs and images can't be read. Export the statement from Excel or your accounting software as .xlsx or .csv.", "error", 6000);
    return;
  }
  if (ext === ".xls") {
    showToast("Old .xls files can't be read. Open the file in Excel and save it as .xlsx (or .csv).", "error", 6000);
    return;
  }
  if (!UPLOAD_TYPES.includes(ext)) {
    showToast("Please choose an Excel (.xlsx) or CSV file.", "error");
    return;
  }
  if (file.size > UPLOAD_MAX_BYTES) {
    showToast("That file is over 5 MB. Remove extra sheets or images and try again.", "error");
    return;
  }

  const zone = document.getElementById("dropzone");
  zone.classList.add("loading");
  try {
    upload.preview = await Api.previewUpload(file);
    renderPreview();
    goToStep(2);
  } catch (err) {
    showToast(err.message || "Could not read the file.", "error", 6000);
  } finally {
    zone.classList.remove("loading");
    document.getElementById("fileInput").value = "";
  }
}

async function downloadTemplate(e) {
  const btn = e.currentTarget;
  btn.disabled = true;
  try {
    const url = URL.createObjectURL(await Api.downloadTemplate());
    const link = document.createElement("a");
    link.href = url;
    link.download = TEMPLATE_FILE_NAME;
    link.click();
    URL.revokeObjectURL(url);
  } catch (err) {
    showToast(err.message || "Could not download the template.", "error");
  } finally {
    btn.disabled = false;
  }
}

function setReplaces(item) {
  upload.replacesId = item ? item.id : null;
  document.getElementById("replaceNote").hidden = !item;
  if (item) {
    document.getElementById("replaceText").textContent =
      `Fixing ${item.fileName} (rejected). Your new file will replace it.`;
  }
}

function setupDropzone() {
  const zone = document.getElementById("dropzone");
  const input = document.getElementById("fileInput");

  input.addEventListener("change", () => handleFile(input.files[0]));

  zone.addEventListener("keydown", (e) => {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      input.click();
    }
  });

  ["dragenter", "dragover"].forEach((type) =>
    zone.addEventListener(type, (e) => {
      e.preventDefault();
      zone.classList.add("dragging");
    })
  );
  ["dragleave", "drop"].forEach((type) =>
    zone.addEventListener(type, () => zone.classList.remove("dragging"))
  );
  zone.addEventListener("drop", (e) => {
    e.preventDefault();
    handleFile(e.dataTransfer.files[0]);
  });
}

function renderPreview() {
  const p = upload.preview;
  const lineCount = p.rows.filter((r) => r.kind === "line").length;
  const scaleNote = p.scaleHint ? `scale found in the file ("${p.scaleHint}")` : "no scale in the file, please confirm";

  document.getElementById("previewFile").textContent = p.fileName;
  document.getElementById("previewMeta").textContent = `${yearList(p.years)} · ${lineCount} lines · ${scaleNote}`;

  document.getElementById("scaleSelect").innerHTML = Object.entries(SCALE_LABELS)
    .map(([value, label]) => `<option value="${value}"${Number(value) === p.scale ? " selected" : ""}>${label}</option>`)
    .join("");

  document.getElementById("confirmCheck").checked = false;
  renderMapTable();
  refreshChecks();
}

function option(value, label, selected) {
  return `<option value="${escapeHtml(value)}"${selected ? " selected" : ""}>${escapeHtml(label)}</option>`;
}

function categoryOptions(selected) {
  return LINE_CATEGORIES.map((c) => option(c.code, c.name, c.code === selected)).join("");
}

function mappingControls(row) {
  const m = row.mapping;
  let choice = "";
  if (m) choice = m.type === "single" ? m.category : m.type;
  else if (row.status === "cross") choice = "split";

  const parts = m?.type === "split"
    ? m.parts
    : (row.candidates || []).map((category) => ({ category, percent: "" }));
  const blank = { category: "", percent: "" };
  const [first = blank, second = blank] = parts;

  return `
    <select class="map-select" aria-label="Category for ${escapeHtml(row.label)}">
      ${option("", "Choose a category…", choice === "")}
      <optgroup label="Categories">${categoryOptions(choice)}</optgroup>
      <optgroup label="Other">
        ${option("split", "Split between two categories…", choice === "split")}
        ${option("ignore", "Not part of the income statement", choice === "ignore")}
      </optgroup>
    </select>
    <div class="split-editor"${choice === "split" ? "" : " hidden"}>
      <div class="split-row">
        <select class="split-cat" aria-label="First category">${option("", "Category…", !first.category)}${categoryOptions(first.category)}</select>
        <input class="split-pct" type="number" min="1" max="99" step="1" value="${first.percent}" placeholder="%" aria-label="Percent to the first category" />
        <span>%</span>
      </div>
      <div class="split-row">
        <select class="split-cat" aria-label="Second category">${option("", "Category…", !second.category)}${categoryOptions(second.category)}</select>
        <output class="split-rest">${first.percent ? 100 - first.percent : "–"}</output>
        <span>%</span>
      </div>
    </div>`;
}

function valueCellsFor(row) {
  return row.values
    .map((v) => (v === null
      ? '<td class="num cell-blank">blank</td>'
      : `<td class="num">${formatSheet(v)}</td>`))
    .join("");
}

function renderMapTable() {
  const p = upload.preview;
  const cols = p.years.length + 3;

  const head = `<thead><tr>
    <th>Line (as in file)</th>
    ${p.years.map((y) => `<th class="num">${escapeHtml(y)}</th>`).join("")}
    <th>Category</th><th>Status</th></tr></thead>`;

  const dataRow = (row, map, status = "") => `<tr class="row-${row.kind}" data-id="${row.id}"><td>${escapeHtml(row.label)}</td>${valueCellsFor(row)}
      <td class="map-cell">${map}</td>
      <td class="status-cell">${status}</td></tr>`;

  const body = p.rows.map((row) => {
    if (row.kind === "heading") {
      return `<tr class="row-heading"><td colspan="${cols}"><span class="heading-text">${escapeHtml(row.label)}</span></td></tr>`;
    }
    if (row.kind === "subtotal") {
      return dataRow(row, `<span class="map-fixed"><i class="fas fa-calculator"></i> Checks ${escapeHtml(SUBTOTAL_NAMES[row.subtotal].toLowerCase())}</span>`);
    }
    if (row.kind === "total") {
      return dataRow(row, '<span class="map-fixed">Total line, not needed</span>', chip("muted", "Skipped", "fa-minus", "RandAhead recalculates totals itself"));
    }
    return dataRow(row, mappingControls(row));
  }).join("");

  const table = document.getElementById("mapTable");
  table.innerHTML = `${head}<tbody>${body}</tbody>`;
  table.querySelectorAll("tr.row-line").forEach(updateRowStatus);
}

function rowStatus(row) {
  const m = row.mapping;
  if (m?.invalid) return chip("warn", "Check the split", "fa-pen", "Pick two different categories and a % from 1 to 99");
  if (!m && row.status === "cross") return chip("warn", "Split or choose one", "fa-code-branch", "This line covers more than one category");
  if (!m) return chip("warn", "Choose a category", "fa-circle-question", "We don't recognise this label yet");
  if (m.type === "ignore") return chip("muted", "Not used", "fa-minus");
  if (row.changed) return chip("info", "Your choice", "fa-user-check", "Remembered for your next upload");
  if (row.status === "remembered") return chip("info", "Remembered", "fa-bookmark", "Chosen on an earlier upload");
  return chip("ok", "Matched", "fa-check", "Recognised from the label");
}

function updateRowStatus(tr) {
  const row = upload.preview.rows[Number(tr.dataset.id)];
  tr.querySelector(".status-cell").innerHTML = rowStatus(row);
  tr.classList.toggle("needs-input", !isResolved(row.mapping));
}

function readMapping(cell) {
  const choice = cell.querySelector(".map-select").value;
  if (!choice) return null;
  if (choice === "ignore") return { type: "ignore" };
  if (choice !== "split") return { type: "single", category: choice };

  const [a, b] = [...cell.querySelectorAll(".split-cat")].map((s) => s.value);
  const pct = Math.round(Number(cell.querySelector(".split-pct").value));
  const pctOk = pct >= 1 && pct <= 99;
  cell.querySelector(".split-rest").textContent = pctOk ? 100 - pct : "–";

  const mapping = { type: "split", parts: [{ category: a, percent: pct }, { category: b, percent: 100 - pct }] };
  if (!a || !b || a === b || !pctOk) mapping.invalid = true;
  return mapping;
}

function onMappingEdit(e) {
  const tr = e.target.closest("tr.row-line");
  if (!tr) return;
  const row = upload.preview.rows[Number(tr.dataset.id)];
  const cell = tr.querySelector(".map-cell");

  cell.querySelector(".split-editor").hidden = cell.querySelector(".map-select").value !== "split";
  row.mapping = readMapping(cell);
  row.changed = true;
  updateRowStatus(tr);
  refreshChecks();
}

function previewChange() {
  const p = upload.preview;
  return historyChange(upload.uploads, { id: NEW_UPLOAD_ID, fileName: p.fileName, periods: p.years, status: "new" });
}

function renderHistoryPlan(change) {
  const { years, sourceOf } = change.after;
  const replaced = Object.fromEntries(change.replaced.map((r) => [r.year, r.from]));
  const cells = years.map((y) => {
    const source = sourceOf[y];
    if (source.id === NEW_UPLOAD_ID) {
      const note = replaced[y] ? `replaces ${replaced[y].fileName}` : "new year";
      return `<td class="plan-new">This file<small>${escapeHtml(note)}</small></td>`;
    }
    return `<td>${escapeHtml(source.fileName)}<small>already approved</small></td>`;
  }).join("");

  document.getElementById("historyPlan").innerHTML = `
    <p class="plan-title">Your history once this file is approved</p>
    <div class="table-wrap">
      <table class="plan-table">
        <thead><tr>${years.map((y) => `<th>${escapeHtml(y)}</th>`).join("")}</tr></thead>
        <tbody><tr>${cells}</tr></tbody>
      </table>
    </div>`;
}

function refreshChecks() {
  const p = upload.preview;
  const change = previewChange();
  const result = checkPreview(p, change);
  upload.check = result;
  renderHistoryPlan(change);

  document.getElementById("previewChecks").innerHTML = checksHtml(result.items);

  document.querySelectorAll("#mapTable tr.row-subtotal").forEach((tr) => {
    const row = p.rows[Number(tr.dataset.id)];
    const cell = tr.querySelector(".status-cell");
    const check = result.subtotals?.[row.subtotal];
    if (!check) {
      cell.innerHTML = chip("muted", "Waiting", "fa-hourglass-half", "Choose every category first");
    } else if (check.matches) {
      cell.innerHTML = chip("ok", "Matches", "fa-check");
    } else {
      const i = check.diffs.findIndex((d) => Math.abs(d) > p.scale);
      cell.innerHTML = chip("bad", `Off by ${randFormat.format(Math.abs(check.diffs[i]))} in ${p.years[i]}`, "fa-xmark");
    }
  });

  updateSubmitState();
}

function updateSubmitState() {
  const confirmed = document.getElementById("confirmCheck").checked;
  document.getElementById("submitUploadBtn").disabled = !(upload.check?.ready && confirmed);
}

async function submitUpload() {
  const btn = document.getElementById("submitUploadBtn");
  const p = upload.preview;
  btn.disabled = true;
  btn.classList.add("busy");

  try {
    const change = previewChange();
    const created = await Api.submitUpload(buildSubmissionPayload(p, upload.replacesId));
    const fixed = upload.uploads.find((u) => u.id === created.replacesId);
    document.getElementById("doneText").textContent =
      `${yearList(p.years)} from ${p.fileName} is waiting for an approver.` +
      (fixed ? ` It's the corrected version of ${fixed.fileName}.` : "") +
      ` Once approved: ${describeChange(change)}` +
      " Until then the dashboard keeps showing the current approved history.";

    setReplaces(null);
    upload.preview = null;
    goToStep(3);
    showToast("Sent for approval.", "success");
    refreshApprovals();
    await loadUploads(created.id);
  } catch (err) {
    showToast(err.message || "Could not send the upload.", "error");
    updateSubmitState();
  } finally {
    btn.classList.remove("busy");
  }
}

function renderUploads(highlightId) {
  const table = document.getElementById("uploadsTable");
  if (!upload.uploads.length) {
    table.innerHTML = '<tbody><tr class="row-empty"><td>No uploads yet. Your first upload will appear here.</td></tr></tbody>';
    return;
  }

  const fixed = new Set(upload.uploads.map((u) => u.replacesId).filter(Boolean));
  const plan = planHistory(upload.uploads.filter((u) => u.status === "approved"));
  const usage = (u) => {
    const used = plan.years.filter((y) => plan.sourceOf[y] === u);
    return used.length ? `Used for ${yearList(used)}` : `Not used: only the latest ${MAX_HISTORY_YEARS} years are kept`;
  };

  const rows = upload.uploads.map((u) => {
    const decided = u.decision || (u.status === "rejected" ? "rejected" : "approved");
    const replacer = u.status === "superseded" && upload.uploads.find((x) => x.id === u.supersededBy);
    let decision = "";
    if (u.status === "rejected") decision = `by ${u.decidedBy}, ${formatDate(u.decidedOn)}`;
    else if (u.decidedOn) decision = `${decided} by ${u.decidedBy}, ${formatDate(u.decidedOn)}`;
    if (u.status === "approved") decision += ` · ${usage(u)}`;
    if (replacer) decision += ` · replaced by ${replacer.fileName}`;

    const canFix = u.status === "rejected" && !fixed.has(u.id) && can("upload");
    const action = canFix
      ? `<button type="button" class="btn-outline btn-small" data-fix="${escapeHtml(u.id)}"><i class="fas fa-wrench"></i><span>Fix &amp; re-upload</span></button>`
      : "";

    const main = `<tr class="${u.id === highlightId ? "row-new" : ""}">
      <td><i class="fas fa-file-lines file-icon"></i>${escapeHtml(u.fileName)}</td>
      <td>${escapeHtml(yearList(uploadPeriods(u)))}</td>
      <td>${formatDate(u.submittedOn)}<small>${escapeHtml(u.submittedBy)}</small></td>
      <td><span class="badge ${u.status}">${UPLOAD_STATUS[u.status] || u.status}</span><small>${escapeHtml(decision)}</small></td>
      <td class="action-cell">${action}</td></tr>`;

    return main + (u.comment ? commentRow(5, decided, u.decidedBy, u.comment) : "");
  }).join("");

  table.innerHTML = `<thead><tr><th>File</th><th>Years</th><th>Uploaded</th><th>Status</th><th></th></tr></thead><tbody>${rows}</tbody>`;
}

async function loadUploads(highlightId) {
  try {
    upload.uploads = await Api.getSubmissions();
    renderUploads(highlightId);
  } catch (err) {
    showToast(err.message || "Could not load your uploads.", "error");
  }
}

function setupUpload() {
  ["dragover", "drop"].forEach((type) => window.addEventListener(type, (e) => e.preventDefault()));

  if (!can("upload")) {
    showRoleNote("uploadRoleNote", "upload", "you can see the uploads and their decisions, but only a Submitter can upload or fix a statement.");
    document.getElementById("uploadFlow").hidden = true;
    loadUploads();
    return;
  }

  setupDropzone();
  goToStep(1);
  document.getElementById("templateNote").hidden = mockSubmissions();
  document.getElementById("templateBtn").addEventListener("click", downloadTemplate);

  const table = document.getElementById("mapTable");
  table.addEventListener("change", onMappingEdit);
  table.addEventListener("input", (e) => {
    if (e.target.classList.contains("split-pct")) onMappingEdit(e);
  });

  document.getElementById("scaleSelect").addEventListener("change", (e) => {
    upload.preview.scale = Number(e.target.value);
    refreshChecks();
  });
  document.getElementById("confirmCheck").addEventListener("change", updateSubmitState);
  document.getElementById("submitUploadBtn").addEventListener("click", submitUpload);
  ["changeFileBtn", "uploadAnotherBtn"].forEach((id) => document.getElementById(id).addEventListener("click", () => goToStep(1)));
  document.getElementById("replaceCancel").addEventListener("click", () => setReplaces(null));

  document.getElementById("uploadsTable").addEventListener("click", (e) => {
    const btn = e.target.closest("[data-fix]");
    if (!btn) return;
    setReplaces(upload.uploads.find((u) => u.id === btn.dataset.fix));
    goToStep(1);
    document.getElementById("view-upload").scrollIntoView({ behavior: "smooth" });
    showToast("Choose the corrected file. It will replace the rejected upload.", "info");
  });

  loadUploads();
}
