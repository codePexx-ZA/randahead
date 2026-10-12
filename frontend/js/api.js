// API calls: one function per C# API endpoint, all through one request helper
import { CONFIG } from "./config.js";
import { Session } from "./session.js";

const NETWORK_MESSAGE = "Can't reach the server. Check that the API is running, then try again.";

async function request(path, { method = "GET", body, auth = true, nullOn404 = false, fallback } = {}) {
  const headers = {};
  const token = auth ? Session.get()?.token : null;
  if (token) headers.Authorization = `Bearer ${token}`;
  const isForm = body instanceof FormData;
  if (body !== undefined && !isForm) headers["Content-Type"] = "application/json";

  let res;
  try {
    res = await fetch(`${CONFIG.API_BASE}${path}`, {
      method,
      headers,
      body: body === undefined || isForm ? body : JSON.stringify(body),
    });
  } catch {
    throw new Error(NETWORK_MESSAGE);
  }
  if (res.status === 401 && auth) {
    Session.clear();
    window.location.href = "index.html";
    throw new Error("Your session has ended. Sign in again.");
  }
  if (res.status === 404 && nullOn404) return null;
  if (!res.ok) throw Object.assign(new Error(await errorMessage(res, fallback)), { status: res.status });

  const type = res.headers.get("Content-Type") || "";
  if (type.includes("json")) return res.json();
  return type ? res.blob() : null;
}

async function errorMessage(res, fallback) {
  try {
    return (await res.json()).message || fallback;
  } catch {
    return fallback;
  }
}

const id = encodeURIComponent;

export async function login(email, password) {
  try {
    return await request("/auth/login", { method: "POST", body: { email, password }, auth: false, fallback: "Could not sign in. Try again." });
  } catch (err) {
    if (err.status === 401) throw new Error("Incorrect email or password.");
    throw err;
  }
}

export function register(details) {
  const body = {
    business_name: details.businessName,
    industry: details.industry || null,
    year_end_month: details.yearEndMonth,
    full_name: details.fullName,
    email: details.email,
    password: details.password,
  };
  return request("/auth/register", { method: "POST", body, auth: false, fallback: "Could not register the business. Try again." });
}

export function forgotPassword(email) {
  return request("/auth/forgot-password", { method: "POST", body: { email }, auth: false, fallback: "Could not send the reset link. Try again." });
}

export function resetPassword(token, password) {
  return request("/auth/reset-password", { method: "POST", body: { token, password }, auth: false, fallback: "Could not save the new password. Try again." });
}

export function changePassword(currentPassword, newPassword) {
  const body = { current_password: currentPassword, new_password: newPassword };
  return request("/auth/change-password", { method: "POST", body, fallback: "Could not change your password." });
}

export async function getCurrentUser() {
  if (!Session.get()) return null;
  return request("/auth/me", { fallback: "Could not check your sign-in." });
}

export function getBusiness() {
  return request("/business", { fallback: "Could not load the business details." });
}

export function updateBusiness(changes) {
  const body = { legal_name: changes.name, industry: changes.industry || null, year_end_month: changes.yearEndMonth };
  return request("/business", { method: "PUT", body, fallback: "Could not save the business details." });
}

export function getStatementDefinitions() {
  return request("/statement/definitions", { fallback: "Could not load the income statement layout." });
}

export function getApprovedSubmissions() {
  return request("/submissions?status=approved", { fallback: "Could not load the income statements." });
}

export function getPendingSubmissions() {
  return request("/submissions?status=submitted", { fallback: "Could not load the uploads waiting for approval." });
}

export function getSubmissions() {
  return request("/submissions", { fallback: "Could not load your uploads." });
}

export function previewUpload(file) {
  const body = new FormData();
  body.append("file", file);
  return request("/submissions/preview", { method: "POST", body, fallback: "Could not read the file." });
}

export function downloadTemplate() {
  return request("/submissions/template", { fallback: "Could not download the template." });
}

