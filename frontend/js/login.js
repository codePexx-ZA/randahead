import { login } from "./api.js";
import { flagInput, setLoading, setupAuthForm, shakeCard } from "./auth-form.js";
import { Session } from "./session.js";
import { showToast } from "./toast.js";

const form = document.getElementById("loginForm");
const usernameInput = document.getElementById("usernameInput");
const passwordInput = document.getElementById("passwordInput");
const rememberInput = document.getElementById("rememberInput");
const loginBtn = document.getElementById("loginBtn");

setupAuthForm();

form.addEventListener("submit", async (e) => {
  e.preventDefault();

  const username = usernameInput.value.trim();
  const password = passwordInput.value;

  if (username === "") return flagInput(usernameInput, "Enter your username.", false);
  if (password === "") return flagInput(passwordInput, "Enter your password.", false);

  setLoading(loginBtn, true);
  try {
    const result = await login(username, password);
    Session.save(result, rememberInput.checked);
    showToast(`Welcome, ${result.user.fullName}`, "success");
    setTimeout(() => (window.location.href = "dashboard.html"), 900);
  } catch (err) {
    showToast(err.message, "error");
    shakeCard();
    setLoading(loginBtn, false);
  }
});
