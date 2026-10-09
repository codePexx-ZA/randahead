// Statement processing: maps lines to categories
const LINE_CATEGORIES = [
  { code: "revenue", name: "Revenue", kind: "income", teamManaged: true },
  { code: "other_income", name: "Other income", kind: "income" },
  { code: "cost_of_sales", name: "Cost of sales", kind: "expense" },
  { code: "employee_costs", name: "Employee costs", kind: "expense", teamManaged: true },
  { code: "occupancy", name: "Occupancy", kind: "expense" },
  { code: "electricity", name: "Electricity", kind: "expense" },
  { code: "other_utilities", name: "Water, telecoms and other utilities", kind: "expense" },
  { code: "transport_fuel", name: "Transport and fuel", kind: "expense" },
  { code: "marketing", name: "Marketing", kind: "expense" },
  { code: "other_operating", name: "Other operating expenses", kind: "expense" },
  { code: "depreciation", name: "Depreciation and amortisation", kind: "expense" },
  { code: "finance_costs", name: "Finance costs", kind: "expense" },
  { code: "income_tax", name: "Income tax", kind: "expense" },
];

const SUBTOTAL_NAMES = {
  gross_profit: "Gross profit",
  operating_profit: "Operating profit",
  profit_before_tax: "Profit before tax",
  net_profit: "Net profit",
};

const OPERATING_EXPENSES = [
  "employee_costs", "occupancy", "electricity", "other_utilities", "transport_fuel",
  "marketing", "other_operating", "depreciation",
];

const PROCESSED_LAYOUT = [
  { category: "revenue" },
  { category: "cost_of_sales" },
  { subtotal: "gross_profit" },
  { category: "other_income" },
  { heading: "Operating expenses" },
  ...OPERATING_EXPENSES.map((code) => ({ category: code })),
  { subtotal: "operating_profit" },
  { category: "finance_costs" },
  { subtotal: "profit_before_tax" },
  { category: "income_tax" },
  { subtotal: "net_profit" },
];

function getCategory(code) {
  return LINE_CATEGORIES.find((c) => c.code === code);
}

function calculateSubtotals(totals, n) {
  const subtotals = Object.fromEntries(Object.keys(SUBTOTAL_NAMES).map((key) => [key, new Array(n).fill(0)]));
  for (let i = 0; i < n; i++) {
    const t = (code) => totals[code][i];
    const opex = OPERATING_EXPENSES.reduce((sum, code) => sum + t(code), 0);
    subtotals.gross_profit[i] = t("revenue") - t("cost_of_sales");
    subtotals.operating_profit[i] = subtotals.gross_profit[i] + t("other_income") - opex;
    subtotals.profit_before_tax[i] = subtotals.operating_profit[i] - t("finance_costs");
    subtotals.net_profit[i] = subtotals.profit_before_tax[i] - t("income_tax");
  }
  return subtotals;
}

function processStatement(submission) {
  const n = submission.years.length;
  const totals = Object.fromEntries(LINE_CATEGORIES.map((c) => [c.code, new Array(n).fill(0)]));
  const sources = Object.fromEntries(LINE_CATEGORIES.map((c) => [c.code, []]));

  submission.rows
    .filter((row) => row.category)
    .forEach((row) => {
      sources[row.category].push(row.label);
      row.values.forEach((v, i) => (totals[row.category][i] += Math.abs(v) * submission.scale));
    });

  const subtotals = calculateSubtotals(totals, n);

  const checks = {};
  submission.rows
    .filter((row) => row.subtotal)
    .forEach((row) => {
      const diffs = row.values.map((v, i) => (v === null ? null : subtotals[row.subtotal][i] - v * submission.scale));
      checks[row.subtotal] = {
        matches: diffs.every((d) => d === null || Math.abs(d) <= submission.scale),
        diffs,
      };
    });

  return { years: submission.years, totals, sources, subtotals, checks };
}

function buildMetricOptions(submission, processed) {
  const lines = submission.rows
    .filter((row) => row.category)
    .map((row) => ({
      id: `line:${row.label}`,
      label: row.label,
      group: "line",
      kind: getCategory(row.category).kind,
      values: row.values.map((v) => Math.abs(v) * submission.scale),
    }));

  const totals = Object.entries(SUBTOTAL_NAMES).map(([key, name]) => ({
    id: `total:${key}`,
    label: name,
    group: "total",
    kind: "total",
    values: processed.subtotals[key],
  }));

  return [...lines, ...totals];
}
