using DevCoreBlog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DevCoreBlog.Infrastructure;

/// <summary>
/// Creates the database context for EF tooling without starting the web host.
/// Runtime configuration remains in Program; design-time commands receive the
/// same connection string through the environment.
/// </summary>
public sealed class ApplicationDbContextFactory
    : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "DB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "DB_CONNECTION_STRING is required for Entity Framework tooling.");
        }

        var migrationsAssemblyName = typeof(ApplicationDbContextFactory)
            .Assembly
            .GetName()
            .Name
            ?? throw new InvalidOperationException(
                "The Web assembly name is required for Entity Framework migrations.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    migrationsAssemblyName))
            .Options;

        return new ApplicationDbContext(options);
    }
}
