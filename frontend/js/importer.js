// Importer: checks uploaded income statements and works out the approved history
const MIN_HISTORY_YEARS = 3;
const MAX_HISTORY_YEARS = 5;

const SCALE_LABELS = { 1: "Rand", 1000: "R'000 (thousands)", 1000000: "R million" };

function labelKey(label) {
  return String(label).trim().toLowerCase().replace(/\s+/g, " ");
}

function fileExtension(name) {
  const dot = name.lastIndexOf(".");
  return dot === -1 ? "" : name.slice(dot).toLowerCase();
}

function mappingParts(mapping) {
  if (!mapping || mapping.type === "ignore") return [];
  if (mapping.type === "single") return [{ category: mapping.category, percent: 100 }];
  return mapping.parts;
}

function isResolved(mapping) {
  return Boolean(mapping) && !mapping.invalid;
}

function splitRows(label, parts, values) {
  return parts.map((p) => ({
    label: parts.length > 1 ? `${label} (${p.percent}%)` : label,
    category: p.category,
    values: values.map((v) => (v * p.percent) / 100),
  }));
}

function previewToSubmission(preview) {
  const rows = preview.rows.flatMap((r) => {
    const values = r.values.map((v) => v ?? 0);
    if (r.kind === "subtotal") return [{ label: r.label, subtotal: r.subtotal, values }];
    return r.kind === "line" && isResolved(r.mapping) ? splitRows(r.label, mappingParts(r.mapping), values) : [];
  });
  return { fileName: preview.fileName, scale: preview.scale, years: preview.years, rows };
}

function missingYears(years) {
  const numbers = years.map((y) => Number(y.slice(2)));
  const missing = [];
  for (let y = numbers[0] + 1; y < numbers.at(-1); y++) {
    if (!numbers.includes(y)) missing.push(`FY${y}`);
  }
  return missing;
}

function listNames(rows, max = 3) {
  const names = rows.slice(0, max).map((r) => `"${r.label}"`);
  if (rows.length > max) names.push(`${rows.length - max} more`);
  return names.join(", ");
}

function blankRows(lines) {
  return lines.filter((r) => r.values.some((v) => v === null));
}

function subtotalsMatchItem(keys) {
  return { level: "ok", text: `Subtotals match the file (${keys.map((k) => SUBTOTAL_NAMES[k].toLowerCase()).join(", ")}).` };
}

function checkPreview(preview, change) {
  const items = [];
  const lines = preview.rows.filter((r) => r.kind === "line");
  const { years } = preview;

  if (change) {
    items.push({ level: "ok", text: `${years.length} financial year${years.length > 1 ? "s" : ""} in this file (${yearList(years)}).` });
  } else if (years.length < MIN_HISTORY_YEARS) {
    items.push({ level: "warning", text: `Only ${years.length} years (${yearList(years)}). Forecasts are more reliable with 3 to 5 years.` });
  } else {
    items.push({ level: "ok", text: `${years.length} financial years found (${yearList(years)}).` });
  }
  preview.notes.forEach((text) => items.push({ level: "warning", text }));
  const missing = missingYears(years);
  if (change) {
    items.push(...historyChecks(change));
  } else if (missing.length) {
    items.push({ level: "warning", text: `Missing ${missing.join(", ")}. Add the missing year so the history has no gaps.` });
  }

  const unresolved = lines.filter((r) => !isResolved(r.mapping));
  if (unresolved.length) {
    items.push({ level: "error", text: `${unresolved.length} line${unresolved.length > 1 ? "s need" : " needs"} a category: ${listNames(unresolved)}.` });
  } else {
    items.push({ level: "ok", text: `All ${lines.length} lines are matched to a category.` });
  }

  const blanks = blankRows(lines);
  if (blanks.length) {
    items.push({ level: "warning", text: `Blank amounts in ${listNames(blanks)} are treated as 0. Type 0 or a dash in the file if that's correct.` });
  }

  let subtotals = null;
  if (!unresolved.length) {
    const hasRevenue = lines.some((r) => mappingParts(r.mapping).some((p) => p.category === "revenue"));
    if (!hasRevenue) items.push({ level: "error", text: "No line is matched to Revenue. Every income statement needs sales or revenue." });

    subtotals = processStatement(previewToSubmission(preview)).checks;
    const keys = Object.keys(subtotals);
    const off = keys.filter((k) => !subtotals[k].matches);
    if (!keys.length) {
      items.push({ level: "warning", text: "No subtotals (gross profit, net profit…) in the file, so the totals couldn't be cross-checked." });
    } else if (off.length) {
      items.push({
        level: "warning",
        text: `${off.map((k) => SUBTOTAL_NAMES[k]).join(", ")} in the file ${off.length > 1 ? "don't" : "doesn't"} match our recalculation. Check the categories, or the sheet itself.`,
      });
    } else {
      items.push(subtotalsMatchItem(keys));
    }
  }

  return { items, subtotals, ready: !items.some((i) => i.level === "error") };
}

