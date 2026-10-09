// Users and business section: business details and who can sign in
const ST_ROLES = ["submitter", "approver", "decision_maker"];
const ST_ROLE_DUTIES = {
  submitter: "Uploads income statements and fixes rejected ones.",
  approver: "Approves or rejects uploads and budgets, tunes warning rules and manages users.",
  decision_maker: "Chooses the budget, sets KPI targets and warning rules, manages users and the business details.",
};
const st = {
  business: null,
  users: [],
  editingId: null,
  confirmDelete: null,
  busy: false,
};

function monthName(month) {
  return new Date(2000, month - 1, 1).toLocaleString("en-ZA", { month: "long" });
}

function isSelf(user) {
  if (!state.user) return false;
  return user.id === state.user.id || user.email.toLowerCase() === String(state.user.email).toLowerCase();
}

function bizValues() {
  return {
    name: document.getElementById("bizName").value.trim(),
    industry: document.getElementById("bizIndustry").value.trim(),
    yearEndMonth: Number(document.getElementById("bizYearEnd").value),
  };
}

function bizDirty() {
  const b = st.business;
  const v = bizValues();
  return v.name !== b.name || v.industry !== (b.industry || "") || v.yearEndMonth !== b.yearEndMonth;
}

function updateBizButtons() {
  const dirty = can("editBusiness") && bizDirty();
  document.getElementById("bizSave").hidden = !dirty;
  document.getElementById("bizUndo").hidden = !dirty;
}

function renderBusiness() {
  const b = st.business;
  const editable = can("editBusiness");
  const name = document.getElementById("bizName");
  const industry = document.getElementById("bizIndustry");
  const yearEnd = document.getElementById("bizYearEnd");

  name.value = b.name;
  const industries = b.industry && !INDUSTRIES.includes(b.industry) ? [b.industry, ...INDUSTRIES] : INDUSTRIES;
  industry.innerHTML = '<option value="">Not set</option>' + industries
    .map((i) => `<option value="${escapeHtml(i)}"${i === b.industry ? " selected" : ""}>${escapeHtml(i)}</option>`)
    .join("");
  yearEnd.innerHTML = Array.from({ length: 12 }, (_, i) => i + 1)
    .map((m) => `<option value="${m}"${m === b.yearEndMonth ? " selected" : ""}>${monthName(m)}</option>`)
    .join("");
  [name, industry].forEach((input) => {
    input.disabled = !editable;
    input.classList.remove("invalid");
  });
  yearEnd.disabled = !editable || b.hasApprovedHistory;

  const lastMonth = monthName(b.yearEndMonth);
  const firstMonth = monthName((b.yearEndMonth % 12) + 1);
  const lock = b.hasApprovedHistory
    ? "The year-end is locked because income statement history is already approved: every financial year label and target date depends on it."
    : "Set this before the first upload is approved; it can't change afterwards.";
  document.getElementById("bizYearEndHint").textContent = `Financial years run from ${firstMonth} to ${lastMonth}. ${lock}`;
  document.getElementById("bizUnit").textContent = `${b.unitName || "Whole company"} · ${b.currency === "ZAR" || !b.currency ? "Rand (ZAR)" : b.currency}`;
  updateBizButtons();
}

async function saveBusiness(e) {
  e.preventDefault();
  if (st.busy || !can("editBusiness") || !bizDirty()) return;

  const v = bizValues();
  const name = document.getElementById("bizName");
  if (!v.name) {
    name.classList.add("invalid");
    name.focus();
    showToast("Type the business's registered name.", "error");
    return;
  }

  const btn = document.getElementById("bizSave");
  st.busy = true;
  btn.disabled = true;
  try {
    const saved = await Api.updateBusiness({ name: v.name, industry: v.industry || null, yearEndMonth: v.yearEndMonth });
    st.business = { ...st.business, ...saved };
    state.business = { ...state.business, ...saved };
    document.getElementById("sidebarBusiness").textContent = st.business.name;
    showView(location.hash.slice(1) || homeView());
    renderBusiness();
    showToast("Business details saved.", "success");
  } catch (err) {
    showToast(err.message || "Could not save the business details.", "error");
  } finally {
    st.busy = false;
    btn.disabled = false;
  }
}

function roleOptionsHtml(selected) {
  return ST_ROLES.map((r) => `<option value="${r}"${r === selected ? " selected" : ""}>${ROLE_LABELS[r]}</option>`).join("");
}

