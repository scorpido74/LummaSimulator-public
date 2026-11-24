'use strict';
// Test the actual SQLite predicates in memory; not a replacement for C# service tests.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
test('activated fixture must set Pending=0; source predicates preserve owner/member event isolation', t => {
  let DatabaseSync;
  try { ({ DatabaseSync } = require('node:sqlite')); }
  catch { t.skip('Node SQLite unavailable; run the authoritative C# service checks'); return; }
  const db = new DatabaseSync(':memory:');
  try {
    db.exec('PRAGMA foreign_keys=ON');
    const legacy = read('LummaSimulator.Web/Services/Prizes/PrizeMigration.cs');
    for (const name of ['PresenterAccounts', 'PresenterAuthSessions', 'PresenterKeys']) {
      const sql = legacy.match(new RegExp('\\["table:' + name + '"\\]\\s*=\\s*("(?:\\\\.|[^"\\\\])*")'));
      assert.ok(sql);db.exec(JSON.parse(sql[1]));
    }
    const access = read('LummaSimulator.Web/Services/EventAccess.cs');
    const schema = read('LummaSimulator.Web/Services/EventAccessMigration.cs').split('internal const string SchemaSql="""')[1].split('"""')[0];db.exec(schema);
    const fixture = read('tests/LummaSimulator.Web.Tests/PrizeEventChecks.cs');
    const insert = 'INSERT INTO PresenterAccounts' + fixture.split('INSERT INTO PresenterAccounts')[1].split('"""')[0];
    function actor(id, role, old = false) {
      const sql = old ? insert.replace('Active,Pending,Activated', 'Active,Activated').replace('$handle,1,1,0,1', '$handle,1,1,1') : insert;
      // Parameters stay synthetic; no keys/cookies/credentials from any running installation.
      const params = { $id: id, $role: role, $handle: Buffer.from(id), $hash: id + '-session', $expires: 9999999999999, $key: id + '-key', $ref: id + '-ref' };
      for (const statement of sql.split(';').filter(x => x.trim())) {
        const bindings = Object.fromEntries(Object.entries(params).filter(([key]) => statement.includes(key)));
        db.prepare(statement).run(bindings);
      }
    }
    actor('admin', 'Administrator');actor('old-target', 'Presenter', true);actor('target', 'Presenter');actor('outsider', 'Presenter');
    const eligibility = access.split('public async Task InviteAsync')[1].match(/"(SELECT COUNT\(\*\) FROM PresenterAccounts WHERE Id=\$id AND Active=1 AND Pending=0)"/)[1];
    const eligible = id => Object.values(db.prepare(eligibility).get({ $id: id }))[0];
    assert.deepEqual({ ...db.prepare('SELECT Active,Pending,Activated FROM PresenterAccounts WHERE Id=?').get('old-target') }, { Active: 1, Pending: 1, Activated: 1 });
    assert.equal(eligible('old-target'), 0, 'Original fixture target is denied for Pending=1');
    assert.equal(eligible('target'), 1, 'Corrected completed-enrollment fixture is eligible');
    const a = '00000000-0000-0000-0000-000000000101';const b = '00000000-0000-0000-0000-000000000102';
    db.prepare("INSERT INTO PresenterEventScopes VALUES('prize',?,'Synthetic A','admin'),('prize',?,'Synthetic B','outsider')").run(a,b);
    const allowedSql = access.match(/"""\s*(SELECT COUNT\(\*\) FROM PresenterEventScopes e JOIN[\s\S]*?)"""/)[1];
    const allowed = (actor,event,manage) => Object.values(db.prepare(allowedSql).get({ $actor:actor,$kind:'prize',$id:event,$manage:manage?1:0 }))[0] === 1;
    assert.equal(allowed('admin',a,true),true,'Primary owner can manage its own scope');
    assert.equal(allowed('target',a,false),false,'Unrelated target has no event access');
    db.prepare("INSERT INTO PresenterEventInvites VALUES('synthetic-invite','prize',?,'target','admin',9999999999999,NULL)").run(a);
    assert.equal(allowed('target',a,false),false,'Pending invitation alone grants no access');
    db.prepare("INSERT INTO PresenterEventMembers VALUES('prize',?,'target')").run(a);
    assert.equal(allowed('target',a,false),true,'Accepted membership permits presenting');
    assert.equal(allowed('target',a,true),false,'Membership does not permit inviting');
    assert.equal(allowed('target',b,false),false,'Event A membership gives no access to B');
    assert.equal(allowed('outsider',a,true),false,'Ownership of B gives no management of A');
    assert.deepEqual(db.prepare('PRAGMA foreign_key_check').all(),[]);
    assert.equal(db.prepare('PRAGMA integrity_check').get().integrity_check,'ok');
  } finally { db.close(); }
});

test('anonymous visitor preflight and bind share the exact active owner rule',()=>{
 const {DatabaseSync}=require('node:sqlite');
 const source=read('LummaSimulator.Web/Services/EventAccessDemos.cs');
 const sql=source.match(/VisitorOwnerSql="([^"]+)"/)[1];
 const db=new DatabaseSync(':memory:');try{
  db.exec("CREATE TABLE PresenterAccounts(Id TEXT PRIMARY KEY,Active INTEGER);CREATE TABLE PresenterEventScopes(Kind TEXT,SourceId TEXT,OwnerId TEXT);INSERT INTO PresenterAccounts VALUES('presenter',1);INSERT INTO PresenterEventScopes VALUES('prize','synthetic',NULL)");
  const eligible=()=>Object.values(db.prepare(sql).get({$kind:'prize',$id:'synthetic'}))[0]===1;
  assert.equal(eligible(),false,'Management may exist without an owner; anonymous bind must fail');
  db.exec("UPDATE PresenterEventScopes SET OwnerId='presenter'");assert.equal(eligible(),true);
  db.exec('UPDATE PresenterAccounts SET Active=0');assert.equal(eligible(),false);
  assert.match(source,/Scalar\(c,tx,VisitorOwnerSql,ct/,'Bind still enforces the same condition transactionally');
 }finally{db.close();}
});
