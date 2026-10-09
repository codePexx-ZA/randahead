// API calls: talks to the C# API and keeps the session
const Api = {
  async login(email, password) {
    if (CONFIG.USE_MOCK_LOGIN) {
      await delay(700);
      const user = MockStore.getUsers().find((u) => u.email === email.toLowerCase() && u.password === password);
      if (!user) throw new Error("Incorrect email or password.");
      if (!user.active) throw new Error("This account is switched off. Ask your Approver or Decision Maker to switch it back on.");
      return {
        token: "mock-token",
        user: { id: user.id, fullName: user.fullName, email: user.email, role: user.role, isAdmin: Boolean(user.isAdmin) },
      };
    }

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
    if (CONFIG.USE_MOCK_LOGIN) throw new Error("Registering a business needs the API. Set USE_MOCK_LOGIN to false.");
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
    if (CONFIG.USE_MOCK_LOGIN) throw new Error("Password reset emails need the API. Set USE_MOCK_LOGIN to false.");
    return postAnonymous("/auth/forgot-password", { email }, "Could not send the reset link. Try again.");
  },

  async resetPassword(token, password) {
    if (CONFIG.USE_MOCK_LOGIN) throw new Error("Password reset needs the API. Set USE_MOCK_LOGIN to false.");
    return postAnonymous("/auth/reset-password", { token, password }, "Could not save the new password. Try again.");
  },

  async changePassword(currentPassword, newPassword) {
    if (Session.get()?.token === "mock-token") {
      await delay(500);
      const me = Session.get().user;
      const users = MockStore.getUsers();
      const account = users.find((u) => u.id === me.id);
      if (!account || account.password !== currentPassword) throw new Error("Your current password is incorrect.");
      account.password = newPassword;
      MockStore.saveUsers(users);
      return { ...me, mustChangePassword: false };
    }
    return sendJson(
      "/auth/change-password",
      "POST",
      { current_password: currentPassword, new_password: newPassword },
      "Could not change your password."
    );
  },

  async getCurrentUser() {
    const session = Session.get();
    if (!session || session.token === "mock-token") return session?.user ?? null;
    return getJson("/auth/me", "Could not check your sign-in.");
  },

  async getLatestSubmission() {
    if (!mockSubmissions()) {
      const [business, approved] = await Promise.all([
        getJson("/business", "Could not load the business details."),
        getJson("/submissions?status=approved", "Could not load the income statements."),
      ]);
      const withRows = approved.filter((u) => u.rows);
      return { business, submission: withRows.length ? combineUploads(withRows) : null };
    }
    await delay(500);
    const approved = MockStore.getUploads().filter((u) => u.status === "approved" && u.rows);
    return { business: MockStore.getBusiness(), submission: approved.length ? combineUploads(approved) : MOCK_SUBMISSION };
  },

  async getLatestBudget(processed, business) {
    if (!mockForecasts()) {
      const res = await apiFetch("/budgets/latest");
      if (res.status === 404) return null;
      if (!res.ok) throw new Error("Could not load the budget.");
      return budgetFromRuns(await res.json(), business.yearEndMonth);
    }
    await delay(300);
    return buildBudget(processed, MockStore.getBudget() || MOCK_BUDGET);
  },

  async getLatestForecast() {
    if (mockForecasts()) {
      await delay(300);
      return MockStore.getForecastRuns();
    }

    const res = await apiFetch("/forecasts/latest");
    if (res.status === 404) return null;
    if (!res.ok) throw new Error("Could not load the latest forecast.");
    return res.json();
  },

  async runForecasts(settings, processed, business) {
    if (!mockForecasts()) {
      return sendJson("/forecasts", "POST", {
        frequency: "annual",
        horizon: settings.horizon,
        window_size: settings.windowSize,
        alpha: settings.alpha,
      }, "Could not run the forecast.");
    }
    await delay(900);
    const runs = runForecastsLocally(processed, settings, business.yearEndMonth, business.unitId);
    MockStore.saveForecastRuns(runs);
    return runs;
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
    if (!mockTargets()) return sendJson("/rules", "POST", ruleBody(rule), "Could not add the rule.");
    await delay(500);
    const rules = MockStore.getRules();
    assertUniqueRuleName(rules, rule.name, null);
    const created = { id: `rule-${Date.now()}`, ...rule, deletable: true };
    MockStore.saveRules([...rules, created]);
    return created;
  },

  async updateRule(id, rule) {
    if (!mockTargets()) return sendJson(`/rules/${encodeURIComponent(id)}`, "PUT", ruleBody(rule), "Could not save the rule.");
    await delay(500);
    const rules = MockStore.getRules();
    const target = rules.find((r) => r.id === id);
    if (!target) throw new Error("This rule no longer exists.");
    assertUniqueRuleName(rules, rule.name, id);
    Object.assign(target, rule);
    MockStore.saveRules(rules);
    return target;
  },

  async deleteRule(id) {
    if (!mockTargets()) {
      await apiSend(`/rules/${encodeURIComponent(id)}`, { method: "DELETE" }, "Could not delete the rule.");
      return;
    }
    await delay(400);
    const rules = MockStore.getRules();
    const target = rules.find((r) => r.id === id);
    if (target && !target.deletable) throw new Error("This rule has raised warnings before, so it can only be switched off.");
    MockStore.saveRules(rules.filter((r) => r.id !== id));
  },

  async getKpiTargets() {
    if (!mockTargets()) return getJson("/kpi-targets", "Could not load the KPI targets.");
    await delay(300);
    return MockStore.getKpiTargets();
  },

  async createKpiTarget(target) {
    if (!mockTargets()) return sendJson("/kpi-targets", "POST", kpiTargetBody(target), "Could not add the target.");
    await delay(500);
    const targets = MockStore.getKpiTargets();
    assertUniqueTarget(targets, target, null);
    const created = { id: `kpi-${Date.now()}`, ...target };
    MockStore.saveKpiTargets([...targets, created]);
    return created;
  },

  async updateKpiTarget(id, target) {
    if (!mockTargets()) {
      return sendJson(`/kpi-targets/${encodeURIComponent(id)}`, "PUT", kpiTargetBody(target), "Could not save the target.");
    }
    await delay(500);
    const targets = MockStore.getKpiTargets();
    const existing = targets.find((t) => t.id === id);
    if (!existing) throw new Error("This target no longer exists.");
    assertUniqueTarget(targets, target, id);
    Object.assign(existing, target);
    MockStore.saveKpiTargets(targets);
    return existing;
  },

  async deleteKpiTarget(id) {
    if (!mockTargets()) {
      await apiSend(`/kpi-targets/${encodeURIComponent(id)}`, { method: "DELETE" }, "Could not delete the target.");
      return;
    }
    await delay(400);
    MockStore.saveKpiTargets(MockStore.getKpiTargets().filter((t) => t.id !== id));
  },

  async chooseBudget(comparison, method) {
    const runIds = [comparison.methods.moving_average.runId, comparison.methods.exponential_smoothing.runId];
    if (!mockForecasts()) return sendJson("/budgets", "POST", { method, run_ids: runIds }, "Could not save the budget.");
    await delay(700);
    const budget = {
      id: `bud-${Date.now()}`,
      runIds,
      method,
      parameters: { window_size: comparison.windowSize, alpha: comparison.alpha },
      horizon: comparison.horizon,
      status: "submitted",
      chosenBy: currentUserName(),
      chosenOn: todayIso(),
    };
    MockStore.saveBudget(budget);
    return budget;
  },

  async previewUpload(file) {
    if (mockSubmissions()) return buildPreview(await readSpreadsheet(file), file.name, MockStore.getMappings());
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
    if (!mockSubmissions()) return sendJson("/submissions", "POST", payload, "Could not send the upload.");
    await delay(800);
    const created = {
      id: `sub-${Date.now()}`,
      fileName: payload.fileName,
      years: [payload.years[0], payload.years[payload.years.length - 1]],
      periods: payload.years,
      scale: payload.scale,
      rows: payload.rows,
      submittedBy: currentUserName(),
      submittedOn: todayIso(),
      status: "submitted",
      replacesId: payload.replacesSubmissionId || null,
    };
    MockStore.saveUploads([created, ...MockStore.getUploads()]);

    const mappings = MockStore.getMappings();
    payload.rows
      .filter((row) => row.remember)
      .forEach((row) => (mappings[labelKey(row.label)] = row.mapping));
    MockStore.saveMappings(mappings);
    return created;
  },

  async getSubmissions() {
    if (!mockSubmissions()) return getJson("/submissions", "Could not load your uploads.");
    await delay(300);
    return MockStore.getUploads();
  },

  async getPendingSubmissions() {
    if (!mockSubmissions()) return getJson("/submissions?status=submitted", "Could not load the uploads waiting for approval.");
    await delay(300);
    return MockStore.getUploads().filter((u) => u.status === "submitted");
  },

  async decideSubmission(id, decision, comments) {
    if (!mockSubmissions()) {
      return sendJson(`/submissions/${encodeURIComponent(id)}/decision`, "POST", { decision, comments }, "Could not save the decision.");
    }
    await delay(700);
    const uploads = MockStore.getUploads();
    const target = uploads.find((u) => u.id === id);
    if (!target || target.status !== "submitted") throw new Error("This upload has already been decided.");

    Object.assign(target, mockDecision(decision, comments));
    if (decision === "approved") {
      const corrected = uploads.find((u) => u.id === target.replacesId && u.status === "rejected");
      if (corrected) Object.assign(corrected, { status: "superseded", supersededBy: target.id });
      fullyCoveredUploads(uploads).forEach(({ upload, by }) =>
        Object.assign(upload, { status: "superseded", supersededBy: by.id })
      );
    }
    MockStore.saveUploads(uploads);
    return target;
  },

  async decideBudget(id, decision, comments) {
    if (!mockForecasts()) {
      return sendJson(`/budgets/${encodeURIComponent(id)}/decision`, "POST", { decision, comments }, "Could not save the decision.");
    }
    await delay(700);
    const budget = MockStore.getBudget();
    if (!budget || budget.id !== id || budget.status !== "submitted") {
      throw new Error("This budget has already been decided.");
    }
    const result = mockDecision(decision, comments);
    Object.assign(budget, result);
    if (decision === "approved") {
      budget.approvedBy = result.decidedBy;
      budget.approvedOn = result.decidedOn;
    }
    MockStore.saveBudget(budget);
    return budget;
  },

  async getBusiness() {
    if (!mockUsers()) return getJson("/business", "Could not load the business details.");
    await delay(300);
    return MockStore.getBusiness();
  },

  async updateBusiness(changes) {
    if (!mockUsers()) {
      const body = { legal_name: changes.name, industry: changes.industry || null, year_end_month: changes.yearEndMonth };
      return sendJson("/business", "PUT", body, "Could not save the business details.");
    }
    await delay(500);
    const business = MockStore.getBusiness();
    if (business.hasApprovedHistory && changes.yearEndMonth !== business.yearEndMonth) {
      throw new Error("The financial year-end can't change once income statement history is approved.");
    }
    Object.assign(business, changes);
    MockStore.saveBusiness(business);
    return business;
  },

  async getUsers() {
    if (!mockUsers()) return getJson("/users", "Could not load the users.");
    await delay(300);
    return MockStore.getUsers().map(publicUser);
  },

  async createUser(user) {
    if (!mockUsers()) return sendJson("/users", "POST", userBody({ ...user, active: true }), "Could not add the user.");
    await delay(500);
    const users = MockStore.getUsers();
    assertUniqueEmail(users, user.email, null);
    const created = { id: `usr-${Date.now()}`, ...user, email: user.email.toLowerCase(), active: true, deletable: true };
    MockStore.saveUsers([...users, created]);
    return publicUser(created);
  },

  async updateUser(id, user) {
    if (!mockUsers()) return sendJson(`/users/${encodeURIComponent(id)}`, "PUT", userBody(user), "Could not save the user.");
    await delay(500);
    const users = MockStore.getUsers();
    if (!users.some((u) => u.id === id)) throw new Error("This user no longer exists.");
    assertUniqueEmail(users, user.email, id);
    const { password, ...fields } = user;
    const changed = users.map((u) => (u.id === id ? { ...u, ...fields, ...(password ? { password } : {}), email: user.email.toLowerCase() } : u));
    assertKeyRolesKept(users, changed);
    MockStore.saveUsers(changed);
    return publicUser(changed.find((u) => u.id === id));
  },

  async deleteUser(id, password) {
    if (!mockUsers()) {
      const options = password === undefined
        ? { method: "DELETE" }
        : { method: "DELETE", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ password }) };
      await apiSend(`/users/${encodeURIComponent(id)}`, options, "Could not delete the user.");
      return;
    }
    await delay(400);
    const users = MockStore.getUsers();
    const target = users.find((u) => u.id === id);
    const me = Session.get()?.user;
    if (me?.isAdmin) {
      const account = users.find((u) => u.id === me.id);
      if (!account || account.password !== password) throw new Error("Incorrect password, so the user was not deleted.");
    } else if (target && !target.deletable) {
      throw new Error("This user has uploads or decisions on record, so they can only be switched off.");
    }
    const remaining = users.filter((u) => u.id !== id);
    assertKeyRolesKept(users, remaining);
    MockStore.saveUsers(remaining);
    if (target && !target.deletable) markFormerUser(target.fullName);
  },

  async getActivity(filters) {
    if (mockUsers()) {
      await delay(300);
      return { scope: "own", more: false, people: null, entries: [] };
    }
    const query = new URLSearchParams(Object.entries(filters).filter(([, value]) => value)).toString();
    const res = await apiSend(`/audit${query ? `?${query}` : ""}`, {}, "Could not load the activity log.");
    return res.json();
  },
};

