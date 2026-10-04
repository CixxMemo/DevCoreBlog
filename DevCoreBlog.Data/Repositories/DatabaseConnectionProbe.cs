using DevCoreBlog.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;

namespace DevCoreBlog.Data.Repositories;

/// <summary>Opens the scoped persistence connection under provider deadlines so metric reads reuse it.</summary>
public sealed class DatabaseConnectionProbe(ApplicationDbContext context) : IDatabaseConnectionProbe
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State == ConnectionState.Closed)
        {
            // Startup/handshake cancellation alone is insufficient; bound the driver's own timeouts too.
            var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString)
            {
                Timeout = 2,
                CommandTimeout = 2,
                CancellationTimeout = 500
            };
            connection.ConnectionString = settings.ConnectionString;
        }
        context.Database.SetCommandTimeout(2);
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection) { CommandTimeout = 2 };
        await command.ExecuteScalarAsync(cancellationToken);
        // The scoped DbContext owns this connection and closes it at request completion.
    }
}