function updateUserHint() {
  const role = document.getElementById("userRoleSelect").value;
  const editing = st.users.find((u) => u.id === st.editingId);
  const self = editing && isSelf(editing);
  const own = self ? " You can't change your own role; ask another Approver or Decision Maker." : "";
  const password = self
    ? " Change your own password with the Password button at the top."
    : editing
      ? " Leave the password empty to keep it, or type a temporary one if they forgot theirs; they choose their own when they next sign in."
      : ` Choose a temporary password of at least ${MIN_PASSWORD_LENGTH} characters and give it to them with their email; they choose their own when they first sign in.`;
  document.getElementById("userHint").textContent = `${ROLE_LABELS[role]}: ${ST_ROLE_DUTIES[role]}${own}${password}`;
}

function fillUserForm(id, fullName, email, roleValue, roleLocked, title) {
  st.editingId = id;
  document.getElementById("userFullName").value = fullName;
  document.getElementById("userEmail").value = email;
  const passwordInput = document.getElementById("userPassword");
  passwordInput.value = "";
  passwordInput.closest(".tg-field").hidden = Boolean(id) && isSelf(st.users.find((u) => u.id === id));
  document.getElementById("userPasswordLabel").textContent = id ? "Temporary password (optional)" : "Temporary password";
  const role = document.getElementById("userRoleSelect");
  role.innerHTML = roleOptionsHtml(roleValue);
  role.disabled = roleLocked;
  document.getElementById("userFormTitle").textContent = title;
  document.getElementById("userSaveText").textContent = id ? "Save changes" : "Add user";
  document.getElementById("userCancel").hidden = !id;
  updateUserHint();
}

function resetUserForm() {
  document.querySelectorAll("#userForm .invalid").forEach((el) => el.classList.remove("invalid"));
  fillUserForm(null, "", "", "submitter", false, "Add a user");
}

function startEditUser(id) {
  const u = st.users.find((x) => x.id === id);
  st.confirmDelete = null;
  fillUserForm(id, u.fullName, u.email, u.role, isSelf(u), `Edit user: ${u.fullName}`);
  renderUsers();
  document.getElementById("userForm").scrollIntoView({ behavior: "smooth", block: "center" });
  document.getElementById("userFullName").focus({ preventScroll: true });
}

function userActionsHtml(u) {
  if (!can("manageUsers")) return "";
  const id = escapeHtml(u.id);
  const name = escapeHtml(u.fullName);
  if (st.confirmDelete === u.id && !can("deleteAnyUser")) {
    return `<td class="action-cell"><span class="tg-confirm">Delete ${name}?
      <button type="button" class="btn-reject btn-small" data-action="delete-user" data-id="${id}">Delete</button>
      <button type="button" class="btn-outline btn-small" data-action="keep-user">Keep</button></span></td>`;
  }

  const button = (action, cls, title, label, icon) =>
    `<button type="button" class="icon-btn${cls}" data-action="${action}" data-id="${id}" title="${title}" aria-label="${label} ${name}"><i class="fas ${icon}"></i></button>`;
  if (u.isAdmin && !state.user.isAdmin) {
    return '<td class="action-cell"><span class="icon-btn locked" title="Admin account: only an admin can change it"><i class="fas fa-lock"></i></span></td>';
  }
  const edit = button("edit-user", "", "Edit", "Edit", "fa-pen");
  if (isSelf(u)) {
    return `<td class="action-cell">${edit}<span class="icon-btn locked" title="You can't switch off or delete your own account"><i class="fas fa-lock"></i></span></td>`;
  }

  const toggle = u.active
    ? button("toggle-user", " danger", "Switch off: can't sign in", "Switch off", "fa-user-slash")
    : button("toggle-user", "", "Switch back on", "Switch on", "fa-user-check");
  const del = u.deletable || can("deleteAnyUser")
    ? button("ask-delete-user", " danger", u.deletable ? "Delete" : "Delete (their history stays, marked former user)", "Delete", "fa-trash-can")
    : '<span class="icon-btn locked" title="Has uploads or decisions on record, so it can be switched off but not deleted"><i class="fas fa-lock"></i></span>';
  return `<td class="action-cell">${edit}${toggle}${del}</td>`;
}

function userRowHtml(u) {
  const you = (u.isAdmin ? '<span class="tag">Admin</span>' : "") + (isSelf(u) ? '<span class="tag">You</span>' : "") +
    (u.mustChangePassword ? '<span class="tag" title="Still on the temporary password; they choose their own at the next sign-in">Temporary password</span>' : "");
  const status = u.active ? chip("ok", "Active", "fa-circle-check") : chip("muted", "Switched off", "fa-circle-pause");
  const cls = `tg-row st-user${u.active ? "" : " inactive"}${st.editingId === u.id ? " row-editing" : ""}`;

  return `<tr class="${cls}" data-id="${escapeHtml(u.id)}">
    <td>
      <div class="st-person">
        <span class="st-avatar">${escapeHtml(initials(u.fullName))}</span>
        <div><span class="cat-name">${escapeHtml(u.fullName)}</span>${you}<small>${escapeHtml(u.email)}</small></div>
      </div>
    </td>
    <td>${escapeHtml(ROLE_LABELS[u.role] || u.role)}<small class="st-status-text">${u.active ? "Active" : "Switched off"}</small></td>
    <td>${status}</td>
    ${userActionsHtml(u)}
  </tr>${st.confirmDelete === u.id && can("deleteAnyUser") ? confirmDeleteRowHtml(u) : ""}`;
}

