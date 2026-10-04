"use strict";

const element = id => document.getElementById(id);
let token = null;
let connection = null;
let activeChatId = null;
let generation = 0;
let expiryTimer = null;
let refreshTimer = null;
let oldestCursor = null;
let loadingOlder = false;
const selectedPeople = new Map();
const messages = new Map();

function status(message) { element("status").textContent = message; }
function report(error) { status(error.message || "The request failed."); }
function connected() { return connection?.state === signalR.HubConnectionState.Connected; }
function updateSendButton() { element("send-button").disabled = !connected() || activeChatId === null; }

async function api(path, body) {
  const headers = {};
  if (token) headers.Authorization = "Bearer " + token;
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const response = await fetch(path, { method: body === undefined ? "GET" : "POST", headers,
    body: body === undefined ? undefined : JSON.stringify(body), cache: "no-store", credentials: "omit" });
  const result = await response.json();
  if (!response.ok) {
    if (response.status === 401 && token) await logout("Your session expired. Please log in again.");
    throw new Error(result.error?.message || "The request failed.");
  }
  return result;
}

async function logout(message = "Logged out.") {
  token = null;
  generation++;
  clearTimeout(expiryTimer);
  clearTimeout(refreshTimer);
  const previous = connection;
  connection = null;
  activeChatId = null;
  messages.clear();
  selectedPeople.clear();
  element("search-results").replaceChildren();
  element("chats").replaceChildren();
  element("messages").replaceChildren();
  element("conversation").hidden = true;
  element("signed-in").hidden = true;
  element("signed-out").hidden = false;
  element("password").value = "";
  element("message-text").value = "";
  showSelectedPeople();
  updateSendButton();
  status(message);
  if (previous) await previous.stop();
}

async function startConnection() {
  const current = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/chat", { accessTokenFactory: () => token || "", withCredentials: false })
    .withAutomaticReconnect([0, 2000, 5000, 10000]).build();
  connection = current;
  current.on("ReceiveMessage", message => {
    if (connection === current && String(message.chatId) === String(activeChatId)) addMessages([message]);
  });
  const refreshSoon = () => {
    if (connection !== current || !token) return;
    clearTimeout(refreshTimer);
    refreshTimer = setTimeout(() => refreshChats().catch(report), 100);
  };
  current.on("ChatCreated", refreshSoon);
  current.on("ChatUpdated", refreshSoon);
  current.onreconnecting(() => { status("Disconnected. Reconnecting…"); updateSendButton(); });
  current.onreconnected(async () => {
    if (connection !== current) return;
    try {
      await refreshChats();
      if (activeChatId !== null) await openChat(activeChatId);
      status("Reconnected. History reloaded.");
    } catch (error) { report(error); }
    updateSendButton();
  });
  current.onclose(() => {
    if (connection === current && token) status("Connection closed. Log out and log in to reconnect.");
    updateSendButton();
  });
  await current.start();
  updateSendButton();
}

element("account-form").addEventListener("submit", async event => {
  event.preventDefault();
  element("account-fields").disabled = true;
  try {
    const credentials = { username: element("username").value, password: element("password").value };
    if (event.submitter?.value === "register") await api("/registration", credentials);
    const login = await api("/auth/login", credentials);
    token = login.accessToken;
    element("password").value = "";
    element("current-user").textContent = login.user.username;
    element("signed-out").hidden = true;
    element("signed-in").hidden = false;
    expiryTimer = setTimeout(() => logout("Your session expired. Please log in again.").catch(report),
      Math.max(0, Date.parse(login.expiresAt) - Date.now()));
    await startConnection();
    await refreshChats();
    status("Connected. Select a chat or create a group.");
  } catch (error) {
    await logout(error.message || "Login failed.");
  } finally { element("account-fields").disabled = false; }
});
element("logout").addEventListener("click", () => logout().catch(report));

