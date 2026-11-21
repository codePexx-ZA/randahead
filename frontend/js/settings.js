import { updateBusiness } from "./api.js";
import { INDUSTRIES } from "./config.js";
import { monthName } from "./format.js";
import { emit, state } from "./store.js";
import { showToast } from "./toast.js";
import { html, raw, withBusy } from "./ui.js";

const st = { busy: false };

function bizValues() {
  return {
    name: document.getElementById("bizName").value.trim(),
    industry: document.getElementById("bizIndustry").value.trim(),
    yearEndMonth: Number(document.getElementById("bizYearEnd").value),
  };
}

function bizDirty() {
  const b = state.business;
  const v = bizValues();
  return v.name !== b.name || v.industry !== (b.industry || "") || v.yearEndMonth !== b.yearEndMonth;
}

function updateBizButtons() {
  const dirty = bizDirty();
  document.getElementById("bizSave").hidden = !dirty;
  document.getElementById("bizUndo").hidden = !dirty;
}

function renderBusiness() {
  const b = state.business;
  const name = document.getElementById("bizName");
  const industry = document.getElementById("bizIndustry");
  const yearEnd = document.getElementById("bizYearEnd");

  name.value = b.name;
  const industries = b.industry && !INDUSTRIES.includes(b.industry) ? [b.industry, ...INDUSTRIES] : INDUSTRIES;
  industry.innerHTML = html`<option value="">Not set</option>${industries
    .map((i) => html`<option value="${i}"${raw(i === b.industry ? " selected" : "")}>${i}</option>`)}`;
  yearEnd.innerHTML = html`${Array.from({ length: 12 }, (_, i) => i + 1)
    .map((m) => html`<option value="${m}"${raw(m === b.yearEndMonth ? " selected" : "")}>${monthName(m)}</option>`)}`;
  [name, industry].forEach((input) => input.classList.remove("invalid"));
  yearEnd.disabled = b.hasHistory;

  const lastMonth = monthName(b.yearEndMonth);
  const firstMonth = monthName((b.yearEndMonth % 12) + 1);
  const lock = b.hasHistory
    ? "The year-end is locked because an income statement is already saved: every financial year label and target date depends on it."
    : "Set this before the first upload or KPI target; it can't change afterwards.";
  document.getElementById("bizYearEndHint").textContent = `Financial years run from ${firstMonth} to ${lastMonth}. ${lock}`;
  document.getElementById("bizUnit").textContent = `${b.unitName || "Whole company"} · ${b.currency === "ZAR" || !b.currency ? "Rand (ZAR)" : b.currency}`;
  updateBizButtons();
}

async function saveBusiness(e) {
  e.preventDefault();
  if (st.busy || !bizDirty()) return;

  const v = bizValues();
  const name = document.getElementById("bizName");
  if (!v.name) {
    name.classList.add("invalid");
    name.focus();
    showToast("Type the business's registered name.", "error");
    return;
  }

  st.busy = true;
  try {
    await withBusy(document.getElementById("bizSave"), async () => {
      const saved = await updateBusiness({ name: v.name, industry: v.industry || null, yearEndMonth: v.yearEndMonth });
      state.business = { ...state.business, ...saved };
      await emit("business");
      renderBusiness();
      showToast("Business details saved.", "success");
    });
  } catch (err) {
    showToast(err.message, "error");
  } finally {
    st.busy = false;
  }
}

export function setup() {
  const bizForm = document.getElementById("bizForm");
  bizForm.addEventListener("submit", saveBusiness);
  bizForm.addEventListener("input", (e) => {
    e.target.classList.remove("invalid");
    if (state.business) updateBizButtons();
  });
  bizForm.addEventListener("change", () => state.business && updateBizButtons());
  document.getElementById("bizUndo").addEventListener("click", renderBusiness);
}

export function refresh() {
  if (state.business) renderBusiness();
}
