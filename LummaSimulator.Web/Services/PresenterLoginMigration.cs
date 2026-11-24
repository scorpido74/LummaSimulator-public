using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services;

public sealed partial class PresenterService
{
    // Schema marker 5 is unrelated to the future Prize Experience development phase.
    private static async Task MigrateDiscoverableLoginAsync(SqliteConnection c, SqliteTransaction tx, CancellationToken ct)
    {
        if (Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM ExperienceMigrations WHERE Version=5",ct)) != 0) return;

        // Never disable foreign keys or silently discard extensions to the known schema.
        // No current table references ceremonies. Refuse an unexpected dependent table/index/trigger.
        if ((string?)await Scalar(c,tx,"""
            SELECT group_concat(name,'|') FROM (SELECT name FROM pragma_table_info('PresenterCeremonies') ORDER BY cid)
            """,ct) != "BrowserHash|AccountId|Kind|OperationId|Options|Expires|Binding"
            || Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM pragma_foreign_key_list('PresenterCeremonies')",ct)) != 1
            || Convert.ToInt64(await Scalar(c,tx,"""
                SELECT COUNT(*) FROM pragma_foreign_key_list('PresenterCeremonies')
                WHERE "from"='AccountId' AND "table"='PresenterAccounts' AND "to"='Id'
                  AND on_update='NO ACTION' AND on_delete='NO ACTION'
                """,ct)) != 1)
            throw new InvalidOperationException("Unexpected presenter ceremony columns or foreign key; migration requires review.");
        if (Convert.ToInt64(await Scalar(c,tx,"""
            SELECT COUNT(*) FROM sqlite_schema s JOIN pragma_foreign_key_list(s.name) f
            WHERE s.type='table' AND f."table"='PresenterCeremonies'
            """,ct)) != 0 || Convert.ToInt64(await Scalar(c,tx,"""
            SELECT COUNT(*) FROM sqlite_schema WHERE tbl_name='PresenterCeremonies'
              AND (type='trigger' OR (type='index' AND sql IS NOT NULL))
            """,ct)) != 0)
            throw new InvalidOperationException("Unexpected presenter ceremony schema; migration requires review.");
        using (var check=Cmd(c,tx,"PRAGMA foreign_key_check"))
        using (var rows=await check.ExecuteReaderAsync(ct))
            if (await rows.ReadAsync(ct)) throw new InvalidOperationException("Foreign key violation before presenter login migration.");

        await Exec(c,tx,"""
            CREATE TABLE PresenterCeremonies_Discoverable (
              BrowserHash TEXT PRIMARY KEY, AccountId TEXT REFERENCES PresenterAccounts(Id), Kind TEXT NOT NULL,
              OperationId TEXT NOT NULL, Options TEXT NOT NULL, Expires INTEGER NOT NULL, Binding TEXT NOT NULL,
              CHECK(AccountId IS NOT NULL OR Kind='login'));
            INSERT INTO PresenterCeremonies_Discoverable(BrowserHash,AccountId,Kind,OperationId,Options,Expires,Binding)
              SELECT BrowserHash,AccountId,Kind,OperationId,Options,Expires,Binding FROM PresenterCeremonies;
            DROP TABLE PresenterCeremonies;
            ALTER TABLE PresenterCeremonies_Discoverable RENAME TO PresenterCeremonies;
            """,ct);
        using (var check=Cmd(c,tx,"PRAGMA foreign_key_check"))
        using (var rows=await check.ExecuteReaderAsync(ct))
            if (await rows.ReadAsync(ct)) throw new InvalidOperationException("Foreign key violation after presenter login migration.");
        await Exec(c,tx,"INSERT INTO ExperienceMigrations(Version) VALUES(5)",ct);
        // The caller commits all DDL, copied active ceremonies and the marker together.
    }
}
