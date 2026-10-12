// Login page: validates and signs the user in
import { login } from "./api.js";
import { rejectInput, setLoading, setupAuthForm, shakeCard } from "./auth-form.js";
import { roleLabel } from "./roles.js";
import { Session } from "./session.js";
import { showToast } from "./toast.js";

const form = document.getElementById("loginForm");
const emailInput = document.getElementById("emailInput");
const passwordInput = document.getElementById("passwordInput");
const rememberInput = document.getElementById("rememberInput");
const loginBtn = document.getElementById("loginBtn");

setupAuthForm();

form.addEventListener("submit", async (e) => {
  e.preventDefault();

  const email = emailInput.value.trim();
  const password = passwordInput.value;

  if (!emailInput.checkValidity() || email === "") return rejectInput(emailInput, "Enter a valid email address.", false);
  if (password === "") return rejectInput(passwordInput, "Enter your password.", false);

  setLoading(loginBtn, true);
  try {
    const result = await login(email, password);
    Session.save(result, rememberInput.checked);
    showToast(`Welcome, ${result.user.fullName} (${roleLabel(result.user)})`, "success");
    const next = result.user.mustChangePassword ? "password.html?change" : "dashboard.html";
    setTimeout(() => (window.location.href = next), 900);
  } catch (err) {
    showToast(err.message, "error");
    shakeCard();
    setLoading(loginBtn, false);
  }
});
