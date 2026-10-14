"""Capture only the tagged teaser SQL in an owned empty-dotenv source; never record result values."""
import argparse
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);a=p.parse_args();source=a.source.resolve()
assert source.name=='source' and str(source).startswith('/private/tmp/devcoreblog-f17.') and (source/'.env').read_text()=='' and not (source/'.git').exists()
program=source/'Program.cs';s=program.read_text();key='npgsqlOptions => npgsqlOptions.MigrationsAssembly(migrationsAssemblyName)));';assert key in s
program.write_text(s.replace(key,'npgsqlOptions => npgsqlOptions.MigrationsAssembly(migrationsAssemblyName))\n        .AddInterceptors(new F32ProjectionCapture()));'))
(source/'F32ProjectionCapture.cs').write_text('''using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
// Fixture-only SQL shape evidence; no parameter values or result rows are logged.
public sealed class F32ProjectionCapture : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("F32 public newsletter teaser", StringComparison.Ordinal)
            && Environment.GetEnvironmentVariable("DEVCORE_F32_SQL_CAPTURE") is { } path)
            await File.AppendAllTextAsync(path, command.CommandText + "\\n---F32---\\n", cancellationToken);
        return result;
    }
}
''')
