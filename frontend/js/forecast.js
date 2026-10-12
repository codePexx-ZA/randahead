// Forecast results: compares the two methods' runs, financial-year labels and economic warnings
const METHOD_NAMES = {
  moving_average: "Moving Average",
  exponential_smoothing: "Exponential Smoothing",
  combined: "Both (average)",
};

function averageOfMethods(ma, es) {
  return Math.round((ma + es) / 2);
}

function yearNumber(label) {
  return parseInt(label.replace(/\D/g, ""), 10);
}

function nextYearLabels(lastYear, horizon) {
  const year = yearNumber(lastYear);
  return Array.from({ length: horizon }, (_, i) => `FY${year + i + 1}`);
}

function describeMethod(budget) {
  const window = `${budget.parameters.window_size}-year window`;
  const alpha = `α ${budget.parameters.alpha}`;
  const setting = { moving_average: window, exponential_smoothing: alpha, combined: `${window} + ${alpha}` }[budget.method];
  return `${METHOD_NAMES[budget.method]} (${setting})`;
}

function fyStartDate(label, yearEndMonth) {
  const year = yearNumber(label);
  const startMonth = (yearEndMonth % 12) + 1;
  const startYear = yearEndMonth === 12 ? year : year - 1;
  return `${startYear}-${String(startMonth).padStart(2, "0")}-01`;
}

function fyLabelFromDate(iso, yearEndMonth) {
  const year = Number(iso.slice(0, 4));
  return `FY${yearEndMonth === 12 ? year : year + 1}`;
}

function calendarYearOf(label, yearEndMonth) {
  const year = yearNumber(label);
  return yearEndMonth >= 6 ? year : year - 1;
}

function compareRuns(runs, yearEndMonth) {
  const first = runs[0];
  const horizon = first.parameters.horizon;
  const years = [...new Set(first.values.map((v) => v.forecast_period))]
    .sort()
    .slice(0, horizon)
    .map((iso) => fyLabelFromDate(iso, yearEndMonth));

  const methods = {};
  runs.forEach((run) => {
    const totals = Object.fromEntries(LINE_CATEGORIES.map((c) => [c.code, new Array(years.length).fill(0)]));
    run.values.forEach((v) => {
      const i = years.indexOf(fyLabelFromDate(v.forecast_period, yearEndMonth));
      if (i >= 0) totals[v.category][i] = Number(v.baseline_amount);
    });
    methods[run.forecasting_method] = {
      runId: run.run_id,
      method: run.forecasting_method,
      parameters: run.parameters,
      horizon,
      totals,
      subtotals: calculateSubtotals(totals, years.length),
    };
  });

  const { moving_average: ma, exponential_smoothing: es } = methods;
  const windowSize = ma.parameters.window_size;
  const alpha = es.parameters.alpha;
  const combined = Object.fromEntries(LINE_CATEGORIES.map((c) => [
    c.code,
    ma.totals[c.code].map((v, i) => averageOfMethods(v, es.totals[c.code][i])),
  ]));
  methods.combined = {
    runIds: [ma.runId, es.runId],
    method: "combined",
    parameters: { window_size: windowSize, alpha },
    horizon,
    totals: combined,
    subtotals: calculateSubtotals(combined, years.length),
  };

  return {
    years,
    horizon,
    windowSize,
    alpha,
    warnings: first.parameters.warnings || [],
    createdAt: first.created_at,
    methods,
  };
}

const INDICATOR_TYPES = [
  { code: "cpi_headline", name: "Consumer inflation (headline CPI)", categories: ["cost_of_sales"] },
  { code: "cpi_core", name: "Core inflation", categories: ["occupancy", "other_utilities", "marketing", "other_operating"] },
  { code: "electricity_price", name: "Electricity price change", categories: ["electricity"] },
  { code: "fuel_price", name: "Fuel price change", categories: ["transport_fuel"] },
];

function getIndicatorType(code) {
  return INDICATOR_TYPES.find((t) => t.code === code);
}

function latestIndicatorInHistory(series, historyYears, yearEndMonth) {
  if (!historyYears.length) return { period: null, value: null };
  const firstYear = calendarYearOf(historyYears[0], yearEndMonth);
  const lastYear = calendarYearOf(historyYears[historyYears.length - 1], yearEndMonth);
  const years = Object.keys(series || {}).map(Number).filter((y) => y >= firstYear && y <= lastYear);
  const period = years.length ? Math.max(...years) : null;
  return { period, value: period === null ? null : series[period] };
}

function buildIndicatorWarnings(indicators, rules, historyYears, yearEndMonth) {
  return rules.map((rule) => {
    const type = getIndicatorType(rule.indicatorCode);
    const { period, value } = latestIndicatorInHistory(indicators[rule.indicatorCode], historyYears, yearEndMonth);
    return {
      ...rule,
      indicatorName: type.name,
      categories: type.categories,
      period,
      value,
      triggered: value !== null && value > rule.threshold,
    };
  });
}
