const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const { page, settle, message } = require('./support/page.cjs');
const reads = app => app.calls.filter(call => call[0] === 'MarkRead');

test('opening a focused conversation acknowledges only its latest rendered message', async () => {
  const app = page();
  await settle();
  await app.open(7);
  assert.equal(reads(app).length, 0, 'Wait for a render frame before acknowledging.');
  await app.frame();
  assert.deepEqual(reads(app), [['MarkRead', 7, 3]]);
  await app.frame();
  assert.equal(reads(app).length, 1, 'Repeated render frames do not generate duplicate writes.');
});

test('a hidden conversation is acknowledged only after the tab becomes visible', async () => {
  const app = page(undefined, { visible: false });
  await settle();
  await app.open(7);
  await app.frame();
  assert.equal(reads(app).length, 0);
  app.visible(true);
  await app.frame();
  assert.deepEqual(reads(app), [['MarkRead', 7, 3]]);
});

test('a visible window without focus does not acknowledge messages', async () => {
  const app = page(undefined, { focused: false });
  await settle();
  await app.open(7);
  await app.frame();
  assert.equal(reads(app).length, 0);
  app.focus(true);
  await app.frame();
  assert.equal(reads(app).length, 1);
});

test('a new message arriving during an acknowledgement is acknowledged in a later request', async () => {
  let finish;
  const app = page(undefined, { invoke: (name, chatId, id) => name === 'MarkRead'
    ? id === 3 ? new Promise(resolve => { finish = resolve; }) : { chatId, lastReadMessageId: id, unreadCount: 0 }
    : undefined });
  await settle();
  await app.open(7);
  const pending = app.frame();
  app.connections[0].handlers.ReceiveMessage(message(4));
  await app.frame();
  assert.equal(reads(app).length, 1, 'Only one acknowledgement may be in flight.');
  finish({ chatId: 7, lastReadMessageId: 3, unreadCount: 1 });
  await pending;
  await app.frame();
  assert.deepEqual(reads(app), [['MarkRead', 7, 3], ['MarkRead', 7, 4]]);
});

test('a history response for an abandoned conversation cannot mark its messages read', async () => {
  let finishOld;
  const app = page(undefined, { fetch: async url => {
    if (url === '/api/chats') return { ok: true, json: async () => [] };
    if (url === '/api/chats/7') return new Promise(resolve => { finishOld = resolve; });
    return { ok: true, json: async () => ({ chat: { name: 'Other', participants: [] }, messages: [message(8, 8)] }) };
  } });
  await settle();
  const abandoned = app.open(7);
  await settle();
  await app.open(8);
  finishOld({ ok: true, json: async () => ({ chat: { name: 'Old', participants: [] }, messages: [message(99)] }) });
  await abandoned;
  await app.frame();
  assert.deepEqual(reads(app), [['MarkRead', 8, 8]]);
});

test('the plain chat list displays the unread count from the server', async () => {
  const app = page(undefined, { chats: [{ id: 7, name: 'Group', unreadCount: 2, lastMessage: null }] });
  await settle();
  assert.equal(app.element('chats').children[0].children[0].textContent, 'Group (2 unread)');
});

test('an acknowledgement completing after logout cannot restore private read state', async () => {
  let finish;
  const app = page(undefined, { invoke: name => name === 'MarkRead' ? new Promise(resolve => { finish = resolve; }) : undefined });
  await settle();
  await app.open(7);
  const pending = app.frame();
  await app.change('logout');
  finish({ chatId: 7, lastReadMessageId: 3, unreadCount: 0 });
  await pending;
  assert.equal(vm.runInContext('readMarkers.size', app.context), 0);
  assert.equal(app.frames.size, 0);
});

test('an older chat-list response cannot overwrite a newer unread count', async () => {
  let requests = 0;
  let finishOld;
  const app = page(undefined, { fetch: async () => {
    requests++;
    if (requests === 2) return new Promise(resolve => { finishOld = resolve; });
    return { ok: true, json: async () => [{ id: 7, name: 'Group', unreadCount: 0 }] };
  } });
  await settle();
  const older = vm.runInContext('refreshChats()', app.context);
  await settle();
  await vm.runInContext('refreshChats()', app.context);
  finishOld({ ok: true, json: async () => [{ id: 7, name: 'Group', unreadCount: 9 }] });
  await older;
  assert.equal(app.element('chats').children[0].children[0].textContent, 'Group');
});
