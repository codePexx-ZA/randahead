// API calls: talks to the C# API and keeps the session
const Api = {
  async login(email, password) {
    let res;
    try {
      res = await fetch(`${CONFIG.API_BASE}/auth/login`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });
    } catch {
      throw new Error("Can't reach the server. Check that the API is running, then try again.");
    }
    if (!res.ok) {
      if (res.status === 403 || res.status === 429) throw new Error(await errorMessage(res, "Could not sign in. Try again."));
      throw new Error(res.status === 401 ? "Incorrect email or password." : "Could not sign in. Try again.");
    }
    return res.json();
  },

  async register(details) {
    let res;
    try {
      res = await fetch(`${CONFIG.API_BASE}/auth/register`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          business_name: details.businessName,
          industry: details.industry || null,
          year_end_month: details.yearEndMonth,
          full_name: details.fullName,
          email: details.email,
          password: details.password,
        }),
      });
    } catch {
      throw new Error("Can't reach the server. Check that the API is running, then try again.");
    }
    if (!res.ok) throw new Error(await errorMessage(res, "Could not register the business. Try again."));
    return res.json();
  },

  async forgotPassword(email) {
    return postAnonymous("/auth/forgot-password", { email }, "Could not send the reset link. Try again.");
  },

  async resetPassword(token, password) {
    return postAnonymous("/auth/reset-password", { token, password }, "Could not save the new password. Try again.");
  },

  async changePassword(currentPassword, newPassword) {
    return sendJson(
      "/auth/change-password",
      "POST",
      { current_password: currentPassword, new_password: newPassword },
      "Could not change your password."
    );
  },

  async getCurrentUser() {
    if (!Session.get()) return null;
    return getJson("/auth/me", "Could not check your sign-in.");
  },

  async getLatestSubmission() {
    const [business, approved] = await Promise.all([
      getJson("/business", "Could not load the business details."),
      getJson("/submissions?status=approved", "Could not load the income statements."),
    ]);
    const withRows = approved.filter((u) => u.rows);
    return { business, submission: withRows.length ? combineUploads(withRows) : null };
  },

  async getLatestBudget(business) {
    const res = await apiFetch("/budgets/latest");
    if (res.status === 404) return null;
    if (!res.ok) throw new Error("Could not load the budget.");
    return budgetFromRuns(await res.json(), business.yearEndMonth);
  },

  async getDashboard(year) {
    const query = year ? `?year=${encodeURIComponent(year)}` : "";
    return (await apiSend(`/dashboard${query}`, {}, "Could not load the dashboard figures.")).json();
  },

  async getReport() {
    return (await apiSend("/reports", {}, "Could not load the report.")).json();
  },

  async getLatestForecast() {
    const res = await apiFetch("/forecasts/latest");
    if (res.status === 404) return null;
    if (!res.ok) throw new Error("Could not load the latest forecast.");
    return res.json();
  },

  async runForecasts(settings) {
    return sendJson("/forecasts", "POST", {
      frequency: "annual",
      horizon: settings.horizon,
      window_size: settings.windowSize,
      alpha: settings.alpha,
    }, "Could not run the forecast.");
  },

  async getIndicatorWarnings(historyYears, yearEndMonth) {
    const { rules, series } = await loadRules("Could not load the economic warnings.");
    return buildIndicatorWarnings(series, rules.filter((r) => r.active), historyYears, yearEndMonth);
  },

  async getRules(historyYears, yearEndMonth) {
    const { rules, series } = await loadRules("Could not load the warning rules.");
    return buildIndicatorWarnings(series, rules, historyYears, yearEndMonth);
  },

  async createRule(rule) {
    return sendJson("/rules", "POST", ruleBody(rule), "Could not add the rule.");
  },

  async updateRule(id, rule) {
    return sendJson(`/rules/${encodeURIComponent(id)}`, "PUT", ruleBody(rule), "Could not save the rule.");
  },

  async deleteRule(id) {
    await apiSend(`/rules/${encodeURIComponent(id)}`, { method: "DELETE" }, "Could not delete the rule.");
  },

  async getKpiTargets() {
    return getJson("/kpi-targets", "Could not load the KPI targets.");
  },

  async createKpiTarget(target) {
    return sendJson("/kpi-targets", "POST", kpiTargetBody(target), "Could not add the target.");
  },

  async updateKpiTarget(id, target) {
    return sendJson(`/kpi-targets/${encodeURIComponent(id)}`, "PUT", kpiTargetBody(target), "Could not save the target.");
  },

  async deleteKpiTarget(id) {
    await apiSend(`/kpi-targets/${encodeURIComponent(id)}`, { method: "DELETE" }, "Could not delete the target.");
  },

  async chooseBudget(comparison, method) {
    const runIds = [comparison.methods.moving_average.runId, comparison.methods.exponential_smoothing.runId];
    return sendJson("/budgets", "POST", { method, run_ids: runIds }, "Could not save the budget.");
  },

  async previewUpload(file) {
    const body = new FormData();
    body.append("file", file);
    const res = await apiSend("/submissions/preview", { method: "POST", body }, "Could not read the file.");
    return res.json();
  },

  async downloadTemplate() {
    const res = await apiSend("/submissions/template", {}, "Could not download the template.");
    return res.blob();
  },

  async submitUpload(payload) {
    return sendJson("/submissions", "POST", payload, "Could not send the upload.");
  },

  async getSubmissions() {
    return getJson("/submissions", "Could not load your uploads.");
  },

  async getPendingSubmissions() {
    return getJson("/submissions?status=submitted", "Could not load the uploads waiting for approval.");
  },

  async decideSubmission(id, decision, comments) {
    return sendJson(`/submissions/${encodeURIComponent(id)}/decision`, "POST", { decision, comments }, "Could not save the decision.");
  },

  async decideBudget(id, decision, comments) {
    return sendJson(`/budgets/${encodeURIComponent(id)}/decision`, "POST", { decision, comments }, "Could not save the decision.");
  },

  async getBusiness() {
    return getJson("/business", "Could not load the business details.");
  },

  async updateBusiness(changes) {
    const body = { legal_name: changes.name, industry: changes.industry || null, year_end_month: changes.yearEndMonth };
    return sendJson("/business", "PUT", body, "Could not save the business details.");
  },

  async getUsers() {
    return getJson("/users", "Could not load the users.");
  },

  async createUser(user) {
    return sendJson("/users", "POST", userBody({ ...user, active: true }), "Could not add the user.");
  },

  async updateUser(id, user) {
    return sendJson(`/users/${encodeURIComponent(id)}`, "PUT", userBody(user), "Could not save the user.");
  },

  async deleteUser(id, password) {
    const options = password === undefined
      ? { method: "DELETE" }
      : { method: "DELETE", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ password }) };
    await apiSend(`/users/${encodeURIComponent(id)}`, options, "Could not delete the user.");
  },

  async getActivity(filters) {
    const query = new URLSearchParams(Object.entries(filters).filter(([, value]) => value)).toString();
    const res = await apiSend(`/audit${query ? `?${query}` : ""}`, {}, "Could not load the activity log.");
    return res.json();
  },

  async sendSupportMessage(message) {
    return sendJson("/support", "POST", message, "Could not send your message. Try again.");
  },
};

