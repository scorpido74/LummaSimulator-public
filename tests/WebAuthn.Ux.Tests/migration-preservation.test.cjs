'use strict';
// Source-SQL reproduction in a new in-memory DB. It does not execute the C# services.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('historical fixture preserves every legacy column; only additive phase-6.5 rows break the old snapshot', t => {
  let DatabaseSync;
  try { ({ DatabaseSync } = require('node:sqlite')); }
  catch { t.skip('Node SQLite unavailable; run the authoritative .NET migration tests locally'); return; }
  const prize = read('LummaSimulator.Web/Services/Prizes/PrizeMigration.cs');
  const legacy = prize.split('Dictionary<string,string> Legacy = new()')[1].split('\n    };')[0];
  const statements = [...legacy.matchAll(/\["table:([^"]+)"\]\s*=\s*("(?:\\.|[^"\\])*")/g)]
    .map(m => ({ name: m[1], sql: JSON.parse(m[2]) }));
  assert.equal(statements.length, 17, 'Explicit version-5 table contract');
  const fixture = read('tests/LummaSimulator.Web.Tests/PrizeMigrationChecks.cs');
  const seed = fixture.split('internal static async Task SeedLegacy')[1].split('"""')[1];
  const extension = read('LummaSimulator.Web/Services/EventAccessMigration.cs');
  const eventDdl = extension.split('internal const string SchemaSql="""')[1].split('"""')[0];
  const eventData = extension.split("INSERT INTO PresenterEventScopes SELECT 'experience'")[1].split('"""')[0];
  const db = new DatabaseSync(':memory:');
  try {
    db.exec('PRAGMA foreign_keys=ON');
    for (const { sql } of statements) db.exec(sql);
    db.exec("CREATE UNIQUE INDEX OneActiveExperienceEvent ON ExperienceEvents((1)) WHERE Status='Active';INSERT INTO ExperienceMigrations VALUES(3),(4),(5);");
    db.exec("INSERT INTO ExperienceEvents(Id,Name,StartedAt,Status) VALUES('synthetic-event','Passkey Experience',1,'Active'),('00000000-0000-0000-0000-000000000004','Open demo',0,'Imported');INSERT INTO EventPrizePolicies(EventId) SELECT Id FROM ExperienceEvents;");
    db.exec(seed);
    db.exec('INSERT INTO PresenterSessions' + fixture.split('INSERT INTO PresenterSessions')[1].split('"""')[0]);
    const before = new Map(statements.filter(s => s.name !== 'ExperienceMigrations').map(({ name }) => [name, db.prepare(`SELECT * FROM "${name}" ORDER BY rowid`).all()]));
    const oldSnapshot = () => {
      const tables = db.prepare("SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'Prize%' AND name NOT LIKE 'Identity%' AND name!='ExperienceMigrations' ORDER BY name").all();
      return tables.flatMap(({ name }) => db.prepare(`SELECT * FROM "${name}" ORDER BY rowid`).all().map(row => ({ name, row })));
    };
    const original = oldSnapshot();
    // Execute the real Prize CREATE statements, then the real Identity DDL/backfill SQL.
    const prizeDdl = prize.match(/string\[\] Statements\s*=\s*\[([\s\S]*?)\r?\n\s*\];/)[1];
    for (const m of prizeDdl.matchAll(/"(?:\\.|[^"\\])*"/g)) db.exec(JSON.parse(m[0]));
    db.exec('INSERT INTO ExperienceMigrations VALUES(6)');
    const identity = read('LummaSimulator.Web/Services/IdentityMigration.cs');
    for (const m of identity.match(/string\[\] Statements\s*=\s*\[([\s\S]*?)\r?\n\s*\];/)[1].matchAll(/"(?:\\.|[^"\\])*"/g)) db.exec(JSON.parse(m[0]));
    db.exec(identity.split('q.CommandText="""')[1].split('"""')[0]);
    const applyEvent = () => {
      db.exec(eventDdl);
      db.exec('INSERT INTO PresenterInstallation(Id,Existing) VALUES(1,1)');
      db.exec("INSERT INTO PresenterEventScopes SELECT 'experience'" + eventData);
      db.exec('DROP INDEX IF EXISTS OneActiveExperienceEvent');
    };
    db.exec('BEGIN IMMEDIATE');applyEvent();db.exec('ROLLBACK');
    assert.deepEqual(oldSnapshot(), original, 'Interrupted DDL/data/index changes roll back');
    db.exec('BEGIN IMMEDIATE');applyEvent();db.exec('COMMIT');
    const after = oldSnapshot();
    assert.notDeepEqual(after, original, 'The old aggregate assertion reproduces the reported failure');
    const additions = after.filter(row => !before.has(row.name));
    assert.deepEqual([...new Set(additions.map(r => r.name))].sort(), ['PresenterDemoContexts', 'PresenterEventMigrations', 'PresenterEventScopes', 'PresenterInstallation']);
    assert.equal(additions.length, 5, 'Two scopes, one context, one installation and one migration marker');
    for (const [name, rows] of before) assert.deepEqual(db.prepare(`SELECT * FROM "${name}" ORDER BY rowid`).all(), rows, `${name}: all pre-existing column values preserved`);
    db.exec(eventDdl); // Idempotent additive schema; no reseeding of installation data.
    assert.deepEqual(oldSnapshot(), after);
    assert.equal(db.prepare('PRAGMA integrity_check').get().integrity_check, 'ok');
    assert.deepEqual(db.prepare('PRAGMA foreign_key_check').all(), []);
  } finally { db.close(); }
});
