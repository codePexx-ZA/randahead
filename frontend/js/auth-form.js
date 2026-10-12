// Auth form helpers: floating labels, show-password toggles, loading buttons and errors for the sign-in pages
import { showToast } from "./toast.js";

const wrapper = document.querySelector(".wrapper");

export function setupAuthForm() {
  document.querySelectorAll(".input-box input").forEach((input) => {
    const update = () => {
      input.parentElement.classList.toggle("filled", input.value !== "");
      input.parentElement.classList.remove("invalid");
    };
    input.addEventListener("input", update);
    update();
  });

  document.querySelectorAll(".toggle-password").forEach((btn) =>
    btn.addEventListener("click", () => {
      const inputs = btn.closest("form").querySelectorAll('input[autocomplete$="password"]');
      const show = inputs[0].type === "password";
      inputs.forEach((input) => (input.type = show ? "text" : "password"));
      btn.querySelector("i").className = show ? "fas fa-eye-slash" : "fas fa-eye";
      btn.setAttribute("aria-label", show ? "Hide password" : "Show password");
    })
  );
}

export function shakeCard() {
  wrapper.classList.remove("shake");
  void wrapper.offsetWidth;
  wrapper.classList.add("shake");
}

export function setLoading(button, loading) {
  button.disabled = loading;
  button.classList.toggle("loading", loading);
}

export function rejectInput(input, message, focus = true) {
  input.parentElement.classList.add("invalid");
  if (focus) input.focus();
  showToast(message, "error");
  shakeCard();
}
