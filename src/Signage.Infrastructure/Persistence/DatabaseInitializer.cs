using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Application;

namespace Signage.Infrastructure.Persistence;

public sealed class DatabaseInitializer(
    IDbContextFactory<SignageDbContext> dbFactory,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        if (databaseOptions.Value.EnableWal)
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        }
        await db.Database.ExecuteSqlRawAsync("PRAGMA optimize;", cancellationToken);
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken);
    }
}