function showSelectedPeople() {
  element("selected-people").textContent = selectedPeople.size
    ? "Selected: " + [...selectedPeople.values()].map(user => user.username).join(", ")
    : "No people selected.";
}
element("search-form").addEventListener("submit", async event => {
  event.preventDefault();
  try {
    const users = await api("/api/users/search?query=" + encodeURIComponent(element("search-query").value));
    element("search-results").replaceChildren();
    for (const user of users) {
      const item = document.createElement("li");
      const label = document.createElement("label");
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.checked = selectedPeople.has(String(user.id));
      checkbox.addEventListener("change", () => {
        if (checkbox.checked) selectedPeople.set(String(user.id), user);
        else selectedPeople.delete(String(user.id));
        showSelectedPeople();
      });
      label.append(checkbox, document.createTextNode(" " + user.username + " (" + user.displayName + ")"));
      item.append(label);
      element("search-results").append(item);
    }
    status(users.length ? "Select people for the new group." : "No matching people found.");
  } catch (error) { report(error); }
});
element("create-form").addEventListener("submit", async event => {
  event.preventDefault();
  if (!selectedPeople.size) { status("Select at least one person first."); return; }
  element("create-button").disabled = true;
  try {
    const chat = await api("/api/chats/create", { chatName: element("chat-name").value,
      description: element("chat-description").value || null,
      userIds: [...selectedPeople.values()].map(user => user.id) });
    selectedPeople.clear();
    showSelectedPeople();
    element("search-results").replaceChildren();
    element("create-form").reset();
    await refreshChats();
    await openChat(chat.id);
  } catch (error) { report(error); }
  finally { element("create-button").disabled = false; }
});

async function refreshChats() {
  const session = token;
  const chats = await api("/api/chats");
  if (!session || token !== session) return;
  element("chats").replaceChildren();
  for (const chat of chats) {
    const item = document.createElement("li");
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = chat.name + (chat.lastMessage ? " — " + chat.lastMessage.senderUsername + ": " + chat.lastMessage.text.slice(0, 60) : "");
    button.addEventListener("click", () => openChat(chat.id).catch(report));
    item.append(button);
    element("chats").append(item);
  }
  if (!chats.length) element("chats").textContent = "No chats yet.";
}
element("refresh").addEventListener("click", () => refreshChats().catch(report));

async function openChat(chatId) {
  const previous = activeChatId;
  const requestGeneration = ++generation;
  activeChatId = chatId;
  messages.clear();
  oldestCursor = null;
  loadingOlder = false;
  element("messages").replaceChildren();
  element("conversation").hidden = true;
  updateSendButton();
  if (!connected()) throw new Error("Wait for the chat connection to reconnect.");
  if (previous !== null && String(previous) !== String(chatId)) await connection.invoke("LeaveChat", previous);
  try {
    await connection.invoke("JoinChat", chatId);
    const details = await api("/api/chats/" + chatId);
    if (requestGeneration !== generation) return;
    element("conversation-name").textContent = details.chat.name;
    element("members").textContent = "Members: " + details.chat.participants.map(user => user.username).join(", ");
    oldestCursor = details.messages[0]?.id ?? null;
    element("older").disabled = details.messages.length < 50;
    addMessages(details.messages);
    element("conversation").hidden = false;
    updateSendButton();
    status("Chat opened.");
  } catch (error) {
    if (requestGeneration === generation) { activeChatId = null; updateSendButton(); }
    throw error;
  }
}

function addMessages(items) {
  for (const message of items) messages.set(String(message.id), message);
  const ordered = [...messages.values()].sort((left, right) => left.id - right.id);
  const fragment = document.createDocumentFragment();
  for (const message of ordered) {
    const item = document.createElement("li");
    const heading = document.createElement("span");
    heading.textContent = message.senderUsername + " · " + new Date(message.timestamp).toLocaleString();
    const text = document.createElement("pre");
    text.textContent = message.text;
    item.append(heading, text);
    fragment.append(item);
  }
  element("messages").replaceChildren(fragment);
}
element("older").addEventListener("click", async () => {
  if (loadingOlder || oldestCursor === null || activeChatId === null) return;
  const requestGeneration = generation;
  const chatId = activeChatId;
  loadingOlder = true;
  element("older").disabled = true;
  try {
    const page = await api("/api/chats/" + chatId + "/messages?beforeMessageId=" + oldestCursor);
    if (requestGeneration !== generation) return;
    if (page.length) oldestCursor = page[0].id;
    addMessages(page);
    element("older").disabled = page.length < 50;
  } catch (error) {
    if (requestGeneration === generation) { element("older").disabled = false; report(error); }
  } finally { if (requestGeneration === generation) loadingOlder = false; }
});
element("message-form").addEventListener("submit", async event => {
  event.preventDefault();
  if (!connected() || activeChatId === null) return;
  const chatId = activeChatId;
  const text = element("message-text").value;
  element("send-button").disabled = true;
  try {
    const sent = await connection.invoke("SendMessage", chatId, text);
    if (String(activeChatId) === String(chatId)) addMessages([sent]);
    if (element("message-text").value === text) element("message-text").value = "";
    status("Message sent.");
  } catch (error) { report(error); }
  finally { updateSendButton(); }
});