function buildSubmissionPayload(preview, replacesSubmissionId) {
  return {
    fileName: preview.fileName,
    frequency: preview.frequency,
    scale: preview.scale,
    years: preview.years,
    replacesSubmissionId: replacesSubmissionId || null,
    rows: preview.rows.map((r) => ({
      label: r.label,
      kind: r.kind,
      subtotal: r.subtotal || null,
      values: r.values,
      mapping: r.kind === "line" ? mappingParts(r.mapping) : null,
      remember: r.kind === "line" && (r.status !== "auto" || Boolean(r.changed)),
    })),
  };
}

function submissionFromUpload(upload) {
  const rows = upload.rows.flatMap((r) => {
    const values = r.values.map((v) => v ?? 0);
    if (r.kind === "heading") return [{ heading: r.label }];
    if (r.kind === "subtotal") return [{ label: r.label, subtotal: r.subtotal, values }];
    return r.kind === "line" ? splitRows(r.label, r.mapping || [], values) : [];
  });

  return {
    fileName: upload.fileName,
    status: upload.status,
    submittedBy: upload.submittedBy,
    approvedBy: upload.decidedBy,
    approvedOn: upload.decidedOn,
    scale: upload.scale,
    years: upload.periods,
    rows,
  };
}

function reviewUpload(upload, change) {
  const items = historyChecks(change);
  const lines = upload.rows.filter((r) => r.kind === "line");
  const name = (code) => getCategory(code).name;

  const handMapped = lines.filter((r) => r.remember && r.mapping.length === 1);
  if (handMapped.length) {
    const list = handMapped.map((r) => `"${r.label}" → ${name(r.mapping[0].category)}`).join("; ");
    items.push({ level: "info", text: `Mapped by the submitter (not recognised automatically): ${list}.` });
  }
  lines.filter((r) => r.mapping.length > 1).forEach((r) => {
    const parts = r.mapping.map((p) => `${p.percent}% ${name(p.category)}`).join(", ");
    items.push({ level: "info", text: `"${r.label}" split by the submitter: ${parts}.` });
  });

  const ignored = lines.filter((r) => !r.mapping.length);
  if (ignored.length) {
    items.push({ level: "warning", text: `Left out as not part of the income statement: ${listNames(ignored)}.` });
  }
  const blanks = blankRows(lines);
  if (blanks.length) items.push({ level: "warning", text: `Blank amounts in ${listNames(blanks)} were treated as 0.` });

  const subtotals = processStatement(submissionFromUpload(upload)).checks;
  const keys = Object.keys(subtotals);
  if (!keys.length) {
    items.push({ level: "warning", text: "No subtotals in the file, so the totals couldn't be cross-checked." });
  }
  keys.forEach((k) => {
    const check = subtotals[k];
    if (check.matches) return;
    const i = check.diffs.findIndex((d) => Math.abs(d) > upload.scale);
    const off = `R ${Math.round(Math.abs(check.diffs[i])).toLocaleString("en-ZA")}`;
    items.push({ level: "warning", text: `${SUBTOTAL_NAMES[k]} in the file doesn't match our recalculation: off by ${off} in ${upload.periods[i]}.` });
  });
  if (keys.length && keys.every((k) => subtotals[k].matches)) items.push(subtotalsMatchItem(keys));

  return { items, subtotals };
}

function uploadPeriods(u) {
  if (u.periods) return u.periods;
  const [first, last] = u.years.map((y) => Number(y.slice(2)));
  return Array.from({ length: last - first + 1 }, (_, i) => `FY${first + i}`);
}

function yearList(years) {
  const sorted = [...years].sort();
  const runs = [];
  sorted.forEach((y) => {
    const run = runs.at(-1);
    if (run && Number(y.slice(2)) === Number(run.at(-1).slice(2)) + 1) run.push(y);
    else runs.push([y]);
  });
  return runs.map((r) => (r.length > 1 ? `${r[0]}–${r.at(-1)}` : r[0])).join(", ");
}

function planHistory(uploads) {
  const sourceOf = {};
  uploads.forEach((u) => uploadPeriods(u).forEach((y) => (sourceOf[y] ??= u)));
  const all = Object.keys(sourceOf).sort();
  const years = all.slice(-MAX_HISTORY_YEARS);
  return {
    years,
    sourceOf: Object.fromEntries(years.map((y) => [y, sourceOf[y]])),
    dropped: all.slice(0, -MAX_HISTORY_YEARS),
  };
}

