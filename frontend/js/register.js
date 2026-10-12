// Register page: signs up a new business with its first Decision Maker, then signs them in
import { register } from "./api.js";
import { rejectInput, setLoading, setupAuthForm, shakeCard } from "./auth-form.js";
import { EMAIL_PATTERN, INDUSTRIES, MIN_PASSWORD_LENGTH } from "./config.js";
import { monthName } from "./format.js";
import { Session } from "./session.js";
import { showToast } from "./toast.js";
import { html } from "./ui.js";

const form = document.getElementById("registerForm");
const businessInput = document.getElementById("businessInput");
const industryInput = document.getElementById("industryInput");
const yearEndInput = document.getElementById("yearEndInput");
const yearEndHint = document.getElementById("yearEndHint");
const nameInput = document.getElementById("nameInput");
const emailInput = document.getElementById("emailInput");
const passwordInput = document.getElementById("passwordInput");
const confirmInput = document.getElementById("confirmInput");
const registerBtn = document.getElementById("registerBtn");

industryInput.innerHTML = html`<option value="">Not set</option>${INDUSTRIES.map((i) => html`<option>${i}</option>`)}`;
yearEndInput.innerHTML = html`${Array.from({ length: 12 }, (_, i) => i + 1)
  .map((m) => html`<option value="${m}"${m === 2 ? " selected" : ""}>${monthName(m)}</option>`)}`;

function showYearEndHint() {
  const end = Number(yearEndInput.value);
  yearEndHint.textContent = `Your financial year runs from 1 ${monthName((end % 12) + 1)} to the end of ${monthName(end)}. ` +
    "It can't change once income statement history is approved.";
}
yearEndInput.addEventListener("change", showYearEndHint);
showYearEndHint();

setupAuthForm();

function findProblem(d) {
  if (d.businessName === "") return [businessInput, "Type the business's registered name."];
  if (d.fullName === "") return [nameInput, "Type your full name."];
  if (!EMAIL_PATTERN.test(d.email)) return [emailInput, "Type a valid email address, e.g. name@business.co.za."];
  if (d.password.length < MIN_PASSWORD_LENGTH) return [passwordInput, `Your password needs at least ${MIN_PASSWORD_LENGTH} characters.`];
  if (confirmInput.value !== d.password) return [confirmInput, "The two passwords don't match."];
  return null;
}

form.addEventListener("submit", async (e) => {
  e.preventDefault();

  const details = {
    businessName: businessInput.value.trim(),
    industry: industryInput.value,
    yearEndMonth: Number(yearEndInput.value),
    fullName: nameInput.value.trim().replace(/\s+/g, " "),
    email: emailInput.value.trim(),
    password: passwordInput.value,
  };
  const problem = findProblem(details);
  if (problem) return rejectInput(...problem);

  setLoading(registerBtn, true);
  try {
    const result = await register(details);
    Session.save(result, false);
    showToast(`Welcome, ${result.user.fullName}. ${details.businessName} is registered.`, "success");
    setTimeout(() => (window.location.href = "dashboard.html"), 1100);
  } catch (err) {
    showToast(err.message, "error");
    shakeCard();
    setLoading(registerBtn, false);
  }
});
