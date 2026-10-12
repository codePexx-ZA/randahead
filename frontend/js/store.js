// Store: the dashboard's shared data (user, business, statement definitions, uploads, rules, statement, budget) and its change events
import * as api from "./api.js";
import { CONFIG } from "./config.js";
import { budgetFromRuns, buildIndicatorWarnings } from "./forecast.js";
import { ACTION_ROLE, DEMO_ACCOUNTS, roleLabel } from "./roles.js";

export const state = {
  user: null,
  business: null,
  definitions: null,
  submission: null,
  processed: null,
  budget: null,
  uploads: null,
  rules: null,
  indicatorSeries: null,
};

const listeners = {};

export function on(topic, fn) {
  (listeners[topic] ??= []).push(fn);
}

export function emit(topic) {
  return Promise.all((listeners[topic] || []).map((fn) => fn()));
}

export function can(action) {
  return Boolean(state.user?.permissions?.includes(action));
}

export function showRoleNote(noteId, action, text) {
  if (can(action)) return;
  const role = state.user ? roleLabel(state.user) : "a guest";
  const demo = CONFIG.SHOW_DEMO_ACCOUNTS ? ` To try it, sign in as ${DEMO_ACCOUNTS[ACTION_ROLE[action]]} (password demo123).` : "";
  const note = document.getElementById(noteId);
  note.querySelector("span").textContent = `You're signed in as ${role}: ${text}${demo}`;
  note.hidden = false;
}

export function getCategory(code) {
  return state.definitions.categories.find((c) => c.code === code);
}

export function subtotalName(code) {
  return state.definitions.subtotals.find((s) => s.code === code).name;
}

export function getKpi(code) {
  return state.definitions.kpis.find((k) => k.code === code);
}

export function historyYears() {
  return state.processed?.years ?? [];
}

export function indicatorWarnings(activeOnly) {
  if (!state.rules) return [];
  const rules = activeOnly ? state.rules.filter((r) => r.active) : state.rules;
  return buildIndicatorWarnings(state.indicatorSeries, state.definitions.indicators, rules, historyYears(), state.business.yearEndMonth);
}

export async function fetchBudget() {
  const data = await api.getLatestBudget();
  return data && budgetFromRuns(data);
}

export async function loadUploads() {
  state.uploads = await api.getSubmissions();
}

export async function loadRules() {
  const [rules, series] = await Promise.all([api.getRules(), state.indicatorSeries ?? api.getIndicatorSeries()]);
  state.rules = rules.map((r) => ({ ...r, threshold: Number(r.threshold) }));
  state.indicatorSeries = series;
}

export async function reloadBudget() {
  state.budget = await fetchBudget();
  await emit("budget");
}

export async function reloadUploads() {
  await loadUploads();
  await emit("uploads");
}

export async function reloadRules() {
  await loadRules();
  await emit("rules");
}
