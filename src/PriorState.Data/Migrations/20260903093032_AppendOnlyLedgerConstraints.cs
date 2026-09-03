using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriorState.Data.Migrations
{
    /// <summary>
    /// Makes the ledger append-only in the database rather than in application code.
    ///
    /// This migration is the guarantee. Everything else in PriorState is a convenience on top of
    /// it, and it is written in plain SQL precisely so that someone auditing the archive — an
    /// opposing expert, a court-appointed reviewer — can read this file and see exactly what is
    /// prevented, without reading any C#. A promise enforced only by application code is a promise
    /// the next commit can quietly withdraw.
    ///
    /// Three carve-outs exist, each narrow and each set-once:
    ///   snapshots."TimestampAnchorId"          - filled in when the day's Merkle root is stamped
    ///   capture_profile_versions."SupersededAt" - filled in when a newer profile version appears
    ///   deployment_ledger_entries."RunId"       - filled in when the triggered run completes
    ///
    /// None of these are inputs to the hash chain, so setting one cannot change any recorded
    /// hash. Every other column, on every ledger table, is immutable from the moment of insert.
    /// TRUNCATE is blocked separately, because row-level triggers do not fire for it.
    /// </summary>
    public partial class AppendOnlyLedgerConstraints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Rejects every UPDATE and DELETE outright. ---
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION priorstate_reject_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION
                        'priorstate: % is an append-only ledger table; % is not permitted',
                        TG_TABLE_NAME, TG_OP
                        USING ERRCODE = 'restrict_violation',
                              HINT = 'Recorded history cannot be altered. This is by design.';
                END;
                $$;
                """);

            // --- Allows exactly one column to go from NULL to a value, nothing else. ---
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION priorstate_set_once_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    settable_column text := TG_ARGV[0];
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION
                            'priorstate: % is an append-only ledger table; DELETE is not permitted',
                            TG_TABLE_NAME
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF (to_jsonb(OLD) ->> settable_column) IS NOT NULL THEN
                        RAISE EXCEPTION
                            'priorstate: %.% has already been set and cannot be changed',
                            TG_TABLE_NAME, settable_column
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF (to_jsonb(NEW) - settable_column) IS DISTINCT FROM (to_jsonb(OLD) - settable_column) THEN
                        RAISE EXCEPTION
                            'priorstate: % is append-only; only % may be set, and only once',
                            TG_TABLE_NAME, settable_column
                            USING ERRCODE = 'restrict_violation',
                                  HINT = 'Every other column is immutable from the moment of insert.';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            // --- snapshots: the chain itself. ---
            migrationBuilder.Sql("""
                CREATE TRIGGER snapshots_append_only
                    BEFORE UPDATE OR DELETE ON snapshots
                    FOR EACH ROW EXECUTE FUNCTION priorstate_set_once_only('TimestampAnchorId');
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER snapshots_no_truncate
                    BEFORE TRUNCATE ON snapshots
                    FOR EACH STATEMENT EXECUTE FUNCTION priorstate_reject_mutation();
                """);

            // --- timestamp_anchors: the external attestations. Never touched after insert. ---
            migrationBuilder.Sql("""
                CREATE TRIGGER timestamp_anchors_append_only
                    BEFORE UPDATE OR DELETE ON timestamp_anchors
                    FOR EACH ROW EXECUTE FUNCTION priorstate_reject_mutation();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER timestamp_anchors_no_truncate
                    BEFORE TRUNCATE ON timestamp_anchors
                    FOR EACH STATEMENT EXECUTE FUNCTION priorstate_reject_mutation();
                """);

            // --- audit_log: who did and saw what. A deletable access log proves nothing. ---
            migrationBuilder.Sql("""
                CREATE TRIGGER audit_log_append_only
                    BEFORE UPDATE OR DELETE ON audit_log
                    FOR EACH ROW EXECUTE FUNCTION priorstate_reject_mutation();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER audit_log_no_truncate
                    BEFORE TRUNCATE ON audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION priorstate_reject_mutation();
                """);

            // --- capture_profile_versions: referenced by name and version in every protocol. ---
            migrationBuilder.Sql("""
                CREATE TRIGGER capture_profile_versions_append_only
                    BEFORE UPDATE OR DELETE ON capture_profile_versions
                    FOR EACH ROW EXECUTE FUNCTION priorstate_set_once_only('SupersededAt');
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER capture_profile_versions_no_truncate
                    BEFORE TRUNCATE ON capture_profile_versions
                    FOR EACH STATEMENT EXECUTE FUNCTION priorstate_reject_mutation();
                """);

            // --- deployment_ledger_entries: the code-to-render bridge. ---
            migrationBuilder.Sql("""
                CREATE TRIGGER deployment_ledger_entries_append_only
                    BEFORE UPDATE OR DELETE ON deployment_ledger_entries
                    FOR EACH ROW EXECUTE FUNCTION priorstate_set_once_only('RunId');
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER deployment_ledger_entries_no_truncate
                    BEFORE TRUNCATE ON deployment_ledger_entries
                    FOR EACH STATEMENT EXECUTE FUNCTION priorstate_reject_mutation();
                """);

            // --- Defence in depth: a role with no privilege to attempt any of the above. ---
            //
            // The triggers above bind everyone, including a superuser and the table owner, so this
            // role is not what makes the guarantee hold. It exists so an operator can run the
            // application as something other than the schema owner and have the privilege system
            // agree with the triggers. Migrations still run as the owner. See docs/operations.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'priorstate_app') THEN
                        CREATE ROLE priorstate_app NOLOGIN;
                    END IF;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                GRANT SELECT, INSERT ON
                    snapshots, timestamp_anchors, audit_log,
                    capture_profile_versions, deployment_ledger_entries
                    TO priorstate_app;
                """);

            // Only the narrow, set-once columns are updatable, matching the triggers exactly.
            migrationBuilder.Sql("""
                GRANT UPDATE ("TimestampAnchorId") ON snapshots TO priorstate_app;
                GRANT UPDATE ("SupersededAt") ON capture_profile_versions TO priorstate_app;
                GRANT UPDATE ("RunId") ON deployment_ledger_entries TO priorstate_app;
                """);

            // Operational state, as opposed to recorded history: fully mutable on purpose.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE, DELETE ON projects, runs, crawl_jobs TO priorstate_app;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversible only so that `ef migrations` stays usable in development. Running this
            // against a production archive removes the protection the archive rests on.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS snapshots_append_only ON snapshots;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS snapshots_no_truncate ON snapshots;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS timestamp_anchors_append_only ON timestamp_anchors;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS timestamp_anchors_no_truncate ON timestamp_anchors;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_append_only ON audit_log;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_no_truncate ON audit_log;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS capture_profile_versions_append_only ON capture_profile_versions;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS capture_profile_versions_no_truncate ON capture_profile_versions;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS deployment_ledger_entries_append_only ON deployment_ledger_entries;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS deployment_ledger_entries_no_truncate ON deployment_ledger_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS priorstate_set_once_only();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS priorstate_reject_mutation();");
        }
    }
}
