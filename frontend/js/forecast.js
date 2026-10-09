// Forecast maths: Moving Average, Exponential Smoothing and warnings
const METHOD_NAMES = {
  moving_average: "Moving Average",
  exponential_smoothing: "Exponential Smoothing",
  combined: "Both (average)",
};

function averageOfMethods(ma, es) {
  return Math.round((ma + es) / 2);
}

function movingAverageForecast(values, windowSize) {
  const w = Math.min(windowSize, values.length);
  return values.slice(-w).reduce((sum, v) => sum + v, 0) / w;
}

function exponentialSmoothingForecast(values, alpha) {
  return values.slice(1).reduce((level, v) => alpha * v + (1 - alpha) * level, values[0]);
}

function yearNumber(label) {
  return parseInt(label.replace(/\D/g, ""), 10);
}

function nextYearLabels(lastYear, horizon) {
  const year = yearNumber(lastYear);
  return Array.from({ length: horizon }, (_, i) => `FY${year + i + 1}`);
}

function buildBudget(processed, budget) {
  const windowSize = budget.parameters.window_size ?? 3;
  const alpha = budget.parameters.alpha ?? 0.5;
  const years = nextYearLabels(processed.years[processed.years.length - 1], budget.horizon);
  const flat = (value) => new Array(years.length).fill(value);

  const forecasts = { moving_average: {}, exponential_smoothing: {}, combined: {} };
  LINE_CATEGORIES.forEach((c) => {
    const ma = Math.round(movingAverageForecast(processed.totals[c.code], windowSize));
    const es = Math.round(exponentialSmoothingForecast(processed.totals[c.code], alpha));
    forecasts.moving_average[c.code] = flat(ma);
    forecasts.exponential_smoothing[c.code] = flat(es);
    forecasts.combined[c.code] = flat(averageOfMethods(ma, es));
  });

  const methods = {};
  Object.entries(forecasts).forEach(([method, totals]) => {
    methods[method] = { totals, subtotals: calculateSubtotals(totals, years.length) };
  });
  const { totals, subtotals } = methods[budget.method];

  return { ...budget, parameters: { window_size: windowSize, alpha }, years, totals, subtotals, methods };
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

function runForecastsLocally(processed, settings, yearEndMonth, unitId) {
  const { windowSize, alpha, horizon } = settings;
  const history = processed.years;
  const futureYears = nextYearLabels(history[history.length - 1], horizon);

  const warnings = [];
  if (history.length < 3) {
    warnings.push(`Short history: ${history.length} annual periods (recommended at least 3). Treat forecasts with caution.`);
  }

  const methods = {
    moving_average: [{ window_size: windowSize }, (v) => movingAverageForecast(v, windowSize)],
    exponential_smoothing: [{ alpha }, (v) => exponentialSmoothingForecast(v, alpha)],
  };
  const createdAt = new Date().toISOString();

  return Object.entries(methods).map(([method, [params, forecastOne]], i) => ({
    run_id: `run-${Date.now()}-${i}`,
    unit_id: unitId,
    forecasting_method: method,
    history_start: fyStartDate(history[0], yearEndMonth),
    history_end: fyStartDate(history[history.length - 1], yearEndMonth),
    parameters: { ...params, frequency: "annual", horizon, warnings },
    created_at: createdAt,
    values: LINE_CATEGORIES.flatMap((c) => {
      const amount = Math.round(forecastOne(processed.totals[c.code]));
      return futureYears.map((year) => ({
        category: c.code,
        display_name: c.name,
        forecast_period: fyStartDate(year, yearEndMonth),
        baseline_amount: amount,
        adjusted_amount: null,
      }));
    }),
  }));
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
