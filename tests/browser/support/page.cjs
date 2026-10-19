const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.resolve(__dirname, '../../..');
const source = name => fs.readFileSync(path.join(root, 'Messanger/wwwroot', name), 'utf8');
const settle = async () => { for (let i = 0; i < 40; i++) await Promise.resolve(); };
const session = (user = 'alice', token = 'access-a') => ({
  user: { username: user }, accessToken: token, expiresAt: new Date(Date.now() + 900000).toISOString()
});
const message = (id, chatId = 7) => ({ id, chatId, senderUsername: 'alice', timestamp: new Date().toISOString(), text: 'Message ' + id });

function page(refresh = async () => session(), options = {}) {
  const elements = new Map();
  const timers = new Map();
  const frames = new Map();
  const events = {};
  const calls = [];
  let visible = options.visible !== false;
  let focused = options.focused !== false;
  let nextTimer = 0;
  let changed;
  let logouts = 0;
  const connections = [];
  function node() {
    return {
      value: '', hidden: false, textContent: '', disabled: false, listeners: {}, children: [],
      addEventListener(name, callback) { this.listeners[name] = callback; },
      replaceChildren(...children) { this.children = children; },
      append(...children) { this.children.push(...children); }
    };
  }
  function element(id) {
    if (!elements.has(id)) elements.set(id, node());
    return elements.get(id);
  }
  class Hub {
    constructor() { this.state = 'Disconnected'; connections.push(this); this.handlers = {}; }
    on(name, callback) { this.handlers[name] = callback; }
    onreconnecting(callback) { this.reconnecting = callback; }
    onreconnected(callback) { this.reconnected = callback; }
    onclose(callback) { this.closed = callback; }
    async start() { this.state = 'Connected'; }
    async stop() { this.state = 'Disconnected'; if (this.closed) await this.closed(); }
    async invoke(name, ...args) {
      calls.push([name, ...args]);
      if (options.invoke) return options.invoke(name, ...args);
      if (name === 'MarkRead') return { chatId: args[0], lastReadMessageId: args[1], unreadCount: 0 };
    }
  }
  class Builder {
    withUrl() { return this; } withAutomaticReconnect() { return this; } build() { return new Hub(); }
  }
  const context = vm.createContext({
    document: {
      getElementById: element, createElement: node, createDocumentFragment: node,
      get visibilityState() { return visible ? 'visible' : 'hidden'; }, hasFocus: () => focused,
      addEventListener(name, callback) { events[name] = callback; }
    },
    window: { addEventListener(name, callback) { events[name] = callback; } }, console, Date,
    setTimeout(callback) { timers.set(++nextTimer, callback); return nextTimer; },
    clearTimeout(id) { timers.delete(id); },
    requestAnimationFrame(callback) { frames.set(++nextTimer, callback); return nextTimer; },
    cancelAnimationFrame(id) { frames.delete(id); },
    signalR: { HubConnectionBuilder: Builder, HubConnectionState: { Connected: 'Connected' } },
    browserSessions: {
      refresh: () => refresh(), login: async () => session(), logout: async () => { logouts++; },
      changePassword: (token, passwords) => options.changePassword ? options.changePassword(token, passwords) : Promise.resolve(),
      onChange(callback) { changed = callback; }
    },
    fetch: async url => {
      if (options.fetch) return options.fetch(url);
      const data = url === '/api/chats' ? options.chats || [] : {
        chat: { name: 'Group', participants: [], lastReadMessageId: null },
        messages: [message(1), message(2), message(3)]
      };
      return { ok: true, json: async () => data };
    }
  });
  vm.runInContext(source('app.js'), context);
  return {
    context, element, timers, frames, connections, calls, change: value => changed(value), logouts: () => logouts,
    open: chatId => vm.runInContext('openChat(' + chatId + ')', context),
    visible(value) { visible = value; events.visibilitychange(); },
    focus(value) { focused = value; if (value) events.focus(); },
    async frame() { const first = frames.entries().next().value; if (first) { frames.delete(first[0]); await first[1](); } }
  };
}
module.exports = { page, settle, session, source, message };
