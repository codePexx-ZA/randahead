// Toast messages: shows small pop-up notifications
function showToast(message, type = "info", duration = 3500) {
  const icons = {
    success: "fa-circle-check",
    error: "fa-circle-xmark",
    warning: "fa-triangle-exclamation",
    info: "fa-circle-info",
  };

  const toast = Object.assign(document.createElement("div"), { className: `toast ${type}` });
  toast.append(
    Object.assign(document.createElement("i"), { className: `fas ${icons[type] || icons.info}` }),
    Object.assign(document.createElement("span"), { textContent: message })
  );
  document.getElementById("toastContainer").appendChild(toast);

  setTimeout(() => {
    toast.classList.add("hide");
    toast.addEventListener("animationend", () => toast.remove(), { once: true });
  }, duration);
}
