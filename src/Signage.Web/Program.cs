using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Infrastructure;
using Signage.Infrastructure.Persistence;
using Signage.Infrastructure.Storage;
using Signage.Web.Endpoints;
using Signage.Web.Services;
using SignageAuthenticationOptions = Signage.Application.AuthenticationOptions;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options => options.ServiceName = "School Signage");
builder.Host.UseSystemd();

// The default Windows Event Log provider can require machine-level source
// registration. Console, debug, and EventSource logging work for local runs,
// systemd, Windows services, containers, and the test host without elevation.
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ");
}
else
{
    builder.Logging.AddJsonConsole();
}
builder.Logging.AddDebug();
builder.Logging.AddEventSourceLogger();

builder.Services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<SignageOptions>().BindConfiguration(SignageOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<UploadOptions>().BindConfiguration(UploadOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<RenderingOptions>().BindConfiguration(RenderingOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<MediaOptions>().BindConfiguration(MediaOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<BackupOptions>().BindConfiguration(BackupOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<SignageAuthenticationOptions>().BindConfiguration(SignageAuthenticationOptions.SectionName).ValidateDataAnnotations()
    .Validate(options => new[] { "Development", "OpenIdConnect", "ActiveDirectory" }.Contains(options.Mode, StringComparer.OrdinalIgnoreCase), "Choose Development, OpenIdConnect, or ActiveDirectory authentication.").ValidateOnStart();

var authMode = builder.Configuration[$"{SignageAuthenticationOptions.SectionName}:Mode"] ?? "Development";
builder.Services.AddOptions<ActiveDirectoryOptions>().BindConfiguration(ActiveDirectoryOptions.SectionName).ValidateDataAnnotations()
    .Validate(options => !authMode.Equals("ActiveDirectory", StringComparison.OrdinalIgnoreCase) || options.IsConfigured,
        "ActiveDirectory requires an LDAPS Host, DomainName, and BaseDn.").ValidateOnStart();
if (builder.Environment.IsProduction() && authMode.Equals("Development", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Development authentication cannot be enabled in Production.");
}

builder.Services.AddRazorPages();
builder.Services.AddProblemDetails();
var maximumUploadBytes = builder.Configuration.GetValue<long?>($"{UploadOptions.SectionName}:MaximumBytes")
    ?? 250L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maximumUploadBytes);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maximumUploadBytes;
    options.MemoryBufferThreshold = 64 * 1024;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
});

var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = authMode.Equals("OpenIdConnect", StringComparison.OrdinalIgnoreCase)
        ? OpenIdConnectDefaults.AuthenticationScheme
        : CookieAuthenticationDefaults.AuthenticationScheme;
}).AddCookie(options =>
{
    options.Cookie.Name = builder.Environment.IsProduction() ? "__Host-SchoolSignage" : "SchoolSignage.Development";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction() || authMode.Equals("ActiveDirectory", StringComparison.OrdinalIgnoreCase) ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.LoginPath = authMode.Equals("Development", StringComparison.OrdinalIgnoreCase) ? "/dev-login" : "/Login";
    options.AccessDeniedPath = "/access-denied";
    options.SlidingExpiration = false;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(builder.Configuration.GetValue<int?>("Authentication:SessionMinutes") ?? 480);
    options.EventsType = typeof(StaffCookieEvents);
});

if (authMode.Equals("OpenIdConnect", StringComparison.OrdinalIgnoreCase))
{
    authentication.AddOpenIdConnect(options =>
    {
        var section = builder.Configuration.GetSection(SignageAuthenticationOptions.SectionName);
        options.Authority = section["Authority"];
        options.ClientId = section["ClientId"];
        options.ClientSecret = section["ClientSecret"];
        options.ResponseType = "code";
        options.SaveTokens = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "role";
        options.Events.OnTokenValidated = async context =>
        {
            var account = await context.HttpContext.RequestServices.GetRequiredService<StaffAccessService>()
                .SignInFederatedAsync(context.Principal!, context.HttpContext.RequestAborted);
            if (!account.IsEnabled) context.Fail("This signage account is disabled.");
            else context.Principal = StaffAccessService.CreatePrincipal(account);
        };
    });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Teacher", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        context.User.IsInRole("Teacher") || context.User.IsInRole("SignageAdmin")));
    options.AddPolicy("SignageAdmin", policy => policy.RequireAuthenticatedUser().RequireRole("SignageAdmin"));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("pairing", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("player", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("directory-login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var dataRoot = Path.GetFullPath(builder.Configuration[$"{StorageOptions.SectionName}:RootPath"] ?? "../../.local-data/content", builder.Environment.ContentRootPath);
var keyRoot = Path.Combine(Path.GetDirectoryName(dataRoot)!, "keys");
Directory.CreateDirectory(keyRoot);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRoot)).SetApplicationName("SchoolSignage");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSignageInfrastructure(builder.Configuration);
builder.Services.AddScoped<PresentationService>();
builder.Services.AddScoped<StaffAccessService>();
builder.Services.AddScoped<StaffCookieEvents>();
builder.Services.AddSingleton<IActiveDirectoryAuthenticator, ActiveDirectoryAuthenticator>();
builder.Services.AddScoped<PairingService>();
builder.Services.AddScoped<DeviceManagementService>();
builder.Services.AddScoped<PresentationLibraryService>();
builder.Services.AddScoped<DevelopmentSeeder>();
builder.Services.AddSingleton<OperationalMetrics>();
builder.Services.AddSingleton<SlidePosters>();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<ReadinessHealthCheck>("dependencies", tags: ["ready"]);

var app = builder.Build();

_ = app.Services.GetRequiredService<DataRootInstanceLock>();
if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    await app.Services.GetRequiredService<DatabaseInitializer>().MigrateAsync(CancellationToken.None);
    return;
}
if (args.Contains("--maintenance-dry-run", StringComparer.OrdinalIgnoreCase))
{
    await using var maintenanceScope = app.Services.CreateAsyncScope();
    var result = await maintenanceScope.ServiceProvider.GetRequiredService<MaintenanceProcessor>()
        .RunAsync(true, CancellationToken.None);
    app.Logger.LogInformation(
        "Maintenance dry run complete versions={Versions} pairings={Pairings} temporary={Temporary}",
        result.ArchivedVersions,
        result.ExpiredPairingSessions,
        result.TemporaryItems);
    return;
}
if (app.Environment.IsDevelopment())
{
    await app.Services.GetRequiredService<DatabaseInitializer>().MigrateAsync(CancellationToken.None);
    await using var seedScope = app.Services.CreateAsyncScope();
    await seedScope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(CancellationToken.None);
}

// Process proxy metadata before HTTPS redirection, authentication, rate limits,
// and URL generation. The middleware's default trust boundary accepts only
// loopback proxies, matching the bundled local Nginx configuration.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    var suppliedCorrelationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    var correlationId = !string.IsNullOrWhiteSpace(suppliedCorrelationId) && suppliedCorrelationId.Length <= 64 && suppliedCorrelationId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
        ? suppliedCorrelationId
        : Guid.NewGuid().ToString("N");
    context.TraceIdentifier = correlationId;
    context.Response.OnStarting(() => { context.Response.Headers["X-Correlation-ID"] = correlationId; return Task.CompletedTask; });
    using var logScope = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Signage.Request").BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
    if (context.Request.Path.Equals("/api/player/assignment"))
    {
        context.Response.OnCompleted(() =>
        {
            if (context.Response.StatusCode >= 400)
            {
                context.RequestServices.GetRequiredService<OperationalMetrics>().RecordAssignmentError();
            }
            return Task.CompletedTask;
        });
    }
    var youtubeAllowed = context.Request.Path.StartsWithSegments("/player") && context.RequestServices.GetRequiredService<IOptions<MediaOptions>>().Value.EnableYouTube;
    var youtubeScriptSources = youtubeAllowed ? " https://www.youtube.com https://s.ytimg.com" : "";
    var youtubeFrameSources = youtubeAllowed ? "https://www.youtube-nocookie.com" : "'none'";
    context.Response.Headers.ContentSecurityPolicy = $"default-src 'self'; script-src 'self'{youtubeScriptSources}; frame-src {youtubeFrameSources}; style-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; connect-src 'self'; worker-src 'self' blob:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = youtubeAllowed ? "strict-origin-when-cross-origin" : "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (context.Request.Path.Equals("/player/sw.js"))
    {
        context.Response.Headers["Service-Worker-Allowed"] = "/";
        context.Response.Headers.CacheControl = "no-cache";
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() && authMode.Equals("Development", StringComparison.OrdinalIgnoreCase))
{
    app.MapGet("/dev-login", async (string? asRole, HttpContext context, StaffAccessService staff) =>
    {
        if (string.IsNullOrWhiteSpace(asRole))
        {
            // Styles come from site.css: the CSP blocks inline <style> blocks.
            var monitor = Signage.Web.Ui.Icons.Get("monitor").ToString();
            var chevron = Signage.Web.Ui.Icons.Get("chevron-right").ToString();
            return Results.Content($$"""
                <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Sign in · School Signage</title><link rel="stylesheet" href="/css/site.css"><script src="/js/prefs.js"></script></head>
                <body class="signin"><main class="signin-card">
                <div class="signin-head"><span class="brand"><span class="brand-mark">{{monitor}}</span><span>School Signage</span></span><span class="dev-chip">Development sign-in</span></div>
                <h1>Sign in</h1>
                <div class="signin-options">
                <a class="signin-option" href="/dev-login?asRole=teacher"><span class="avatar">AM</span><span><strong>Continue as teacher</strong><small>Alex Morgan</small></span>{{chevron}}</a>
                <a class="signin-option" href="/dev-login?asRole=admin"><span class="avatar">ST</span><span><strong>Continue as administrator</strong><small>Sam Taylor</small></span>{{chevron}}</a>
                </div></main></body></html>
                """, "text/html");
        }
        var isAdmin = asRole.Equals("admin", StringComparison.OrdinalIgnoreCase);
        var subject = isAdmin ? "dev-admin" : "dev-teacher";
        var account = await staff.FindAsync(subject, context.RequestAborted);
        if (account is null || !account.IsEnabled) return Results.Forbid();
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffAccessService.CreatePrincipal(account));
        return Results.Redirect(account.Role == Signage.Domain.StaffRole.Administrator ? "/Admin" : account.Role == Signage.Domain.StaffRole.Teacher ? "/Publish" : "/AccessPending");
    }).AllowAnonymous();
}

app.MapPost("/sign-out", async (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.BadRequest(); }
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).RequireAuthorization();
app.MapGet("/access-denied", () => Results.Content("Access denied", "text/plain", statusCode: StatusCodes.Status403Forbidden));
app.MapRazorPages();
app.MapHumanApi();
app.MapAdminApi();
app.MapPlayerApi();
app.MapContentApi();
app.MapMetricsApi();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

public partial class Program;
