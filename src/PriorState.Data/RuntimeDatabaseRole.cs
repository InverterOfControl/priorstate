using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PriorState.Data;

/// <summary>Run only with the schema administrator, after migrations, before starting runtime services.</summary>
public static class RuntimeDatabaseRole
{
    public static async Task ConfigureAsync(PriorStateDbContext db, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        // Separate connection keeps the secret out of EF command logging. A parameter and PostgreSQL's
        // literal quoting support arbitrary passwords without building SQL from untrusted text.
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var secret = new NpgsqlCommand("SELECT set_config('priorstate.runtime_password', @password, true)", connection, transaction))
        {
            secret.Parameters.AddWithValue("password", password);
            await secret.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var command = new NpgsqlCommand("""
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM pg_auth_members WHERE member = 'priorstate_app'::regrole)
                   OR EXISTS (SELECT FROM pg_class WHERE relowner = 'priorstate_app'::regrole)
                   OR EXISTS (SELECT FROM pg_namespace WHERE nspowner = 'priorstate_app'::regrole)
                   OR EXISTS (SELECT FROM pg_database WHERE datdba = 'priorstate_app'::regrole)
                   OR EXISTS (SELECT FROM pg_proc WHERE proowner = 'priorstate_app'::regrole) THEN
                    RAISE EXCEPTION 'priorstate_app must not own objects or belong to other roles; use a separate schema administrator';
                END IF;
                EXECUTE format('ALTER ROLE priorstate_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT PASSWORD %L', current_setting('priorstate.runtime_password'));
            END $$;
            REVOKE CREATE ON SCHEMA public FROM PUBLIC, priorstate_app;
            GRANT USAGE ON SCHEMA public TO priorstate_app;
            REVOKE ALL ON ALL TABLES IN SCHEMA public FROM priorstate_app;
            GRANT SELECT, INSERT ON snapshots, timestamp_anchors, audit_log,
                capture_profile_versions, deployment_ledger_entries, plugin_binding_versions TO priorstate_app;
            GRANT UPDATE ("TimestampAnchorId") ON snapshots TO priorstate_app;
            GRANT UPDATE ("SupersededAt") ON capture_profile_versions, plugin_binding_versions TO priorstate_app;
            GRANT UPDATE ("RunId") ON deployment_ledger_entries TO priorstate_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON projects, runs, crawl_jobs,
                "AspNetUsers", "AspNetRoles", "AspNetUserClaims", "AspNetRoleClaims",
                "AspNetUserLogins", "AspNetUserRoles", "AspNetUserTokens" TO priorstate_app;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO priorstate_app;
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
