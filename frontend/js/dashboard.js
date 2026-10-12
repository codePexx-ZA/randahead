// Dashboard page: checks the signed-in user, switches sections and loads the data every section shares
import * as activity from "./activity.js";
import { getApprovedSubmissions, getBusiness, getCurrentUser, getStatementDefinitions } from "./api.js";
import * as approvals from "./approvals.js";
import * as dashboard from "./dashboard-section.js";
import * as forecast from "./forecast-section.js";
import { initials } from "./format.js";
import { combineUploads } from "./importer.js";
import * as reports from "./reports.js";
import { ROLE_HOME, roleLabel } from "./roles.js";
import { Session } from "./session.js";
import * as settings from "./settings.js";
import { can, emit, fetchBudget, loadRules, loadUploads, on, state } from "./store.js";
import * as support from "./support.js";
import * as targets from "./targets.js";
import { showToast } from "./toast.js";
import * as upload from "./upload.js";

const SECTIONS = { activity, reports, dashboard, support, upload, forecast, approvals, targets, settings };

const views = document.querySelectorAll(".view");
const navItems = document.querySelectorAll(".nav-item");

function homeView() {
  if (state.user?.isAdmin) return "dashboard";
  return ROLE_HOME[state.user?.role] || "dashboard";
}

function allowedView(view) {
  return Boolean(view) && (!view.dataset.permission || can(view.dataset.permission));
}

function showView(name) {
  const requested = document.getElementById(`view-${name}`);
  const view = allowedView(requested) ? requested : document.getElementById(`view-${homeView()}`);
  const viewName = view.id.replace("view-", "");
  const opening = !view.classList.contains("active");

  views.forEach((v) => v.classList.toggle("active", v === view));
  navItems.forEach((item) => item.classList.toggle("active", item.dataset.view === viewName));

  document.getElementById("viewTitle").textContent = view.dataset.title;
  document.getElementById("viewSubtitle").textContent =
    view.dataset.subtitle || (state.business?.name ?? "");
  document.getElementById("yearPicker").hidden = viewName !== "dashboard" || !state.submission;
  document.title = `${view.dataset.title} | RandAhead`;
  if (opening) SECTIONS[viewName].onShow?.();

  document.body.classList.remove("sidebar-open");
}

function showCurrentView() {
  showView(location.hash.slice(1) || homeView());
}

function setupNav() {
  navItems.forEach((item) => (item.hidden = !allowedView(document.getElementById(`view-${item.dataset.view}`))));
  window.addEventListener("hashchange", () => showView(location.hash.slice(1)));
  showCurrentView();

  document.getElementById("menuToggle").addEventListener("click", () => document.body.classList.toggle("sidebar-open"));
  document.getElementById("sidebarBackdrop").addEventListener("click", () => document.body.classList.remove("sidebar-open"));
}

function renderUserChip() {
  const { user } = state;
  document.getElementById("userName").textContent = user.fullName;
  document.getElementById("userRole").textContent = roleLabel(user);
  document.getElementById("userAvatar").textContent = initials(user.fullName);
}

function setupUser() {
  state.user = Session.get()?.user ?? null;
  if (!state.user) {
    window.location.replace("index.html");
    return false;
  }
  if (state.user.mustChangePassword) {
    window.location.replace("password.html?change");
    return false;
  }
  renderUserChip();
  document.getElementById("signOutBtn").addEventListener("click", () => {
    Session.clear();
    window.location.href = "index.html";
  });

  const refreshed = refreshUser();
  return state.user.permissions ? true : refreshed;
}

async function refreshUser() {
  try {
    const user = await getCurrentUser();
    if (!user) return true;
    const roleChanged = user.role !== state.user.role || Boolean(user.isAdmin) !== Boolean(state.user.isAdmin);
    Session.update(user);
    if (user.mustChangePassword) {
      window.location.replace("password.html?change");
      return false;
    }
    if (roleChanged) {
      window.location.reload();
      return false;
    }
    state.user = { ...state.user, ...user };
    await emit("user");
  } catch {
    // The stored user stays in place; a 401 has already sent the user to the login page
  }
  return true;
}

async function loadData() {
  const shared = Promise.all([loadUploads(), loadRules()].map((load) => load.catch((err) => showToast(err.message, "error"))));
  try {
    const [business, approved] = await Promise.all([getBusiness(), getApprovedSubmissions()]);
    const withRows = approved.filter((u) => u.rows);
    state.business = business;
    state.submission = withRows.length ? combineUploads(withRows) : null;
    state.processed = state.submission?.statement ?? null;
    state.budget = state.submission ? await fetchBudget() : null;
  } catch (err) {
    showToast(err.message, "error");
    return;
  }

  document.getElementById("sidebarBusiness").textContent = state.business.name;
  showCurrentView();
  await shared;
  Object.values(SECTIONS).forEach(async (section) => section.refresh?.());
}

on("user", renderUserChip);
on("business", () => {
  document.getElementById("sidebarBusiness").textContent = state.business.name;
  showCurrentView();
});
on("reload", loadData);

async function init() {
  if (!(await setupUser())) return;
  try {
    state.definitions = await getStatementDefinitions();
  } catch (err) {
    showToast(err.message, "error");
    return;
  }
  Object.values(SECTIONS).forEach((section) => section.setup());
  setupNav();
  await loadData();
}

init();