function confirmDeleteRowHtml(u) {
  const name = escapeHtml(u.fullName);
  const history = u.deletable ? "" : " Their past uploads and decisions stay on record, marked “former user”.";
  return `<tr class="st-confirm-row"><td colspan="4">
    <div class="st-confirm">
      <p><i class="fas fa-triangle-exclamation"></i> Delete ${name}? They can't sign in any more.${history}</p>
      <label class="tg-field">
        <span>Your password</span>
        <input type="password" id="deletePassword" autocomplete="current-password" />
      </label>
      <div class="tg-form-buttons">
        <button type="button" class="btn-outline btn-small" data-action="keep-user">Keep</button>
        <button type="button" class="btn-reject btn-small" data-action="delete-user" data-id="${escapeHtml(u.id)}">
          <i class="fas fa-trash-can"></i> Delete
        </button>
      </div>
    </div>
  </td></tr>`;
}

function renderUsers() {
  const table = document.getElementById("userTable");
  const badge = document.getElementById("userBadge");
  const active = st.users.filter((u) => u.active).length;
  const off = st.users.length - active;
  badge.hidden = !st.users.length;
  badge.className = "badge info";
  badge.textContent = off ? `${active} active · ${off} switched off` : `${active} active`;

  if (!st.users.length) {
    table.innerHTML = '<tbody><tr class="row-empty"><td>No users yet.</td></tr></tbody>';
    return;
  }

  const sorted = [...st.users].sort((a, b) =>
    Number(b.active) - Number(a.active) ||
    ST_ROLES.indexOf(a.role) - ST_ROLES.indexOf(b.role) ||
    a.fullName.localeCompare(b.fullName)
  );
  table.innerHTML = `<thead><tr>
      <th>User</th><th>Role</th><th>Status</th>
      ${can("manageUsers") ? '<th class="action-cell"><span class="sr-only">Actions</span></th>' : ""}
    </tr></thead><tbody>${sorted.map(userRowHtml).join("")}</tbody>`;
}

async function reloadUsers(highlightId) {
  st.users = await Api.getUsers();
  renderUsers();
  if (highlightId) document.querySelector(`#userTable tr[data-id="${CSS.escape(highlightId)}"]`)?.classList.add("row-new");
}

async function saveUser(e) {
  e.preventDefault();
  if (st.busy || !can("manageUsers")) return;

  const nameInput = document.getElementById("userFullName");
  const emailInput = document.getElementById("userEmail");
  const fullName = nameInput.value.trim().replace(/\s+/g, " ");
  const email = emailInput.value.trim().toLowerCase();
  const role = document.getElementById("userRoleSelect").value;
  const passwordInput = document.getElementById("userPassword");
  const password = passwordInput.value;

  let problem = null;
  if (!fullName) problem = [nameInput, "Type the user's full name."];
  else if (!EMAIL_PATTERN.test(email)) problem = [emailInput, "Type a valid email address, e.g. name@business.co.za."];
  else if (!st.editingId && !password) problem = [passwordInput, "Type a password for the new user."];
  else if (password && password.length < MIN_PASSWORD_LENGTH) problem = [passwordInput, `The password needs at least ${MIN_PASSWORD_LENGTH} characters.`];
  if (problem) {
    problem[0].classList.add("invalid");
    problem[0].focus();
    showToast(problem[1], "error");
    return;
  }

  const btn = document.getElementById("userSave");
  st.busy = true;
  btn.disabled = true;
  try {
    const editing = st.users.find((u) => u.id === st.editingId);
    let saved;
    if (editing) {
      const self = isSelf(editing);
      saved = await Api.updateUser(editing.id, { fullName, email, role: self ? editing.role : role, active: editing.active, password });
      if (self) {
        const changes = { fullName: saved.fullName, email: saved.email };
        state.user = { ...state.user, ...changes };
        Session.update(changes);
        renderUserChip();
      }
      showToast(`${saved.fullName} updated.`, "success");
    } else {
      saved = await Api.createUser({ fullName, email, role, password });
      showToast(`${saved.fullName} added as ${ROLE_LABELS[saved.role]}. They sign in with ${saved.email} and the temporary password, then choose their own.`, "success");
    }
    resetUserForm();
    await reloadUsers(saved.id);
  } catch (err) {
    emailInput.classList.toggle("invalid", /email|account/i.test(err.message || ""));
    passwordInput.classList.toggle("invalid", /password/i.test(err.message || ""));
    showToast(err.message || "Could not save the user.", "error");
  } finally {
    st.busy = false;
    btn.disabled = false;
  }
}

