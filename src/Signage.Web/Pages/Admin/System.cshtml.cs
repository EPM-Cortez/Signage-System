using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Pages.Admin;

[Authorize(Policy = "SignageAdmin")]
public sealed class SystemModel(
    IDbContextFactory<SignageDbContext> dbFactory,
    IContentStorage storage,
    IConverterRunner converter,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    IOptions<StorageOptions> storageOptions,
    IOptions<RenderingOptions> renderingOptions) : PageModel
{
    public bool DatabaseReady { get; private set; }
    public bool StorageWritable { get; private set; }
    public bool ConverterReady { get; private set; }
    public int QueuedJobs { get; private set; }
    public int FailedVersions { get; private set; }
    public string AvailableSpace { get; private set; } = string.Empty;
    public string DatabasePath { get; private set; } = string.Empty;
    public string ContentRoot => Path.GetFullPath(storageOptions.Value.RootPath, environment.ContentRootPath);
    public string TempRoot => Path.GetFullPath(renderingOptions.Value.TempRoot, environment.ContentRootPath);
    public string MachineName => Environment.MachineName;
    public string OperatingSystem => RuntimeInformation.OSDescription;
    public string Runtime => $"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})";
    public string EnvironmentName => environment.EnvironmentName;
    public string AppVersion => typeof(SystemModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "Unknown";
    public TimeSpan Uptime => DateTime.Now - Process.GetCurrentProcess().StartTime;
    public string MemoryUsage => $"{Environment.WorkingSet / 1048576.0:0} MB";
    public string ServerTime => $"{DateTimeOffset.Now:d MMM yyyy, HH:mm} · {TimeZoneInfo.Local.DisplayName}";
    public async Task OnGetAsync(CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token); DatabaseReady = await db.Database.CanConnectAsync(token); StorageWritable = await storage.CanWriteAsync(token); ConverterReady = await converter.CheckHealthAsync(token); QueuedJobs = await db.ConversionJobs.CountAsync(item => item.Status == ConversionJobStatus.Queued, token); FailedVersions = await db.PresentationVersions.CountAsync(item => item.Status == PresentationVersionStatus.Failed, token); AvailableSpace = FormatBytes(storage.GetAvailableBytes());
        var connection = configuration.GetConnectionString("SignageDb") ?? string.Empty; var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection); DatabasePath = Path.GetFullPath(builder.DataSource, environment.ContentRootPath);
    }
    private static string FormatBytes(long bytes) { string[] units = ["B", "KB", "MB", "GB", "TB"]; var value = (double)bytes; var unit = 0; while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; } return $"{value:0.0} {units[unit]} available"; }
}
