using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PriorState.Data.Tests;

public sealed class RuntimeDatabaseRoleTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;

    public RuntimeDatabaseRoleTests(PostgresFixture postgres) => _postgres = postgres;

    private async Task<PriorStateDbContext> RuntimeContextAsync()
    {
        await using var owner = _postgres.CreateContext();
        // Includes quotes to exercise safe server-side password quoting.
        const string password = "runtime-test-'password";
        await RuntimeDatabaseRole.ConfigureAsync(owner, password);
        var connection = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString)
        {
            Username = "priorstate_app",
            Password = password,
        };
        return new PriorStateDbContext(new DbContextOptionsBuilder<PriorStateDbContext>()
            .UseNpgsql(connection.ConnectionString).Options);
    }

    [Theory]
    [InlineData("ALTER TABLE snapshots DISABLE TRIGGER ALL")]
    [InlineData("DROP TRIGGER snapshots_append_only ON snapshots")]
    [InlineData("DROP TABLE snapshots CASCADE")]
    [InlineData("DROP FUNCTION priorstate_set_once_only() CASCADE")]
    [InlineData("SET session_replication_role = replica")]
    [InlineData("CREATE TABLE public.untrusted (id integer)")]
    [InlineData("TRUNCATE snapshots CASCADE")]
    [InlineData("DELETE FROM snapshots")]
    [InlineData("UPDATE snapshots SET \"Url\" = 'tampered'")]
    public async Task RuntimeCannotAlterHistoryOrRemoveProtections(string sql)
    {
        await using var db = await RuntimeContextAsync();
#pragma warning disable EF1002 // Fixed test inputs, never user input.
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
#pragma warning restore EF1002
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task RuntimeCanCaptureCreateAccountsAndUpdateOperationalState()
    {
        await using var db = await RuntimeContextAsync();
        var snapshot = await PostgresFixture.SeedSnapshotAsync(db);
        var user = new ApplicationUser { UserName = "runtime-user", Email = "runtime@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        user.AccessFailedCount = 1;
        await db.SaveChangesAsync();
        var run = await db.Runs.SingleAsync(r => r.Id == snapshot.RunId);
        run.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var binding = await PostgresFixture.SeedPluginBindingAsync(db);
        db.SourceExecutions.Add(new Domain.Entities.SourceExecution { BindingId = binding.Id });
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlAsync($"UPDATE plugin_binding_versions SET \"SupersededAt\" = {now} WHERE \"Id\" = {binding.Id}");
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"UPDATE plugin_binding_versions SET \"SupersededAt\" = {now} WHERE \"Id\" = {binding.Id}"));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }
}
