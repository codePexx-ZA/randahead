// Forecast results: the two methods' runs beside the API's comparison of them, financial-year labels and economic warnings
export const METHOD_NAMES = {
  moving_average: "Moving Average",
  exponential_smoothing: "Exponential Smoothing",
  combined: "Both (average)",
};

// Shorter than the indicator_type names, to fit the rule list and warnings
export const INDICATOR_NAMES = {
  cpi_headline: "Consumer inflation (headline CPI)",
  cpi_core: "Core inflation",
  electricity_price: "Electricity price change",
  fuel_price: "Fuel price change",
};

function yearNumber(label) {
  return parseInt(label.replace(/\D/g, ""), 10);
}

export function nextYearLabels(lastYear, horizon) {
  const year = yearNumber(lastYear);
  return Array.from({ length: horizon }, (_, i) => `FY${year + i + 1}`);
}

export function describeMethod(budget) {
  const window = `${budget.parameters.window_size}-year window`;
  const alpha = `α ${budget.parameters.alpha}`;
  const setting = { moving_average: window, exponential_smoothing: alpha, combined: `${window} + ${alpha}` }[budget.method];
  return `${METHOD_NAMES[budget.method]} (${setting})`;
}

export function fyStartDate(label, yearEndMonth) {
  const year = yearNumber(label);
  const startMonth = (yearEndMonth % 12) + 1;
  const startYear = yearEndMonth === 12 ? year : year - 1;
  return `${startYear}-${String(startMonth).padStart(2, "0")}-01`;
}

function calendarYearOf(label, yearEndMonth) {
  const year = yearNumber(label);
  return yearEndMonth >= 6 ? year : year - 1;
}

export function compareRuns({ runs, comparison }) {
  const run = (method) => runs.find((r) => r.forecasting_method === method);
  const ma = run("moving_average");
  const es = run("exponential_smoothing");
  const { horizon } = ma.parameters;
  const windowSize = ma.parameters.window_size;
  const { alpha } = es.parameters;
  const fromRun = (r) => ({ runId: r.run_id, method: r.forecasting_method, parameters: r.parameters, horizon, ...comparison.methods[r.forecasting_method] });

  return {
    years: comparison.years,
    horizon,
    windowSize,
    alpha,
    warnings: ma.parameters.warnings || [],
    createdAt: ma.created_at,
    methods: {
      moving_average: fromRun(ma),
      exponential_smoothing: fromRun(es),
      combined: {
        runIds: [ma.run_id, es.run_id],
        method: "combined",
        parameters: { window_size: windowSize, alpha },
        horizon,
        ...comparison.methods.combined,
      },
    },
  };
}

export function budgetFromRuns(data) {
  const { runs, comparison: pair, ...budget } = data;
  const comparison = compareRuns({ runs, comparison: pair });
  const chosen = comparison.methods[budget.method];
  return {
    ...budget,
    parameters: { window_size: comparison.windowSize, alpha: comparison.alpha },
    horizon: comparison.horizon,
    years: comparison.years,
    totals: chosen.totals,
    subtotals: chosen.subtotals,
    totalExpenses: chosen.totalExpenses,
    methods: comparison.methods,
  };
}

function latestIndicatorInHistory(series, historyYears, yearEndMonth) {
  if (!historyYears.length) return { period: null, value: null };
  const firstYear = calendarYearOf(historyYears[0], yearEndMonth);
  const lastYear = calendarYearOf(historyYears[historyYears.length - 1], yearEndMonth);
  const years = Object.keys(series || {}).map(Number).filter((y) => y >= firstYear && y <= lastYear);
  const period = years.length ? Math.max(...years) : null;
  return { period, value: period === null ? null : series[period] };
}

export function buildIndicatorWarnings(indicators, links, rules, historyYears, yearEndMonth) {
  return rules.map((rule) => {
    const { period, value } = latestIndicatorInHistory(indicators[rule.indicatorCode], historyYears, yearEndMonth);
    return {
      ...rule,
      indicatorName: INDICATOR_NAMES[rule.indicatorCode],
      categories: links[rule.indicatorCode] ?? [],
      period,
      value,
      triggered: value !== null && value > rule.threshold,
    };
  });
}
