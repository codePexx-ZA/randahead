// Password page: emails a reset link, sets a new password from that link, or changes the signed-in user's password
import { changePassword, forgotPassword, resetPassword } from "./api.js";
import { rejectInput, setLoading, setupAuthForm, shakeCard } from "./auth-form.js";
import { EMAIL_PATTERN, MIN_PASSWORD_LENGTH } from "./config.js";
import { Session } from "./session.js";
import { showToast } from "./toast.js";

const params = new URLSearchParams(window.location.search);
const resetToken = params.get("token");
const session = Session.get();

document.querySelectorAll(".pw-min").forEach((el) => (el.textContent = MIN_PASSWORD_LENGTH));
setupAuthForm();

function newPasswordProblem(passwordInput, confirmInput) {
  if (passwordInput.value.length < MIN_PASSWORD_LENGTH) {
    return [passwordInput, `Your new password needs at least ${MIN_PASSWORD_LENGTH} characters.`];
  }
  if (confirmInput.value !== passwordInput.value) return [confirmInput, "The two passwords don't match."];
  return null;
}

async function submitForm(form, action) {
  const btn = form.querySelector(".btn");
  setLoading(btn, true);
  try {
    await action();
  } catch (err) {
    showToast(err.message, "error");
    shakeCard();
    setLoading(btn, false);
  }
}

function setupForgot() {
  const form = document.getElementById("forgotForm");
  const emailInput = document.getElementById("forgotEmail");
  form.hidden = false;
  emailInput.focus();

  form.addEventListener("submit", (e) => {
    e.preventDefault();
    const email = emailInput.value.trim();
    if (!EMAIL_PATTERN.test(email)) return rejectInput(emailInput, "Type a valid email address, e.g. name@business.co.za.");
    submitForm(form, async () => {
      const result = await forgotPassword(email);
      document.getElementById("sentText").textContent = result.message;
      form.hidden = true;
      document.getElementById("sentPanel").hidden = false;
    });
  });
}

function setupReset() {
  const form = document.getElementById("resetForm");
  const passwordInput = document.getElementById("resetPassword");
  const confirmInput = document.getElementById("resetConfirm");
  window.history.replaceState(null, "", "password.html");
  form.hidden = false;
  passwordInput.focus();

  form.addEventListener("submit", (e) => {
    e.preventDefault();
    const problem = newPasswordProblem(passwordInput, confirmInput);
    if (problem) return rejectInput(...problem);
    submitForm(form, async () => {
      const result = await resetPassword(resetToken, passwordInput.value);
      showToast(result.message, "success");
      setTimeout(() => (window.location.href = "index.html"), 1500);
    });
  });
}

function setupChange() {
  const form = document.getElementById("changeForm");
  const currentInput = document.getElementById("changeCurrent");
  const passwordInput = document.getElementById("changePassword");
  const confirmInput = document.getElementById("changeConfirm");
  const forced = Boolean(session.user.mustChangePassword);

  document.getElementById("changeIntro").textContent = forced
    ? `Hi ${session.user.fullName}. You signed in with a temporary password, so choose your own before you carry on.`
    : `Signed in as ${session.user.email}. Use at least ${MIN_PASSWORD_LENGTH} characters.`;
  document.getElementById("changeCurrentLabel").textContent = forced ? "Temporary password" : "Current password";
  document.getElementById("changeBack").hidden = forced;
  document.getElementById("changeSignOut").hidden = !forced;
  document.getElementById("signOutBtn").addEventListener("click", () => {
    Session.clear();
    window.location.href = "index.html";
  });
  form.hidden = false;
  currentInput.focus();

  form.addEventListener("submit", (e) => {
    e.preventDefault();
    if (currentInput.value === "") return rejectInput(currentInput, "Type your current password.");
    const problem = newPasswordProblem(passwordInput, confirmInput);
    if (problem) return rejectInput(...problem);
    if (passwordInput.value === currentInput.value) {
      return rejectInput(passwordInput, "Choose a new password that's different from the current one.");
    }
    submitForm(form, async () => {
      const result = await changePassword(currentInput.value, passwordInput.value);
      Session.save(result, Boolean(localStorage.getItem("session")));
      showToast("Your password is changed.", "success");
      setTimeout(() => (window.location.href = "dashboard.html"), 1100);
    });
  });
}

if (resetToken) setupReset();
else if (params.has("change") && session) setupChange();
else if (params.has("change")) window.location.replace("index.html");
else setupForgot();
