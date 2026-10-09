// Importer: reads and checks uploaded Excel/CSV files
const MIN_HISTORY_YEARS = 3;
const MAX_HISTORY_YEARS = 5;
const SHEETJS_URL = "https://cdn.sheetjs.com/xlsx-0.20.3/package/dist/xlsx.full.min.js";
const MONTHS = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

const SCALE_LABELS = { 1: "Rand", 1000: "R'000 (thousands)", 1000000: "R million" };

const CATEGORY_KEYWORDS = {
  revenue: ["sales", "revenue", "turnover", "income", "income from services", "service income", "fees earned", "takings"],
  other_income: [
    "other income", "interest received", "interest income", "sundry income", "rental income",
    "dividends received", "dividend income", "commission received", "commission income",
  ],
  cost_of_sales: ["cost of sales", "cost of goods sold", "cost of goods", "cogs", "purchases", "direct costs"],
  employee_costs: ["salaries", "salary", "wages", "staff costs", "employee", "employee costs", "payroll", "uif", "sdl", "paye", "bonuses", "pension"],
  occupancy: ["rent", "rental", "rates", "lease", "occupancy", "property"],
  electricity: ["electricity", "eskom", "prepaid electricity"],
  other_utilities: ["water", "telephone", "internet", "telecoms", "telecommunications", "cellphone", "utilities", "other utilities", "wifi"],
  transport_fuel: ["fuel", "petrol", "diesel", "delivery", "motor vehicle", "vehicle", "transport", "courier", "travel"],
  marketing: ["advertising", "marketing", "promotions", "promotion", "social media"],
  other_operating: [
    "other operating expenses", "admin", "administration", "bank charges", "repairs", "maintenance",
    "insurance", "stationery", "accounting fees", "audit fees", "legal fees", "professional fees",
    "cleaning", "security", "consumables", "licences", "subscriptions", "sundry expenses", "general expenses",
  ],
  depreciation: ["depreciation", "amortisation", "amortization"],
  finance_costs: ["interest paid", "interest expense", "finance costs", "finance charges", "interest on overdraft", "interest on loan"],
  income_tax: ["income tax", "tax", "taxation", "sars", "company tax"],
};

const SUBTOTAL_PATTERNS = [
  ["profit_before_tax", /\b(profit|loss)\b.*\bbefore (income )?tax/],
  ["gross_profit", /^gross (profit|loss|margin)/],
  ["operating_profit", /^(operating (profit|loss|income)|(profit|loss) from operations|ebit)\b/],
  ["net_profit", /^(net (profit|loss|income)|(profit|loss)( loss)? (for the (year|period)|after tax))/],
];

function labelKey(label) {
  return String(label).trim().toLowerCase().replace(/\s+/g, " ");
}

function cleanText(text) {
  return text.toLowerCase().replace(/&/g, " and ").replace(/[^a-z0-9]+/g, " ").trim();
}

function fileExtension(name) {
  const dot = name.lastIndexOf(".");
  return dot === -1 ? "" : name.slice(dot).toLowerCase();
}

function loadSheetJs() {
  if (window.XLSX) return Promise.resolve(window.XLSX);
  return new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = SHEETJS_URL;
    script.onload = () => resolve(window.XLSX);
    script.onerror = () => reject(new Error("Couldn't load the Excel reader. Check your connection or upload a CSV."));
    document.head.appendChild(script);
  });
}

function parseCsv(text) {
  text = text.replace(/^\uFEFF/, "");
  const sample = text.split(/\r?\n/).slice(0, 15).join("\n");
  const counts = { ",": 0, ";": 0, "\t": 0 };
  for (const ch of sample) if (ch in counts) counts[ch]++;
  const delimiter = Object.keys(counts).reduce((a, b) => (counts[b] > counts[a] ? b : a));

  const rows = [];
  let row = [];
  let cell = "";
  let quoted = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (quoted) {
      if (ch === '"' && text[i + 1] === '"') { cell += '"'; i++; }
      else if (ch === '"') quoted = false;
      else cell += ch;
    } else if (ch === '"') {
      quoted = true;
    } else if (ch === delimiter) {
      row.push(cell);
      cell = "";
    } else if (ch === "\n" || ch === "\r") {
      if (ch === "\r" && text[i + 1] === "\n") i++;
      row.push(cell);
      rows.push(row);
      row = [];
      cell = "";
    } else {
      cell += ch;
    }
  }
  if (cell !== "" || row.length) {
    row.push(cell);
    rows.push(row);
  }
  return rows;
}

async function readSpreadsheet(file) {
  if (fileExtension(file.name) === ".csv") return [parseCsv(await file.text())];

  const XLSX = await loadSheetJs();
  const workbook = XLSX.read(await file.arrayBuffer(), { type: "array" });
  return workbook.SheetNames.map((name) =>
    XLSX.utils.sheet_to_json(workbook.Sheets[name], { header: 1, raw: false, defval: "" })
  );
}

