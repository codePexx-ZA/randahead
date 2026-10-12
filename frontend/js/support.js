// Contact support section: sends the signed-in user's message to the support inbox
import { sendSupportMessage } from "./api.js";
import { on, state } from "./store.js";
import { showToast } from "./toast.js";
import { withBusy } from "./ui.js";

function fillUser() {
  document.getElementById("supportName").value = state.user.fullName;
  document.getElementById("supportEmail").value = state.user.email;
}

export function setup() {
  fillUser();
  on("user", fillUser);

  const form = document.getElementById("supportForm");
  const btn = document.getElementById("supportBtn");

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const fields = ["supportName", "supportEmail", "supportMessage"].map((id) => document.getElementById(id));
    const invalid = fields.filter((f) => !f.checkValidity() || f.value.trim() === "");
    fields.forEach((f) => f.classList.toggle("invalid", invalid.includes(f)));

    if (invalid.length) {
      showToast("Please fill in your name, a valid email and a message.", "error");
      return;
    }

    const [name, email, message] = fields.map((f) => f.value.trim());
    const topic = document.getElementById("supportTopic").value;
    await withBusy(btn, async () => {
      try {
        const result = await sendSupportMessage({ name, email, topic, message });
        document.getElementById("supportMessage").value = "";
        showToast(result.message, "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });
  });
}