function delay(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

function mockUsers() {
  return CONFIG.USE_MOCK_USERS || Session.get()?.token === "mock-token";
}

function mockSubmissions() {
  return CONFIG.USE_MOCK_SUBMISSIONS || Session.get()?.token === "mock-token";
}

function mockForecasts() {
  return CONFIG.USE_MOCK_FORECASTS || Session.get()?.token === "mock-token";
}

function mockTargets() {
  return CONFIG.USE_MOCK_TARGETS || Session.get()?.token === "mock-token";
}

async function loadRules(failMessage) {
  if (mockTargets()) {
    await delay(300);
    return { rules: MockStore.getRules(), series: MOCK_INDICATORS };
  }
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
  if (token && token !== "mock-token") headers.Authorization = `Bearer ${token}`;

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

function currentUserName() {
  const session = Session.get();
  return session ? session.user.fullName : "You";
}

function todayIso() {
  return new Date().toISOString().slice(0, 10);
}

function publicUser(u) {
  const { password, ...rest } = u;
  return rest;
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

function assertUniqueEmail(users, email, ownId) {
  const key = email.trim().toLowerCase();
  if (users.some((u) => u.id !== ownId && u.email.toLowerCase() === key)) {
    throw new Error(`${email.trim()} already has an account. Use another email address.`);
  }
}

function assertKeyRolesKept(before, after) {
  const names = { approver: "Approver", decision_maker: "Decision Maker" };
  const has = (users, role) => users.some((u) => u.active && u.role === role);
  const lost = Object.keys(names).find((role) => has(before, role) && !has(after, role));
  if (lost) throw new Error(`This would leave the business without an active ${names[lost]}, so the change can't be saved.`);
}

function markFormerUser(name) {
  const label = `${name} (former user)`;
  const relabel = (record, fields) => {
    let changed = false;
    fields.forEach((f) => {
      if (record[f] === name) {
        record[f] = label;
        changed = true;
      }
    });
    return changed;
  };

  const uploads = MockStore.getUploads();
  if (uploads.map((u) => relabel(u, ["submittedBy", "decidedBy"])).some(Boolean)) MockStore.saveUploads(uploads);
  const budget = MockStore.getBudget() || { ...MOCK_BUDGET };
  if (relabel(budget, ["chosenBy", "decidedBy", "approvedBy"])) MockStore.saveBudget(budget);
}

function mockDecision(decision, comments) {
  return {
    status: decision,
    decision,
    decidedBy: currentUserName(),
    decidedOn: todayIso(),
    comment: comments || null,
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

function assertUniqueRuleName(rules, name, ownId) {
  const key = name.trim().toLowerCase();
  if (rules.some((r) => r.id !== ownId && r.name.trim().toLowerCase() === key)) {
    throw new Error(`A rule called "${name.trim()}" already exists. Pick another name.`);
  }
}

function assertUniqueTarget(targets, target, ownId) {
  if (targets.some((t) => t.id !== ownId && t.metricName === target.metricName && t.targetPeriod === target.targetPeriod)) {
    throw new Error("There's already a target for this KPI and year. Edit that one instead.");
  }
}

async function errorMessage(res, fallback) {
  try {
    return (await res.json()).message || fallback;
  } catch {
    return fallback;
  }
}

function loadMock(key, fallback = () => null) {
  return JSON.parse(localStorage.getItem(key) || "null") || fallback();
}

function saveMock(key, value) {
  localStorage.setItem(key, JSON.stringify(value));
}

const MockStore = {
  getBusiness: () => ({ ...MOCK_BUSINESS, ...loadMock("mockBusiness") }),
  saveBusiness: (business) => saveMock("mockBusiness", business),
  getUsers: () => loadMock("mockUsers", () => MOCK_APP_USERS.map((u) => ({ ...u }))),
  saveUsers: (list) => saveMock("mockUsers", list),
  getRules: () => loadMock("mockRules", () => MOCK_WARNING_RULES.map((r) => ({ ...r }))),
  saveRules: (list) => saveMock("mockRules", list),
  getKpiTargets: () => loadMock("mockKpiTargets", () => MOCK_KPI_TARGETS.map((t) => ({ ...t }))),
  saveKpiTargets: (list) => saveMock("mockKpiTargets", list),
  getForecastRuns: () => loadMock("mockForecastRuns"),
  saveForecastRuns: (runs) => saveMock("mockForecastRuns", runs),
  getBudget: () => loadMock("mockBudget"),
  saveBudget: (budget) => saveMock("mockBudget", budget),
  getUploads: () => loadMock("mockUploads", () => [buildDemoPendingUpload(), ...MOCK_UPLOADS.map((u) => ({ ...u }))]).map(withDemoRows),
  saveUploads: (list) => saveMock("mockUploads", list),
  getMappings: () => loadMock("mockMappings", () => ({})),
  saveMappings: (map) => saveMock("mockMappings", map),
};

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
