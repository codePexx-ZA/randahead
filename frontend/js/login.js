// Login page: validates and signs the user in
const form = document.getElementById("loginForm");
const emailInput = document.getElementById("emailInput");
const passwordInput = document.getElementById("passwordInput");
const rememberInput = document.getElementById("rememberInput");
const loginBtn = document.getElementById("loginBtn");
const wrapper = document.querySelector(".wrapper");

const ROLE_NAMES = {
  submitter: "Submitter",
  approver: "Approver",
  decision_maker: "Decision Maker",
};

const brandLogo = document.getElementById("brandLogo");
const hideLogo = () => brandLogo.classList.add("missing");
brandLogo.addEventListener("error", hideLogo);
if (brandLogo.complete && brandLogo.naturalWidth === 0) hideLogo();

document.querySelectorAll(".input-box input").forEach((input) => {
  const update = () => {
    input.parentElement.classList.toggle("filled", input.value !== "");
    input.parentElement.classList.remove("invalid");
  };
  input.addEventListener("input", update);
  update();
});

document.querySelector(".toggle-password").addEventListener("click", (e) => {
  const btn = e.currentTarget;
  const show = passwordInput.type === "password";
  passwordInput.type = show ? "text" : "password";
  btn.querySelector("i").className = show ? "fas fa-eye-slash" : "fas fa-eye";
  btn.setAttribute("aria-label", show ? "Hide password" : "Show password");
});

function shakeCard() {
  wrapper.classList.remove("shake");
  void wrapper.offsetWidth;
  wrapper.classList.add("shake");
}

function setLoading(loading) {
  loginBtn.disabled = loading;
  loginBtn.classList.toggle("loading", loading);
}

function rejectInput(input, message) {
  input.parentElement.classList.add("invalid");
  showToast(message, "error");
  shakeCard();
}

form.addEventListener("submit", async (e) => {
  e.preventDefault();

  const email = emailInput.value.trim();
  const password = passwordInput.value;

  if (!emailInput.checkValidity() || email === "") return rejectInput(emailInput, "Enter a valid email address.");
  if (password === "") return rejectInput(passwordInput, "Enter your password.");

  setLoading(true);
  try {
    const result = await Api.login(email, password);
    Session.save(result, rememberInput.checked);
    showToast(`Welcome, ${result.user.fullName} (${result.user.isAdmin ? "Admin" : ROLE_NAMES[result.user.role]})`, "success");
    const next = result.user.mustChangePassword ? "password.html?change" : "dashboard.html";
    setTimeout(() => (window.location.href = next), 900);
  } catch (err) {
    showToast(err.message, "error");
    shakeCard();
    setLoading(false);
  }
});
