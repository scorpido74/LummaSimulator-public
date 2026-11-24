const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const script = fs.readFileSync(path.join(root, 'LummaSimulator.Web/wwwroot/js/monitor-keys.js'), 'utf8');
function fixture() {
  const handlers = {}; const value = { textContent: 'synthetic-issued-value' }; const panel = { hidden: false };
  vm.runInNewContext(script, { document: { getElementById: id => id === 'monitor-issued-key' ? value : panel }, window: { addEventListener: (name, fn) => handlers[name] = fn } });
  return { handlers, value, panel };
}
test('one-time issue remains visible only on original response', () => {
  const f = fixture(); f.handlers.pageshow({ persisted: false }); assert.equal(f.panel.hidden, false);
  f.handlers.pagehide(); assert.equal(f.value.textContent, ''); assert.equal(f.panel.hidden, true);
});
test('browser history cache cannot redisplay issued key', () => {
  const f = fixture(); f.handlers.pageshow({ persisted: true }); assert.equal(f.value.textContent, ''); assert.equal(f.panel.hidden, true);
});
test('issue script has no storage network or clipboard transfer', () => {
  assert.doesNotMatch(script, /localStorage|sessionStorage|clipboard|fetch\(|XMLHttpRequest|location\s*=/);
});
