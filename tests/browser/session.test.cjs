const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const source = name => fs.readFileSync(path.join(root, 'Messanger/wwwroot', name), 'utf8');
const settle = async () => { for (let i = 0; i < 30; i++) await Promise.resolve(); };
const session = (user = 'alice', token = 'access-a') => ({
  user: { username: user }, accessToken: token, expiresAt: new Date(Date.now() + 900000).toISOString()
});

function page(refresh = async () => session()) {
  const elements = new Map();
  const timers = new Map();
  let nextTimer = 0;
  let changed;
  let logouts = 0;
  const connections = [];
  function element(id) {
    if (!elements.has(id)) elements.set(id, {
      value: '', hidden: false, textContent: '', disabled: false, listeners: {},
      addEventListener(name, callback) { this.listeners[name] = callback; }, replaceChildren() {}
    });
    return elements.get(id);
  }
  class Hub {
    constructor() { this.state = 'Disconnected'; connections.push(this); }
    on() {}
    onreconnecting(callback) { this.reconnecting = callback; }
    onreconnected(callback) { this.reconnected = callback; }
    onclose(callback) { this.closed = callback; }
    async start() { this.state = 'Connected'; }
    async stop() { this.state = 'Disconnected'; if (this.closed) await this.closed(); }
  }
  class Builder {
    withUrl() { return this; } withAutomaticReconnect() { return this; } build() { return new Hub(); }
  }
  const context = vm.createContext({
    document: { getElementById: element }, console, Date,
    setTimeout(callback) { timers.set(++nextTimer, callback); return nextTimer; },
    clearTimeout(id) { timers.delete(id); },
    signalR: { HubConnectionBuilder: Builder, HubConnectionState: { Connected: 'Connected' } },
    browserSessions: {
      refresh: () => refresh(), login: async () => session(), logout: async () => { logouts++; },
      onChange(callback) { changed = callback; }
    },
    fetch: async () => ({ ok: true, json: async () => [] })
  });
  vm.runInContext(source('app.js'), context);
  return { context, element, timers, connections, change: value => changed(value), logouts: () => logouts };
}

test('page load restores identity and renews access without recreating the live connection', async () => {
  let requests = 0;
  const app = page(async () => session('alice', 'access-' + ++requests));
  await settle();
  assert.equal(app.element('current-user').textContent, 'alice');
  assert.equal(app.element('signed-in').hidden, false);
  assert.equal(app.connections.length, 1);
  await [...app.timers.values()][0]();
  assert.equal(vm.runInContext('token', app.context), 'access-2');
  assert.equal(app.connections.length, 1);
});

test('revocation clears authentication and keeps unsent text with an explanation', async () => {
  let revoked = false;
  const app = page(async () => {
    if (revoked) throw Object.assign(new Error('Session revoked.'), { status: 401 });
    return session();
  });
  await settle();
  app.element('message-text').value = 'Unsent draft';
  revoked = true;
  await [...app.timers.values()][0]();
  assert.equal(vm.runInContext('token', app.context), null);
  assert.equal(app.element('message-text').value, 'Unsent draft');
  assert.match(app.element('status').textContent, /unsent text/);
  assert.equal(app.element('signed-out').hidden, false);
});

test('logout in another tab clears local state without sending another logout request', async () => {
  const app = page();
  await settle();
  await app.change('logout');
  assert.equal(vm.runInContext('token', app.context), null);
  assert.equal(app.logouts(), 0);
  assert.equal(app.connections[0].state, 'Disconnected');
});

test('a refresh from an old account cannot overwrite a new account restored in another tab', async () => {
  let request = 0;
  let completeOld;
  const app = page(async () => {
    request++;
    if (request === 2) return new Promise(resolve => { completeOld = resolve; });
    return request === 1 ? session() : session('bob', 'bob-access');
  });
  await settle();
  const old = vm.runInContext('renewAccess()', app.context).catch(() => {});
  await settle();
  const changed = app.change('changed');
  await settle();
  completeOld(session('alice', 'stale-access'));
  await Promise.all([old, changed]);
  assert.equal(vm.runInContext('token', app.context), 'bob-access');
  assert.equal(app.element('current-user').textContent, 'bob');
});

test('tabs serialize CSRF bootstrap and cookie rotation through one shared lock', async () => {
  let tail = Promise.resolve();
  let cookie = 0;
  let active = 0;
  let maxActive = 0;
  const locks = { request(_name, action) {
    const next = tail.then(action);
    tail = next.catch(() => {});
    return next;
  } };
  const fetch = async (url, options) => {
    assert.equal(options.credentials, 'same-origin');
    if (url === '/auth/csrf') return { ok: true, json: async () => ({ token: 'proof-' + cookie }) };
    active++;
    maxActive = Math.max(active, maxActive);
    await Promise.resolve();
    assert.equal(options.headers['X-CSRF-Token'], 'proof-' + cookie);
    cookie++;
    active--;
    return { ok: true, status: 200, json: async () => session() };
  };
  const tab = () => {
    const context = vm.createContext({ window: { isSecureContext: true }, navigator: { locks }, fetch });
    vm.runInContext(source('session.js'), context);
    return vm.runInContext('browserSessions', context);
  };
  await Promise.all([tab().refresh(), tab().refresh()]);
  assert.equal(cookie, 2);
  assert.equal(maxActive, 1);
});