function historyChange(uploads, incoming) {
  const isApproved = (u) => u.status === "approved" && u.id !== incoming.id;
  const approved = uploads.filter(isApproved);
  const index = uploads.findIndex((u) => u.id === incoming.id);
  const saved = index !== -1;
  const before = planHistory(approved);
  const after = planHistory(saved ? uploads.filter((u) => u.id === incoming.id || isApproved(u)) : [incoming, ...approved]);
  const own = uploadPeriods(incoming);
  const mine = (y) => Boolean(after.sourceOf[y]) && after.sourceOf[y].id === incoming.id;
  const newer = new Set(approved.filter((u) => uploads.indexOf(u) < index).flatMap(uploadPeriods));

  return {
    before,
    after,
    added: own.filter((y) => mine(y) && !before.sourceOf[y]),
    replaced: own.filter((y) => mine(y) && before.sourceOf[y]).map((y) => ({ year: y, from: before.sourceOf[y] })),
    covered: own.filter((y) => newer.has(y)),
    tooOld: own.filter((y) => !newer.has(y) && !after.sourceOf[y]),
    dropped: before.years.filter((y) => !after.sourceOf[y]),
  };
}

function describeChange(change) {
  const parts = [];
  if (change.added.length) parts.push(`Adds ${yearList(change.added)}.`);
  const byFile = new Map();
  change.replaced.forEach(({ year, from }) => byFile.set(from, [...(byFile.get(from) || []), year]));
  byFile.forEach((years, from) => parts.push(`Replaces ${yearList(years)} from ${from.fileName}.`));
  if (change.dropped.length) parts.push(`${yearList(change.dropped)} drops out (only the latest ${MAX_HISTORY_YEARS} years are used).`);
  if (!change.added.length && !change.replaced.length) parts.push("Adds nothing to the history: every year in it is already covered by newer uploads or too old.");
  return parts.join(" ");
}

function historyChecks(change) {
  const items = [];
  const years = change.after.years;
  const span = yearList(years);
  if (years.length < MIN_HISTORY_YEARS) {
    items.push({ level: "warning", text: `Once approved, the history has only ${years.length} year${years.length > 1 ? "s" : ""} (${span}). Forecasts are more reliable with 3 to 5 years.` });
  } else {
    items.push({ level: "ok", text: `Once approved, the history is ${span} (${years.length} years).` });
  }
  items.push({ level: "info", text: describeChange(change) });
  const missing = missingYears(years);
  if (missing.length) {
    items.push({ level: "warning", text: `Missing ${missing.join(", ")}, so the history has a gap. Upload that year to fill it.` });
  }
  if (change.covered.length) {
    items.push({ level: "warning", text: `${yearList(change.covered)} in this file won't be used: a newer approved upload already covers ${change.covered.length > 1 ? "them" : "it"}.` });
  }
  if (change.tooOld.length) {
    items.push({ level: "info", text: `${yearList(change.tooOld)} won't be used: only the latest ${MAX_HISTORY_YEARS} years are kept.` });
  }
  return items;
}

function rowKeys(rows) {
  const seen = {};
  return rows.map((r) => {
    let key = `l:${labelKey(r.label || "")}:${r.category}`;
    if (r.heading) key = `h:${labelKey(r.heading)}`;
    else if (r.subtotal) key = `s:${r.subtotal}`;
    seen[key] = (seen[key] || 0) + 1;
    return seen[key] > 1 ? `${key}#${seen[key]}` : key;
  });
}

function combineUploads(approved) {
  const plan = planHistory(approved);
  const used = approved.filter((u) => plan.years.some((y) => plan.sourceOf[y] === u));
  const newest = used[0];
  const { scale } = newest;

  const parts = used.map((u) => {
    const s = submissionFromUpload(u);
    const keys = rowKeys(s.rows);
    return { u, periods: uploadPeriods(u), byKey: new Map(keys.map((k, i) => [k, s.rows[i]])), keys };
  });

  const order = [];
  const template = new Map();
  parts.forEach(({ keys, byKey }) => {
    let at = -1;
    keys.forEach((key) => {
      if (template.has(key)) {
        at = order.indexOf(key);
        return;
      }
      order.splice(at + 1, 0, key);
      template.set(key, byKey.get(key));
      at += 1;
    });
  });

  const rows = order.map((key) => {
    const row = template.get(key);
    if (row.heading) return { heading: row.heading };
    const values = plan.years.map((y) => {
      const part = parts.find((p) => p.u === plan.sourceOf[y]);
      const source = part.byKey.get(key);
      return source ? (source.values[part.periods.indexOf(y)] * part.u.scale) / scale : null;
    });
    return { ...row, values };
  });

  return {
    fileName: newest.fileName,
    status: "approved",
    submittedBy: newest.submittedBy,
    approvedBy: newest.decidedBy,
    approvedOn: newest.decidedOn,
    scale,
    years: plan.years,
    rows,
    sources: used.map((u) => ({
      id: u.id,
      fileName: u.fileName,
      years: plan.years.filter((y) => plan.sourceOf[y] === u),
      submittedBy: u.submittedBy,
      approvedBy: u.decidedBy,
    })),
  };
}

function describeSources(submission) {
  const sources = submission.sources || [{ fileName: submission.fileName, years: submission.years }];
  const list = sources.map((s) => `${s.fileName} (${yearList(s.years)})`);
  return list.length > 1 ? `${list.slice(0, -1).join(", ")} and ${list.at(-1)}` : list[0];
}
