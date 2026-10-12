// Session: the signed-in user and token, kept in localStorage ("remember me") or sessionStorage
export const Session = {
  save(data, remember) {
    (remember ? localStorage : sessionStorage).setItem("session", JSON.stringify(data));
  },
  get() {
    const raw = localStorage.getItem("session") || sessionStorage.getItem("session");
    const data = raw ? JSON.parse(raw) : null;
    if (data?.expiresAt && new Date(data.expiresAt) <= new Date()) {
      this.clear();
      return null;
    }
    return data;
  },
  update(changes) {
    const store = localStorage.getItem("session") ? localStorage : sessionStorage;
    const data = this.get();
    if (!data) return;
    data.user = { ...data.user, ...changes };
    store.setItem("session", JSON.stringify(data));
  },
  clear() {
    localStorage.removeItem("session");
    sessionStorage.removeItem("session");
  },
};
