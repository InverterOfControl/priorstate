using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PriorState.Data;

/// <summary>
/// Used only by `dotnet ef` when generating migrations. The connection string is never opened for
/// scaffolding, so a placeholder is fine and no local database is needed to add a migration.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PriorStateDbContext>
{
    public PriorStateDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PRIORSTATE_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=priorstate;Username=priorstate;Password=design-time-only";

        var options = new DbContextOptionsBuilder<PriorStateDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(PriorStateDbContext).Assembly.FullName))
            .Options;

        return new PriorStateDbContext(options);
    }
}
