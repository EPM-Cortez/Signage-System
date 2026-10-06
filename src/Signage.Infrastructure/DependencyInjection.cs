using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;
using Signage.Application;
using Signage.Infrastructure.Conversion;
using Signage.Infrastructure.Persistence;
using Signage.Infrastructure.Storage;

namespace Signage.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSignageInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddDbContextFactory<SignageDbContext>((provider, options) =>
        {
            var connectionString = configuration.GetConnectionString("SignageDb")
                ?? throw new InvalidOperationException("ConnectionStrings:SignageDb is required.");
            var connection = new SqliteConnectionStringBuilder(connectionString);
            if (!Path.IsPathRooted(connection.DataSource))
            {
                connection.DataSource = Path.GetFullPath(
                    connection.DataSource,
                    provider.GetRequiredService<IHostEnvironment>().ContentRootPath);
            }
            options.UseSqlite(connection.ConnectionString, sqlite => sqlite.MigrationsAssembly(typeof(SignageDbContext).Assembly.FullName));
            options.AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>());
        });
        services.AddSingleton<DataRootInstanceLock>();
        services.AddSingleton<IContentStorage, LocalContentStorage>();
        services.AddScoped<IPresentationInspector, PptxPackageInspector>();
        services.AddScoped<IConverterRunner, NodeConverterRunner>();
        services.AddScoped<PptxVideoExtractor>();
        services.AddScoped<ConversionJobProcessor>();
        services.AddSingleton<ConversionProgressTracker>();
        services.AddScoped<MaintenanceProcessor>();
        services.AddScoped<ContentDeletionProcessor>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddHostedService<ConversionWorker>();
        services.AddHostedService<MaintenanceWorker>();
        return services;
    }
}