async function toggleUser(id) {
  const u = st.users.find((x) => x.id === id);
  if (!u || isSelf(u)) return;
  const row = document.querySelector(`#userTable tr[data-id="${CSS.escape(id)}"]`);
  row?.classList.add("busy");
  try {
    await Api.updateUser(id, { fullName: u.fullName, email: u.email, role: u.role, active: !u.active });
    await reloadUsers(id);
    showToast(u.active ? `${u.fullName} switched off: they can't sign in any more.` : `${u.fullName} switched back on.`, "success");
  } catch (err) {
    row?.classList.remove("busy");
    showToast(err.message || "Could not change the user.", "error");
  }
}

async function deleteUser(id, btn) {
  if (st.busy) return;
  const u = st.users.find((x) => x.id === id);
  const passwordInput = can("deleteAnyUser") ? document.getElementById("deletePassword") : null;
  if (passwordInput && !passwordInput.value) {
    passwordInput.classList.add("invalid");
    passwordInput.focus();
    showToast("Type your password to confirm.", "error");
    return;
  }

  st.busy = true;
  btn.disabled = true;
  try {
    await Api.deleteUser(id, passwordInput?.value);
    st.confirmDelete = null;
    if (st.editingId === id) resetUserForm();
    await reloadUsers();
    const history = u && !u.deletable ? " Their past uploads and decisions stay, marked “former user”." : "";
    showToast(`${u?.fullName ?? "User"} deleted.${history}`, "success");
  } catch (err) {
    const wrongPassword = passwordInput && /password/i.test(err.message || "");
    if (wrongPassword) {
      passwordInput.value = "";
      passwordInput.classList.add("invalid");
      passwordInput.focus();
      btn.disabled = false;
    } else {
      st.confirmDelete = null;
      renderUsers();
    }
    showToast(err.message || "Could not delete the user.", "error");
  } finally {
    st.busy = false;
  }
}

async function refreshSettings() {
  try {
    [st.business, st.users] = await Promise.all([Api.getBusiness(), Api.getUsers()]);
    state.business = { ...state.business, ...st.business };
    document.getElementById("sidebarBusiness").textContent = st.business.name;
    renderBusiness();
    if (!st.editingId) resetUserForm();
    renderUsers();
  } catch (err) {
    showToast(err.message || "Could not load the users and business details.", "error");
  }
}

function setupSettings() {
  showRoleNote("bizRoleNote", "editBusiness", "you can see the business details, but only a Decision Maker can change them.");
  showRoleNote("userRoleNote", "manageUsers", "you can see who has access, but only an Approver or Decision Maker can add or change users.");
  document.getElementById("userForm").hidden = !can("manageUsers");
  const bizForm = document.getElementById("bizForm");
  bizForm.addEventListener("submit", saveBusiness);
  bizForm.addEventListener("input", (e) => {
    e.target.classList.remove("invalid");
    if (st.business) updateBizButtons();
  });
  bizForm.addEventListener("change", () => st.business && updateBizButtons());
  document.getElementById("bizUndo").addEventListener("click", renderBusiness);

  const userForm = document.getElementById("userForm");
  userForm.addEventListener("submit", saveUser);
  userForm.addEventListener("input", (e) => e.target.classList.remove("invalid"));
  document.getElementById("userRoleSelect").addEventListener("change", updateUserHint);
  document.getElementById("userCancel").addEventListener("click", () => {
    resetUserForm();
    renderUsers();
  });

  document.getElementById("userTable").addEventListener("click", (e) => {
    const btn = e.target.closest("[data-action]");
    if (!btn || !can("manageUsers")) return;
    const action = btn.dataset.action;
    if (action === "edit-user") return startEditUser(btn.dataset.id);
    if (action === "toggle-user") return toggleUser(btn.dataset.id);
    if (action === "delete-user") return deleteUser(btn.dataset.id, btn);
    if (action === "ask-delete-user") st.confirmDelete = btn.dataset.id;
    if (action === "keep-user") st.confirmDelete = null;
    renderUsers();
    document.getElementById("deletePassword")?.focus();
  });
  document.getElementById("userTable").addEventListener("keydown", (e) => {
    if (e.target.id !== "deletePassword") return;
    e.target.classList.remove("invalid");
    if (e.key === "Enter") document.querySelector('#userTable [data-action="delete-user"]')?.click();
    if (e.key === "Escape") {
      st.confirmDelete = null;
      renderUsers();
    }
  });
}
