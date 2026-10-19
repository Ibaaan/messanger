"use strict";

// The lock covers cookie reads/writes and CSRF bootstrap across tabs. Tokens never enter browser storage.
const browserSessions = (() => {
  const changes = typeof BroadcastChannel === "function" ? new BroadcastChannel("messanger-session") : null;
  function locked(action) {
    if (!window.isSecureContext || !navigator.locks)
      throw new Error("Persistent sign-in requires HTTPS and a browser with Web Locks support.");
    return navigator.locks.request("messanger-session", action);
  }
  async function result(response) {
    const body = response.status === 204 ? null : await response.json();
    if (!response.ok) {
      const error = new Error(body?.error?.message || "Session request failed.");
      error.status = response.status;
      throw error;
    }
    return body;
  }
  async function request(path, body) {
    return locked(async () => {
      const bootstrap = await fetch("/auth/csrf", { credentials: "same-origin", cache: "no-store" });
      const proof = await bootstrap.json();
      if (!bootstrap.ok) throw new Error(proof.error?.message || "Unable to initialize sign-in.");
      const response = await fetch(path, {
        method: "POST", credentials: "same-origin", cache: "no-store",
        headers: { "X-CSRF-Token": proof.token, ...(body === undefined ? {} : { "Content-Type": "application/json" }) },
        body: body === undefined ? undefined : JSON.stringify(body)
      });
      return result(response);
    });
  }
  return {
    login: async credentials => { const session = await request("/auth/login", credentials); changes?.postMessage("changed"); return session; },
    refresh: () => request("/auth/refresh"),
    logout: async () => { await request("/auth/logout"); changes?.postMessage("logout"); },
    changePassword: async (accessToken, passwords) => {
      await locked(async () => result(await fetch("/api/users/me/password", {
        method: "POST", credentials: "same-origin", cache: "no-store",
        headers: { Authorization: "Bearer " + accessToken, "Content-Type": "application/json" },
        body: JSON.stringify(passwords)
      })));
      changes?.postMessage("logout");
    },
    onChange: callback => { if (changes) changes.onmessage = event => callback(event.data); }
  };
})();
