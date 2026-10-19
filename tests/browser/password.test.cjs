const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const { page, settle, session, source } = require('./support/page.cjs');
const replacement = 'new test passphrase 456';
function fill(app, confirmation = replacement) {
  app.element('current-password').value = 'old test passphrase 123';
  app.element('new-password').value = replacement;
  app.element('confirm-password').value = confirmation;
}
const submit = app => app.element('password-form').listeners.submit({ preventDefault() {} });
const assertCleared = app => {
  for (const id of ['current-password', 'new-password', 'confirm-password']) assert.equal(app.element(id).value, '');
};

test('a confirmation mismatch makes no request and clears sensitive fields', async () => {
  let requests = 0;
  const app = page(undefined, { changePassword: async () => { requests++; } });
  await settle();
  fill(app, 'different confirmation');
  await submit(app);
  assert.equal(requests, 0);
  assert.equal(vm.runInContext('token', app.context), 'access-a');
  assert.match(app.element('status').textContent, /do not match/);
  assertCleared(app);
});

test('incorrect current-password confirmation does not log out a valid session', async () => {
  const app = page(undefined, { changePassword: async () => { throw Object.assign(new Error('Current password is incorrect.'), { status: 401 }); } });
  await settle();
  fill(app);
  await submit(app);
  assert.equal(vm.runInContext('token', app.context), 'access-a');
  assert.equal(app.element('signed-in').hidden, false);
  assert.equal(app.element('password-fields').disabled, false);
  assertCleared(app);
});

test('successful password change clears the session and asks for a fresh login', async () => {
  const app = page();
  await settle();
  app.element('message-text').value = 'An unsent draft';
  fill(app);
  await submit(app);
  assert.equal(vm.runInContext('token', app.context), null);
  assert.equal(app.connections[0].state, 'Disconnected');
  assert.equal(app.element('signed-out').hidden, false);
  assert.match(app.element('status').textContent, /Password changed/);
  assert.equal(app.element('message-text').value, 'An unsent draft');
  assertCleared(app);
});

test('a stale successful password response cannot log out a newly restored account', async () => {
  let finish;
  let refreshes = 0;
  const app = page(async () => session(++refreshes === 1 ? 'alice' : 'bob', refreshes === 1 ? 'alice-access' : 'bob-access'),
    { changePassword: () => new Promise(resolve => { finish = resolve; }) });
  await settle();
  fill(app);
  const changing = submit(app);
  await settle();
  await app.change('changed');
  finish();
  await changing;
  assert.equal(vm.runInContext('token', app.context), 'bob-access');
  assert.equal(app.element('current-user').textContent, 'bob');
  assertCleared(app);
});

test('a late failed renewal from the revoked connection cannot overwrite password-change success', async () => {
  let failRefresh;
  let refreshes = 0;
  let closed;
  const app = page(async () => ++refreshes === 1 ? session() : new Promise((_, reject) => { failRefresh = reject; }), {
    changePassword: async () => { closed = app.connections[0].closed(); await settle(); }
  });
  await settle();
  fill(app);
  await submit(app);
  failRefresh(Object.assign(new Error('Session expired.'), { status: 401 }));
  await closed;
  assert.match(app.element('status').textContent, /Password changed/);
});

test('password cookie mutation uses the shared lock and explicit Bearer, broadcasting only after success', async () => {
  const broadcasts = [];
  let allowed = true;
  let locked = false;
  const context = vm.createContext({
    window: { isSecureContext: true },
    navigator: { locks: { request: async (name, action) => {
      assert.equal(name, 'messanger-session'); locked = true;
      try { return await action(); } finally { locked = false; }
    } } },
    BroadcastChannel: class { postMessage(value) { broadcasts.push(value); } },
    fetch: async (url, options) => {
      assert.equal(locked, true);
      assert.equal(url, '/api/users/me/password');
      assert.equal(options.credentials, 'same-origin');
      assert.equal(options.headers.Authorization, 'Bearer access-token');
      assert.deepEqual(JSON.parse(options.body), { currentPassword: 'old test passphrase', newPassword: replacement });
      return { ok: allowed, status: allowed ? 204 : 401, json: async () => ({ error: { message: 'Incorrect password.' } }) };
    }
  });
  vm.runInContext(source('session.js'), context);
  const client = vm.runInContext('browserSessions', context);
  const passwords = { currentPassword: 'old test passphrase', newPassword: replacement };
  await client.changePassword('access-token', passwords);
  assert.deepEqual(broadcasts, ['logout']);
  allowed = false;
  await assert.rejects(client.changePassword('access-token', passwords), error => error.status === 401);
  assert.deepEqual(broadcasts, ['logout']);
});
