// Dashboard summary and combined report in the API's shape, worked out in the browser for mock mode
const REPORT_MARGINS = [
  ["gross_margin", "Gross margin", "gross_profit"],
  ["operating_margin", "Operating margin", "operating_profit"],
  ["net_margin", "Net margin", "net_profit"],
];

function roundedChange(value, base) {
  if (base === null || base === undefined || base === 0) return null;
  return Math.round(((value - base) / Math.abs(base)) * 1000) / 10;
}

function summaryLines(processed, i) {
  return PROCESSED_LAYOUT.filter((item) => !item.heading).map((item) => {
    const subtotal = Boolean(item.subtotal);
    const code = item.subtotal || item.category;
    const category = subtotal ? null : getCategory(code);
    const series = subtotal ? processed.subtotals[code] : processed.totals[code];
    const actual = series[i];
    const previous = i > 0 ? series[i - 1] : null;
    return {
      code,
      name: subtotal ? SUBTOTAL_NAMES[code] : category.name,
      kind: subtotal ? "total" : category.kind,
      teamManaged: Boolean(category?.teamManaged),
      subtotal,
      actual,
      previous,
      change: roundedChange(actual, previous),
      budget: null,
      variance: null,
      variancePercent: null,
    };
  });
}

function summaryTarget(target, processed, budget, yearEndMonth) {
  const r = evaluateTarget(target, processed, budget, yearEndMonth);
  return {
    id: target.id,
    metricName: target.metricName,
    name: r.metric ? r.metric.name : target.metricName,
    unit: target.measurementUnit,
    higherIsBetter: r.metric ? r.metric.better === "higher" : true,
    year: r.year,
    targetPeriod: target.targetPeriod,
    targetValue: target.targetValue,
    source: r.figure ? r.figure.source : null,
    value: r.figure ? r.figure.value : null,
    gap: r.figure ? r.gap : null,
    reached: r.figure ? r.reached : null,
    status: r.status,
  };
}

function summaryTargets(processed, budget, targets, yearEndMonth) {
  if (!processed) return [];
  const order = (t) => KPI_METRICS.findIndex((m) => m.code === t.metricName);
  return targets
    .map((t) => summaryTarget(t, processed, budget, yearEndMonth))
    .sort((a, b) => b.targetPeriod.localeCompare(a.targetPeriod) || order(a) - order(b));
}

function buildDashboardSummary(processed, budget, targets, year, yearEndMonth) {
  const years = processed?.years ?? [];
  const i = year ? years.indexOf(year) : years.length - 1;
  if (year && i < 0) throw new Error(`Choose a year in the approved history (${yearsText(years)}).`);
  return {
    years,
    year: i >= 0 ? years[i] : null,
    previousYear: i > 0 ? years[i - 1] : null,
    budget: null,
    lines: i >= 0 ? summaryLines(processed, i) : [],
    targets: summaryTargets(processed, budget, targets, yearEndMonth),
  };
}

function reportColumns(processed, budget) {
  const actual = processed.years.map((year, i) => ({ year, source: "actual", figures: processed, i }));
  if (budget?.status !== "approved") return actual;
  const planned = budget.years
    .map((year, i) => ({ year, source: "budget", figures: budget, i }))
    .filter((c) => !processed.years.includes(c.year));
  return [...actual, ...planned];
}

function buildReport(processed, budget, targets, business) {
  const approved = budget?.status === "approved" ? budget : null;
  const header = {
    business: business.name,
    unit: "Whole company",
    generatedOn: todayIso(),
    budget: approved
      ? { id: approved.id, method: approved.method, status: approved.status, approvedBy: approved.approvedBy, approvedOn: approved.approvedOn }
      : null,
  };
  if (!processed) {
    return { ...header, columns: [], changeFrom: null, changeTo: null, lines: [], ratios: [], targets: [] };
  }

  const columns = reportColumns(processed, budget);
  const value = (c, code) => (c.figures.subtotals[code] ?? c.figures.totals[code])[c.i];
  const span = processed.years.length > 1;
  const last = processed.years.length - 1;
  const lines = summaryLines(processed, last).map((line) => ({
    code: line.code,
    name: line.name,
    kind: line.kind,
    teamManaged: line.teamManaged,
    subtotal: line.subtotal,
    values: columns.map((c) => value(c, line.code)),
    change: span ? roundedChange(value(columns[last], line.code), value(columns[0], line.code)) : null,
  }));

  const revenue = columns.map((c) => value(c, "revenue"));
  const margin = (c, subtotal) => {
    const r = value(c, "revenue");
    return r ? Math.round((value(c, subtotal) / r) * 1000) / 10 : null;
  };
  const ratios = [
    { code: "revenue_growth", name: "Revenue growth", values: revenue.map((r, i) => (i === 0 ? null : roundedChange(r, revenue[i - 1]))) },
    ...REPORT_MARGINS.map(([code, name, subtotal]) => ({ code, name, values: columns.map((c) => margin(c, subtotal)) })),
  ];

  return {
    ...header,
    columns: columns.map(({ year, source }) => ({ year, source })),
    changeFrom: span ? processed.years[0] : null,
    changeTo: span ? processed.years[last] : null,
    lines,
    ratios,
    targets: summaryTargets(processed, budget, targets, business.yearEndMonth),
  };
}