export function checkUpload(payload) {
  return request("/submissions/check", { method: "POST", body: payload, fallback: "Could not check the statement." });
}

export function submitUpload(payload) {
  return request("/submissions", { method: "POST", body: payload, fallback: "Could not send the upload." });
}

export function decideSubmission(submissionId, decision, comments) {
  const body = { decision, comments };
  return request(`/submissions/${id(submissionId)}/decision`, { method: "POST", body, fallback: "Could not save the decision." });
}

export function getLatestBudget() {
  return request("/budgets/latest", { nullOn404: true, fallback: "Could not load the budget." });
}

export function chooseBudget(method, runIds) {
  return request("/budgets", { method: "POST", body: { method, run_ids: runIds }, fallback: "Could not save the budget." });
}

export function decideBudget(budgetId, decision, comments) {
  const body = { decision, comments };
  return request(`/budgets/${id(budgetId)}/decision`, { method: "POST", body, fallback: "Could not save the decision." });
}

export function getLatestForecast() {
  return request("/forecasts/latest", { nullOn404: true, fallback: "Could not load the latest forecast." });
}

export function runForecasts(settings) {
  const body = { frequency: "annual", horizon: settings.horizon, window_size: settings.windowSize, alpha: settings.alpha };
  return request("/forecasts", { method: "POST", body, fallback: "Could not run the forecast." });
}

export function getDashboard(year) {
  const query = year ? `?year=${id(year)}` : "";
  return request(`/dashboard${query}`, { fallback: "Could not load the dashboard figures." });
}

export function getReport() {
  return request("/reports", { fallback: "Could not load the report." });
}

export function getRules() {
  return request("/rules", { fallback: "Could not load the warning rules." });
}

export function getIndicatorSeries() {
  return request("/indicators/series", { fallback: "Could not load the economic warnings." });
}

export function createRule(rule) {
  return request("/rules", { method: "POST", body: ruleBody(rule), fallback: "Could not add the rule." });
}

export function updateRule(ruleId, rule) {
  return request(`/rules/${id(ruleId)}`, { method: "PUT", body: ruleBody(rule), fallback: "Could not save the rule." });
}

export function deleteRule(ruleId) {
  return request(`/rules/${id(ruleId)}`, { method: "DELETE", fallback: "Could not delete the rule." });
}

export function getKpiTargets() {
  return request("/kpi-targets", { fallback: "Could not load the KPI targets." });
}

export function createKpiTarget(target) {
  return request("/kpi-targets", { method: "POST", body: kpiTargetBody(target), fallback: "Could not add the target." });
}

export function updateKpiTarget(targetId, target) {
  return request(`/kpi-targets/${id(targetId)}`, { method: "PUT", body: kpiTargetBody(target), fallback: "Could not save the target." });
}

export function deleteKpiTarget(targetId) {
  return request(`/kpi-targets/${id(targetId)}`, { method: "DELETE", fallback: "Could not delete the target." });
}

export function getUsers() {
  return request("/users", { fallback: "Could not load the users." });
}

export function createUser(user) {
  return request("/users", { method: "POST", body: userBody({ ...user, active: true }), fallback: "Could not add the user." });
}

export function updateUser(userId, user) {
  return request(`/users/${id(userId)}`, { method: "PUT", body: userBody(user), fallback: "Could not save the user." });
}

export function deleteUser(userId, password) {
  const body = password === undefined ? undefined : { password };
  return request(`/users/${id(userId)}`, { method: "DELETE", body, fallback: "Could not delete the user." });
}

export function getActivity(filters) {
  const query = new URLSearchParams(Object.entries(filters).filter(([, value]) => value)).toString();
  return request(`/audit${query ? `?${query}` : ""}`, { fallback: "Could not load the activity log." });
}

export function sendSupportMessage(message) {
  return request("/support", { method: "POST", body: message, fallback: "Could not send your message. Try again." });
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