function parseAmount(cell) {
  if (typeof cell === "number") return cell;
  let s = String(cell ?? "").trim();
  if (s === "") return null;
  if (/^[-–—]+$/.test(s)) return 0;

  let negative = false;
  if (/^\(.*\)$/.test(s)) {
    negative = true;
    s = s.slice(1, -1);
  }
  s = s.replace(/^R/i, "").replace(/[\s\u00a0]/g, "");
  if (s.startsWith("-")) {
    negative = !negative;
    s = s.slice(1);
  }
  if (/^\d{1,3}(,\d{3})+(\.\d+)?$/.test(s)) s = s.replace(/,/g, "");
  else if (/^\d+,\d+$/.test(s)) s = s.replace(",", ".");
  if (!/^\d+(\.\d+)?$/.test(s)) return NaN;

  const n = parseFloat(s);
  return negative ? -n : n;
}

function isAmount(value) {
  return value !== null && !Number.isNaN(value);
}

function fullYear(text) {
  const n = Number(text);
  return text.length === 2 ? 2000 + n : n;
}

function parsePeriodHeader(cell) {
  const s = String(cell ?? "").trim();
  if (!s || s.length > 40) return null;
  const valid = (year) => (year >= 1990 && year <= 2100 ? { year } : null);
  let m;

  if ((m = s.match(/^FY\s*'?(\d{4}|\d{2})$/i))) return valid(fullYear(m[1]));

  if ((m = s.match(/^(\d{4})\s*[/-]\s*(\d{2}|\d{4})$/))) {
    const end = m[2].length === 2 ? Math.floor(Number(m[1]) / 100) * 100 + Number(m[2]) : Number(m[2]);
    return valid(end);
  }

  if ((m = s.match(/^(?:\d{1,2}\s+)?([a-z]{3,9})\.?[\s-]+'?(\d{4}|\d{2})$/i))) {
    const month = MONTHS.indexOf(m[1].slice(0, 3).toLowerCase());
    if (month !== -1) return { month: month + 1, year: fullYear(m[2]) };
  }

  if ((m = s.match(/year\s+end(?:ed|ing)?.*?\b(\d{4})\b/i))) return valid(Number(m[1]));

  if ((m = s.match(/^(\d{4})\b(.*)$/)) && !/\d/.test(m[2]) && m[2].length <= 12) return valid(Number(m[1]));

  return null;
}

function findHeader(grid) {
  const limit = Math.min(grid.length, 40);
  for (let r = 0; r < limit; r++) {
    const cells = grid[r] || [];
    const years = [];
    const months = [];
    cells.forEach((cell, col) => {
      const period = parsePeriodHeader(cell);
      if (!period) return;
      if (period.month) months.push({ col, ...period });
      else years.push({ col, year: period.year });
    });

    if (months.length >= 2) {
      if (new Set(months.map((p) => p.month)).size > 1) return { row: r, monthly: true };
      return { row: r, years: months.map(({ col, year }) => ({ col, year })) };
    }
    if (years.length >= 2) return { row: r, years };
  }

  for (let r = 0; r < limit; r++) {
    const hits = [];
    (grid[r] || []).forEach((cell, col) => {
      const period = col > 0 && parsePeriodHeader(cell);
      if (period) hits.push({ col, year: period.year });
    });
    if (hits.length !== 1) continue;
    const { col } = hits[0];
    if (grid.slice(r + 1, r + 41).some((row) => isAmount(parseAmount((row || [])[col])))) return { row: r, years: hits };
  }
  return null;
}

function detectScale(grid, headerRow) {
  const text = grid.slice(0, headerRow + 3).flat().map((c) => String(c ?? "")).join(" | ");
  let m;
  if ((m = text.match(/R\s*'?\s*000|'000|thousands/i))) return { scale: 1000, hint: m[0] };
  if ((m = text.match(/\bR\s*'?\s*m(illion)?s?\b|millions/i))) return { scale: 1000000, hint: m[0] };
  return { scale: 1, hint: null };
}

function bestKeyword(text) {
  const padded = ` ${text} `;
  let best = null;
  let bestLength = 0;
  Object.entries(CATEGORY_KEYWORDS).forEach(([category, words]) => {
    words.forEach((word) => {
      if (word.length > bestLength && padded.includes(` ${word} `)) {
        best = category;
        bestLength = word.length;
      }
    });
  });
  return best;
}

function matchCategory(key) {
  const whole = cleanText(key);
  const exact = Object.keys(CATEGORY_KEYWORDS).find((c) => CATEGORY_KEYWORDS[c].includes(whole));
  if (exact) return { type: "single", category: exact };

  const parts = key
    .replace(/&/g, " and ")
    .split(/\s+and\s+|[/,+;]/)
    .map(cleanText)
    .filter(Boolean);
  const found = parts.map(bestKeyword);
  if (!found.length || found.some((c) => !c)) return null;

  const unique = [...new Set(found)];
  return unique.length === 1 ? { type: "single", category: unique[0] } : { type: "cross", categories: unique };
}

function matchSubtotal(clean) {
  return SUBTOTAL_PATTERNS.find(([, pattern]) => pattern.test(clean))?.[0] ?? null;
}

function mappingFromParts(parts) {
  if (!parts.length) return { type: "ignore" };
  if (parts.length === 1) return { type: "single", category: parts[0].category };
  return { type: "split", parts: parts.map((p) => ({ ...p })) };
}

function mappingParts(mapping) {
  if (!mapping || mapping.type === "ignore") return [];
  if (mapping.type === "single") return [{ category: mapping.category, percent: 100 }];
  return mapping.parts;
}

function isResolved(mapping) {
  return Boolean(mapping) && !mapping.invalid;
}

function classifyRow(id, label, values, remembered) {
  const key = labelKey(label);
  const base = { id, label, key, values };
  if (!values.some(isAmount)) return { ...base, kind: "heading" };

  const clean = cleanText(key);
  const subtotal = matchSubtotal(clean);
  if (subtotal) return { ...base, kind: "subtotal", subtotal };
  if (/^total\b|\btotals?$/.test(clean)) return { ...base, kind: "total" };

  const line = { ...base, kind: "line" };
  if (remembered[key]) return { ...line, status: "remembered", mapping: mappingFromParts(remembered[key]) };

  const match = matchCategory(key);
  if (!match) return { ...line, status: "unknown", mapping: null };
  if (match.type === "cross") return { ...line, status: "cross", candidates: match.categories, mapping: null };
  return { ...line, status: "auto", mapping: { type: "single", category: match.category } };
}

function buildPreview(grids, fileName, remembered = {}) {
  let grid = null;
  let header = null;
  for (const g of grids) {
    header = findHeader(g);
    if (header) { grid = g; break; }
  }

  if (!header) {
    throw new Error("We couldn't find the year columns. Put the line names in the first column and one financial year per column (e.g. FY2025, FY2026), or a single year (e.g. FY2026).");
  }
  if (header.monthly) {
    throw new Error("This looks like a monthly file. Monthly uploads are coming soon; for now please upload annual figures (one column per financial year).");
  }

  let cols = [...header.years].sort((a, b) => a.year - b.year);
  const repeated = cols.filter((c, i) => i > 0 && c.year === cols[i - 1].year).map((c) => `FY${c.year}`);
  if (repeated.length) {
    throw new Error(`The file has more than one column for ${[...new Set(repeated)].join(", ")}. Keep one column per financial year.`);
  }

  const notes = [];
  if (cols.length > MAX_HISTORY_YEARS) {
    const kept = cols.slice(-MAX_HISTORY_YEARS);
    notes.push(`The file has ${cols.length} years; only the latest ${MAX_HISTORY_YEARS} (FY${kept[0].year}–FY${kept.at(-1).year}) are used.`);
    cols = kept;
  }

  const firstYearCol = Math.min(...header.years.map((c) => c.col));
  const rows = [];
  for (let r = header.row + 1; r < grid.length; r++) {
    const cells = grid[r] || [];
    const label = cells
      .slice(0, firstYearCol)
      .map((c) => String(c ?? "").trim())
      .filter((c) => c && Number.isNaN(parseAmount(c)))
      .join(" ");
    if (!label) continue;
    const values = cols.map((c) => {
      const v = parseAmount(cells[c.col]);
      return isAmount(v) ? v : null;
    });
    rows.push(classifyRow(rows.length, label, values, remembered));
  }

  while (rows.at(-1)?.kind === "heading") rows.pop();

  if (!rows.some((r) => r.kind === "line")) {
    throw new Error("No amounts found under the year columns. Fill in the figures and upload again.");
  }

  const { scale, hint: scaleHint } = detectScale(grid, header.row);
  return { fileName, frequency: "annual", years: cols.map((c) => `FY${c.year}`), scale, scaleHint, rows, notes };
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

function fullyCoveredUploads(uploads) {
  const approved = uploads.filter((u) => u.status === "approved");
  return approved
    .map((u, i) => {
      const newer = approved.slice(0, i);
      const periods = uploadPeriods(u);
      if (!newer.length || !periods.every((y) => newer.some((n) => uploadPeriods(n).includes(y)))) return null;
      return { upload: u, by: newer.find((n) => uploadPeriods(n).some((y) => periods.includes(y))) };
    })
    .filter(Boolean);
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
