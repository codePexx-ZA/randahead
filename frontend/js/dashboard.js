import * as activity from "./activity.js";
import { getApprovedSubmissions, getBusiness, getCurrentUser, getStatementDefinitions } from "./api.js";
import * as approvals from "./approvals.js";
import * as dashboard from "./dashboard-section.js";
import * as forecast from "./forecast-section.js";
import { initials } from "./format.js";
import { combineUploads } from "./importer.js";
import * as reports from "./reports.js";
import { Session } from "./session.js";
import * as settings from "./settings.js";
import { emit, fetchBudget, loadRules, loadUploads, on, state } from "./store.js";
import * as support from "./support.js";
import * as targets from "./targets.js";
import { showToast } from "./toast.js";
import * as upload from "./upload.js";

const SECTIONS = { activity, reports, dashboard, support, upload, forecast, approvals, targets, settings };

const views = document.querySelectorAll(".view");
const navItems = document.querySelectorAll(".nav-item");

const HOME_VIEW = "dashboard";

function showView(name) {
  const view = document.getElementById(`view-${name}`) || document.getElementById(`view-${HOME_VIEW}`);
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
  showView(location.hash.slice(1) || HOME_VIEW);
}

function setupNav() {
  window.addEventListener("hashchange", () => showView(location.hash.slice(1)));
  showCurrentView();

  document.getElementById("menuToggle").addEventListener("click", () => document.body.classList.toggle("sidebar-open"));
  document.getElementById("sidebarBackdrop").addEventListener("click", () => document.body.classList.remove("sidebar-open"));
}

function renderUserChip() {
  const { user } = state;
  document.getElementById("userName").textContent = user.fullName;
  document.getElementById("userRole").textContent = user.username;
  document.getElementById("userAvatar").textContent = initials(user.fullName);
}

function setupUser() {
  state.user = Session.get()?.user ?? null;
  if (!state.user) {
    window.location.replace("index.html");
    return false;
  }
  renderUserChip();
  document.getElementById("signOutBtn").addEventListener("click", () => {
    Session.clear();
    window.location.href = "index.html";
  });
  refreshUser();
  return true;
}

async function refreshUser() {
  try {
    const user = await getCurrentUser();
    if (!user) return;
    Session.update(user);
    state.user = { ...state.user, ...user };
    await emit("user");
  } catch {
  }
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
