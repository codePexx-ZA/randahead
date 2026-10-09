// Register page: signs up a new business with its first Decision Maker, then signs them in
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
const wrapper = document.querySelector(".wrapper");

const monthName = (month) => new Date(2000, month - 1, 1).toLocaleString("en-ZA", { month: "long" });

const brandLogo = document.getElementById("brandLogo");
const hideLogo = () => brandLogo.classList.add("missing");
brandLogo.addEventListener("error", hideLogo);
if (brandLogo.complete && brandLogo.naturalWidth === 0) hideLogo();

industryInput.innerHTML = ['<option value="">Not set</option>', ...INDUSTRIES.map((i) => `<option>${i}</option>`)].join("");
yearEndInput.innerHTML = Array.from({ length: 12 }, (_, i) => i + 1)
  .map((m) => `<option value="${m}"${m === 2 ? " selected" : ""}>${monthName(m)}</option>`)
  .join("");

function showYearEndHint() {
  const end = Number(yearEndInput.value);
  yearEndHint.textContent = `Your financial year runs from 1 ${monthName((end % 12) + 1)} to the end of ${monthName(end)}. ` +
    "It can't change once income statement history is approved.";
}
yearEndInput.addEventListener("change", showYearEndHint);
showYearEndHint();

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
  passwordInput.type = confirmInput.type = show ? "text" : "password";
  btn.querySelector("i").className = show ? "fas fa-eye-slash" : "fas fa-eye";
  btn.setAttribute("aria-label", show ? "Hide password" : "Show password");
});

function shakeCard() {
  wrapper.classList.remove("shake");
  void wrapper.offsetWidth;
  wrapper.classList.add("shake");
}

function setLoading(loading) {
  registerBtn.disabled = loading;
  registerBtn.classList.toggle("loading", loading);
}

function rejectInput(input, message) {
  input.parentElement.classList.add("invalid");
  input.focus();
  showToast(message, "error");
  shakeCard();
}

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

  setLoading(true);
  try {
    const result = await Api.register(details);
    Session.save(result, false);
    showToast(`Welcome, ${result.user.fullName}. ${details.businessName} is registered.`, "success");
    setTimeout(() => (window.location.href = "dashboard.html"), 1100);
  } catch (err) {
    showToast(err.message, "error");
    shakeCard();
    setLoading(false);
  }
});