async function loadRules(failMessage) {
  const [rules, series] = await Promise.all([getJson("/rules", failMessage), getJson("/indicators/series", failMessage)]);
  return { rules: rules.map((r) => ({ ...r, threshold: Number(r.threshold) })), series };
}

function budgetFromRuns(data, yearEndMonth) {
  const { runs, ...budget } = data;
  const comparison = compareRuns(runs, yearEndMonth);
  const chosen = comparison.methods[budget.method];
  return {
    ...budget,
    parameters: { window_size: comparison.windowSize, alpha: comparison.alpha },
    horizon: comparison.horizon,
    years: comparison.years,
    totals: chosen.totals,
    subtotals: chosen.subtotals,
    methods: comparison.methods,
  };
}

async function apiFetch(path, options = {}) {
  const token = Session.get()?.token;
  const headers = { ...options.headers };
  if (token) headers.Authorization = `Bearer ${token}`;

  let res;
  try {
    res = await fetch(`${CONFIG.API_BASE}${path}`, { ...options, headers });
  } catch {
    throw new Error("Can't reach the server. Check that the API is running, then try again.");
  }
  if (res.status === 401) {
    Session.clear();
    window.location.href = "index.html";
    throw new Error("Your session has ended. Sign in again.");
  }
  return res;
}

async function getJson(path, failMessage) {
  const res = await apiFetch(path);
  if (!res.ok) throw new Error(failMessage);
  return res.json();
}

async function apiSend(path, options, fallback) {
  const res = await apiFetch(path, options);
  if (!res.ok) throw new Error(await errorMessage(res, fallback));
  return res;
}

async function postAnonymous(path, body, fallback) {
  let res;
  try {
    res = await fetch(`${CONFIG.API_BASE}${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  } catch {
    throw new Error("Can't reach the server. Check that the API is running, then try again.");
  }
  if (!res.ok) throw new Error(await errorMessage(res, fallback));
  return res.json();
}

async function sendJson(path, method, body, fallback) {
  const options = { method, headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) };
  const res = await apiSend(path, options, fallback);
  return res.json();
}

function userBody(user) {
  return {
    full_name: user.fullName,
    email: user.email,
    role: user.role,
    active_status: user.active ? "active" : "inactive",
    ...(user.password ? { password: user.password } : {}),
  };
}

function ruleBody(rule) {
  return {
    rule_name: rule.name,
    indicator_code: rule.indicatorCode,
    threshold_value: rule.threshold,
    active_status: rule.active ? "active" : "inactive",
  };
}

function kpiTargetBody(target) {
  return {
    metric_name: target.metricName,
    target_value: target.targetValue,
    measurement_unit: target.measurementUnit,
    target_period: target.targetPeriod,
  };
}

async function errorMessage(res, fallback) {
  try {
    return (await res.json()).message || fallback;
  } catch {
    return fallback;
  }
}

const Session = {
  save(data, remember) {
    (remember ? localStorage : sessionStorage).setItem("session", JSON.stringify(data));
  },
  get() {
    const raw = localStorage.getItem("session") || sessionStorage.getItem("session");
    const data = raw ? JSON.parse(raw) : null;
    if (data?.expiresAt && new Date(data.expiresAt) <= new Date()) {
      this.clear();
      return null;
    }
    return data;
  },
  update(changes) {
    const store = localStorage.getItem("session") ? localStorage : sessionStorage;
    const data = this.get();
    if (!data) return;
    data.user = { ...data.user, ...changes };
    store.setItem("session", JSON.stringify(data));
  },
  clear() {
    localStorage.removeItem("session");
    sessionStorage.removeItem("session");
  },
};
