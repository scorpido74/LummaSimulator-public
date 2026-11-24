const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'LummaSimulator.Web/wwwroot/js/presenter-navigation.js'), 'utf8');
function harness(wide) {
  const callbacks = {}; const classes = new Set(); let focused = false;
  const navigation = { dataset: {}, open: true, addEventListener: (name, fn) => callbacks[name] = fn };
  const toggle = { hidden: true, attributes: {}, setAttribute(name, value) { this.attributes[name] = value; }, addEventListener: (name, fn) => callbacks[name] = fn };
  const summary = { focus: () => focused = true };
  const desktop = { matches: wide, addEventListener: (_, fn) => callbacks.resize = fn };
  const context = { document: { body: { classList: { remove: key => classes.delete(key), toggle: key => { if(classes.has(key)){classes.delete(key);return false;} classes.add(key);return true;} } }, getElementById: id => ({ 'presenter-navigation': navigation, 'presenter-collapse': toggle, 'presenter-navigation-summary': summary })[id] }, window: { matchMedia: () => desktop } };
  vm.runInNewContext(source, context);
  return { context, callbacks, navigation, toggle, desktop, classes, focused: () => focused };
}
test('desktop collapse keeps navigation open and exposes expansion state', () => {
  const h = harness(true); assert.equal(h.toggle.hidden, false); assert.equal(h.navigation.open, true);
  h.callbacks.click(); assert.ok(h.classes.has('sidebar-compact')); assert.equal(h.toggle.attributes['aria-expanded'], 'false');
  assert.equal(h.toggle.textContent, '>>');
  h.callbacks.click(); assert.equal(h.classes.size, 0); assert.equal(h.toggle.attributes['aria-expanded'], 'true');
  assert.equal(h.toggle.textContent, '<<');
  h.callbacks.click(); h.callbacks.resize();
  assert.equal(h.classes.size, 0); assert.equal(h.toggle.textContent, '<<');
});
test('mobile navigation closes on Escape and returns focus to its native summary', () => {
  const h = harness(false); assert.equal(h.navigation.open, false);
  h.navigation.open = true; h.callbacks.keydown({ key: 'Escape' }); assert.equal(h.navigation.open, false); assert.ok(h.focused());
  h.desktop.matches = true; h.callbacks.resize(); assert.equal(h.navigation.open, true);
});
test('reattachment leaves current navigation state intact and needs no persistence or network', () => {
  const h = harness(true); h.callbacks.click(); vm.runInNewContext(source, h.context); assert.ok(h.classes.has('sidebar-compact'));
  assert.doesNotMatch(source, /localStorage|sessionStorage|fetch\(|XMLHttpRequest|location\s*=/);
});
test('all new shell/configuration labels exist in both resource catalogs', () => {
  const catalogs = ['nl-NL', 'en-GB'].map(language => fs.readFileSync(path.join(root, `LummaSimulator.Web/Resources/Localization/UiText.${language}.resx`), 'utf8'));
  const keys = catalogs.map(text => [...text.matchAll(/<data name="(P65[^"]+)"/g)].map(m => m[1]).sort());
  assert.ok(keys[0].length >= 20); assert.deepEqual(keys[0], keys[1]);
});

test('event schema uses exact SQLite objects and transactions preserve an interrupted creation', t => {
  let DatabaseSync;try { ({ DatabaseSync } = require('node:sqlite')); } catch { t.skip('Optional Node SQLite requires Node 22+; .NET migration suite remains authoritative');return; }
  const source = fs.readFileSync(path.join(root,'LummaSimulator.Web/Services/EventAccessMigration.cs'),'utf8');
  const ddl = source.split('internal const string SchemaSql="""')[1].split('"""')[0];
  const objects = [...ddl.matchAll(/CREATE (TABLE|TRIGGER) IF NOT EXISTS ([A-Za-z0-9_]+).*?;(?=\s*CREATE|\s*$)/gs)];
  assert.equal(objects.length,14);
  const db = new DatabaseSync(':memory:');
  try {
    db.exec('PRAGMA foreign_keys=ON;CREATE TABLE PresenterAccounts(Id TEXT PRIMARY KEY,Active INTEGER);CREATE TABLE Demos(Id TEXT PRIMARY KEY);CREATE TABLE PresenterAuthSessions(Hash TEXT PRIMARY KEY);');
    db.exec('BEGIN IMMEDIATE');db.exec(ddl);db.exec('ROLLBACK');
    assert.equal(db.prepare("SELECT COUNT(*) n FROM sqlite_schema WHERE name='PresenterEventScopes'").get().n,0);
    db.exec(ddl);db.exec(ddl);
    const normalize = sql => sql.replace(/ IF NOT EXISTS/g,'').replace(/\s+/g,'').replace(/;$/,'');
    for(const m of objects){assert.equal(normalize(db.prepare('SELECT sql FROM sqlite_schema WHERE name=?').get(m[2]).sql),normalize(m[0]));}
    db.exec("INSERT INTO PresenterInstallation(Id,Existing) VALUES(1,0);UPDATE PresenterInstallation SET CompletedAt=123 WHERE Id=1;");
    assert.throws(()=>db.exec('DELETE FROM PresenterInstallation'),/cannot be reset/);
    assert.throws(()=>db.exec('UPDATE PresenterInstallation SET CompletedAt=NULL'),/cannot be reset/);
    assert.throws(()=>db.exec('UPDATE PresenterInstallation SET Existing=1'),/cannot be reset/);
    db.exec("INSERT INTO PresenterAccounts VALUES('synthetic-owner',1);INSERT INTO PresenterEventScopes VALUES('experience','synthetic-event','Synthetic','synthetic-owner');");
    assert.throws(()=>db.exec("UPDATE PresenterAccounts SET Active=0 WHERE Id='synthetic-owner'"),/EventOwnerTransferRequired/);
  } finally { db.close(); }
});

test('invitation copying occurs only on explicit click, with no automatic transfer or persistence', async () => {
  const script=fs.readFileSync(path.join(root,'LummaSimulator.Web/wwwroot/js/event-invitations.js'),'utf8');
  let click;const copied=[];const button={dataset:{success:'Copied',failed:'Failed'},addEventListener:(_,f)=>click=f};const value={textContent:'synthetic-once'};const status={textContent:''};
  vm.runInNewContext(script,{document:{getElementById:id=>({'copy-invitation':button,'monitor-issued-key':value,'copy-status':status})[id]},navigator:{clipboard:{writeText:async s=>copied.push(s)}}});
  assert.equal(copied.length,0);await click();assert.deepEqual(copied,['synthetic-once']);assert.equal(status.textContent,'Copied');
  value.textContent='';await click();assert.equal(copied.length,1);assert.doesNotMatch(script,/localStorage|sessionStorage|fetch\(|XMLHttpRequest/);
});

test('enrollment consumes a well-formed fragment locally and removes it from browser history', () => {
  const script=fs.readFileSync(path.join(root,'LummaSimulator.Web/wwwroot/js/invite-enrollment.js'),'utf8');
  for(const candidate of ['A'.repeat(43),'invalid']){
    const field={value:''};const replacements=[];let hide;
    vm.runInNewContext(script,{URLSearchParams,document:{getElementById:()=>field},window:{location:{hash:'#invite='+candidate,pathname:'/Presenter/Enroll',search:''},addEventListener:(_,f)=>hide=f},history:{replaceState:(_,__,url)=>replacements.push(url)}});
    assert.equal(field.value,candidate.length===43?candidate:'');assert.deepEqual(replacements,['/Presenter/Enroll']);hide();assert.equal(field.value,'');
  }
  assert.doesNotMatch(script,/fetch\(|localStorage|sessionStorage|console\./);
});
