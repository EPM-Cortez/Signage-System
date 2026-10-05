using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure;
using Signage.Infrastructure.Conversion;
using Signage.Infrastructure.Persistence;
using Signage.Web.Services;

namespace Signage.IntegrationTests;

public sealed class ActiveDirectoryTests : IDisposable
{
    private readonly DirectoryApplication app = new();

    [Fact]
    public async Task Verified_bootstrap_admin_can_manage_access_and_new_staff_start_pending()
    {
        using var admin = app.Client();
        var login = await LoginAsync(admin, "admin");
        Assert.Equal("/Admin", login.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Admin/Access")).StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("SchoolSignage.", StringComparison.Ordinal));
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        using var staff = app.Client();
        Assert.Equal("/AccessPending", (await LoginAsync(staff, "teacher")).Headers.Location!.OriginalString);
        Assert.Contains("Awaiting approval", await staff.GetStringAsync("/AccessPending"));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/screen-groups/available")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/devices")).StatusCode);
        Assert.Equal(StaffRole.Pending, (await app.AccountAsync("teacher")).Role);
    }

    [Fact]
    public async Task Bad_password_is_generic_not_reflected_or_persisted()
    {
        using var client = app.Client();
        var response = await LoginAsync(client, "teacher", "dummy-wrong-password-not-a-real-secret");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Sign-in failed", html);
        Assert.DoesNotContain("dummy-wrong-password-not-a-real-secret", html);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(0, await app.CountStaffAsync());
    }

    [Fact]
    public async Task Password_sign_in_over_http_is_blocked_before_contacting_directory()
    {
        using var client = app.Client(https: false);
        var response = await LoginAsync(client, "admin");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.Directory.Calls);
        Assert.Contains("HTTPS", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sign_in_requires_antiforgery_and_rejects_external_return_url()
    {
        using var client = app.Client();
        var unprotected = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Username"] = "admin", ["Input.Password"] = "test-only" }));
        Assert.Equal(HttpStatusCode.BadRequest, unprotected.StatusCode);
        Assert.Equal(0, app.Directory.Calls);
        var protectedLogin = await LoginAsync(client, "admin", returnUrl: "https://attacker.invalid/");
        Assert.Equal("/Admin", protectedLogin.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Approval_limits_picker_and_api_uploads_and_revocation_updates_existing_session()
    {
        using var admin = app.Client();
        using var teacher = app.Client();
        await LoginAsync(admin, "admin");
        await LoginAsync(teacher, "teacher");
        var account = await app.AccountAsync("teacher");
        await SaveAsync(admin, account.Id, StaffRole.Teacher, true, [DevelopmentSeeder.ReceptionId]);
        var groups = await teacher.GetFromJsonAsync<GroupResult[]>("/api/screen-groups/available");
        Assert.Equal("Reception", Assert.Single(groups!).Name);
        var picker = await teacher.GetStringAsync("/Publish");
        Assert.Contains("Main reception screens", picker);
        Assert.DoesNotContain("Staff briefing screens", picker);
        var csrf = GetCsrf(picker);
        var denied = await UploadAsync(teacher, DevelopmentSeeder.StaffRoomId, csrf);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var accepted = await UploadAsync(teacher, DevelopmentSeeder.ReceptionId, csrf);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(1, await app.CountQueuedAsync());
        // Teacher cannot change their own role by posting to the administrator form.
        var escalation = await SaveAsync(teacher, account.Id, StaffRole.Administrator, true, []);
        Assert.NotEqual(HttpStatusCode.OK, escalation.StatusCode);
        Assert.Equal(StaffRole.Teacher, (await app.AccountAsync("teacher")).Role);
        await SaveAsync(admin, account.Id, StaffRole.Teacher, true, []);
        Assert.Empty((await teacher.GetFromJsonAsync<GroupResult[]>("/api/screen-groups/available"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(teacher, DevelopmentSeeder.ReceptionId, GetCsrf(await teacher.GetStringAsync("/Publish")))).StatusCode);
        await SaveAsync(admin, account.Id, StaffRole.Pending, true, []);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/screen-groups/available")).StatusCode);
        await SaveAsync(admin, account.Id, StaffRole.Teacher, false, []);
        Assert.Equal(HttpStatusCode.Unauthorized, (await teacher.GetAsync("/api/screen-groups/available")).StatusCode);
        Assert.Contains("Sign-in failed", await (await LoginAsync(teacher, "teacher")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Promotion_and_demotion_apply_to_the_existing_cookie()
    {
        using var admin = app.Client();
        using var teacher = app.Client();
        await LoginAsync(admin, "admin"); await LoginAsync(teacher, "teacher");
        var account = await app.AccountAsync("teacher");
        await SaveAsync(admin, account.Id, StaffRole.Administrator, true, []);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync("/api/admin/devices")).StatusCode);
        Assert.Equal(2, (await teacher.GetFromJsonAsync<GroupResult[]>("/api/screen-groups/available"))!.Length);
        await SaveAsync(admin, account.Id, StaffRole.Teacher, true, [DevelopmentSeeder.StaffRoomId]);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/admin/devices")).StatusCode);
        Assert.Equal("Staff Room", Assert.Single((await teacher.GetFromJsonAsync<GroupResult[]>("/api/screen-groups/available"))!).Name);
    }

    [Fact]
    public async Task Last_administrator_and_invalid_groups_cannot_be_saved()
    {
        using var admin = app.Client(); await LoginAsync(admin, "admin");
        var account = await app.AccountAsync("admin");
        var response = await SaveAsync(admin, account.Id, StaffRole.Teacher, false, []);
        Assert.Contains("Keep at least one enabled administrator", await response.Content.ReadAsStringAsync());
        Assert.Equal(StaffRole.Administrator, (await app.AccountAsync("admin")).Role);
        Assert.True((await app.AccountAsync("admin")).IsEnabled);
        var csrf = GetCsrf(await admin.GetStringAsync("/Admin/Access"));
        await admin.PostAsync("/Admin/Access?handler=Add", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = csrf, ["Input.Username"] = "teacher", ["Input.Role"] = "Administrator", ["Input.IsEnabled"] = "true" }));
        Assert.Null((await app.AccountAsync("teacher")).ExternalSubject);
        response = await SaveAsync(admin, account.Id, StaffRole.Teacher, false, []);
        Assert.Contains("Keep at least one enabled administrator", await response.Content.ReadAsStringAsync());
        response = await SaveAsync(admin, account.Id, StaffRole.Administrator, true, [Guid.NewGuid()]);
        Assert.Contains("Choose active screen groups only", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Permissions_can_be_preassigned_and_recycled_usernames_cannot_inherit_them()
    {
        using var admin = app.Client(); await LoginAsync(admin, "admin");
        var token = GetCsrf(await admin.GetStringAsync("/Admin/Access"));
        var add = await admin.PostAsync("/Admin/Access?handler=Add", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Input.Username"] = "TEACHER", ["Input.Role"] = "Teacher",
            ["Input.IsEnabled"] = "true", ["Input.ScreenGroupIds"] = DevelopmentSeeder.ReceptionId.ToString()
        }));
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        var before = await app.AccountAsync("teacher"); Assert.Null(before.ExternalSubject);
        using var teacher = app.Client();
        Assert.Equal("/Publish", (await LoginAsync(teacher, "teacher")).Headers.Location!.OriginalString);
        var after = await app.AccountAsync("teacher");
        Assert.Equal(before.IdentitySubject, after.IdentitySubject);
        Assert.NotNull(after.ExternalSubject);
        app.Directory.RecycleTeacher = true;
        using var recycled = app.Client();
        Assert.Contains("Sign-in failed", await (await LoginAsync(recycled, "teacher")).Content.ReadAsStringAsync());
        Assert.Equal(after.ExternalSubject, (await app.AccountAsync("teacher")).ExternalSubject);
    }

    [Fact]
    public async Task Directory_test_does_not_create_an_account_or_change_current_identity()
    {
        using var admin = app.Client(); await LoginAsync(admin, "admin");
        var token = GetCsrf(await admin.GetStringAsync("/Admin/Directory"));
        var response = await admin.PostAsync("/Admin/Directory", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Input.Username"] = "teacher", ["Input.Password"] = "test-only"
        }));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Connection successful", html);
        Assert.DoesNotContain("value=\"test-only\"", html);
        Assert.Equal(1, await app.CountStaffAsync());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/devices")).StatusCode);
    }

    [Fact]
    public async Task Permission_forms_require_antiforgery()
    {
        using var admin = app.Client(); await LoginAsync(admin, "admin");
        var account = await app.AccountAsync("admin");
        var response = await admin.PostAsync("/Admin/Access?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["id"] = account.Id.ToString(), ["Input.Role"] = "Teacher", ["Input.IsEnabled"] = "false" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await app.AccountAsync("admin")).IsEnabled);
    }

    [Fact]
    public async Task Repeated_login_attempts_are_rate_limited()
    {
        using var client = app.Client(); var token = GetCsrf(await client.GetStringAsync("/Login"));
        HttpResponseMessage? response = null;
        for (var i = 0; i < 22; i++)
        {
            response?.Dispose();
            response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = token, ["Input.Username"] = "teacher", ["Input.Password"] = "wrong-test-password" }));
            if (response.StatusCode == HttpStatusCode.TooManyRequests) break;
        }
        using (response) Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        Assert.InRange(app.Directory.Calls, 1, 19);
    }

    [Fact]
    public async Task Sign_out_requires_a_token_and_removes_the_staff_session()
    {
        using var admin = app.Client(); await LoginAsync(admin, "admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/sign-out", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/devices")).StatusCode);
        var csrf = GetCsrf(await admin.GetStringAsync("/Admin/Access"));
        var response = await admin.PostAsync("/sign-out", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/admin/devices")).StatusCode);
    }

    [Fact]
    public async Task Native_ldap_client_never_attempts_an_empty_password_bind()
    {
        using var real = new ActiveDirectoryAuthenticator(Options.Create(DirectoryApplication.Settings), NullLogger<ActiveDirectoryAuthenticator>.Instance);
        Assert.Null(await real.AuthenticateAsync("teacher", "", CancellationToken.None));
    }

    [Theory]
    [InlineData("TEACHER", "teacher@example.test")]
    [InlineData("EXAMPLE\\Teacher", "teacher@example.test")]
    [InlineData("Teacher@Example.Test", "teacher@example.test")]
    public void Directory_login_formats_are_normalized(string input, string expected) => Assert.Equal(expected, DirectoryLoginNames.Normalize(input, DirectoryApplication.Settings));

    [Fact]
    public void Untrusted_domains_and_ldap_filter_injection_are_rejected_or_escaped()
    {
        Assert.Throws<ArgumentException>(() => DirectoryLoginNames.Normalize("OTHER\\teacher", DirectoryApplication.Settings));
        Assert.Throws<ArgumentException>(() => DirectoryLoginNames.Normalize("teacher@other.test", DirectoryApplication.Settings));
        Assert.Equal("a\\2a\\28b\\29\\5c\\00", DirectoryLoginNames.EscapeFilter("a*(b)\\\0"));
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password = "test-only", string? returnUrl = null)
    {
        var token = GetCsrf(await client.GetStringAsync("/Login"));
        return await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = token, ["Input.Username"] = username, ["Input.Password"] = password, ["ReturnUrl"] = returnUrl ?? "" }));
    }

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid id, StaffRole role, bool enabled, Guid[] groups)
    {
        var get = await client.GetAsync("/Admin/Access");
        var token = GetCsrf(await get.Content.ReadAsStringAsync());
        var values = new List<KeyValuePair<string, string>>
        { new("__RequestVerificationToken", token), new("id", id.ToString()), new("Input.Role", role.ToString()), new("Input.IsEnabled", enabled.ToString()) };
        values.AddRange(groups.Select(group => new KeyValuePair<string, string>("Input.ScreenGroupIds", group.ToString())));
        return await client.PostAsync("/Admin/Access?handler=Save", new FormUrlEncodedContent(values));
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid groupId, string csrf)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(csrf), "__RequestVerificationToken"); form.Add(new StringContent(groupId.ToString()), "screenGroupIds");
        // The conversion worker is disabled here: this fixture tests the upload's access boundary only.
        form.Add(new ByteArrayContent([80, 75, 3, 4, 0, 0, 0, 0]), "file", "permission-test.pptx");
        return await client.PostAsync("/api/presentations", form);
    }

    private static string GetCsrf(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    public void Dispose() => app.Dispose();
    private sealed record GroupResult(Guid Id, string Name);

    private sealed class FakeDirectory : IActiveDirectoryAuthenticator
    {
        public int Calls;
        public bool RecycleTeacher { get; set; }
        public Task<DirectoryIdentity?> AuthenticateAsync(string username, string password, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            var login = DirectoryLoginNames.Normalize(username, DirectoryApplication.Settings);
            var identity = password != "test-only" ? null : new DirectoryIdentity("ad:example.test:" + (login.StartsWith("admin@", StringComparison.Ordinal) ? "00000000-0000-4000-8000-000000000001" : RecycleTeacher ? "00000000-0000-4000-8000-000000000099" : "00000000-0000-4000-8000-000000000002"), login, login.StartsWith("admin@", StringComparison.Ordinal) ? "Test Administrator" : "Test Teacher");
            return Task.FromResult(identity);
        }
    }

    private sealed class DirectoryApplication : WebApplicationFactory<Program>
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"signage-ad-tests-{Guid.NewGuid():N}");
        public FakeDirectory Directory { get; } = new();
        public static ActiveDirectoryOptions Settings => new() { Host = "dc.example.test", DomainName = "example.test", NetBiosDomain = "EXAMPLE", BaseDn = "DC=example,DC=test", BootstrapAdminUpn = "admin@example.test" };
        public HttpClient Client(bool https = true) => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(https ? "https://localhost" : "http://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            System.IO.Directory.CreateDirectory(root);
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SignageDb"] = $"Data Source={Path.Combine(root, "signage.db")};Foreign Keys=True;Default Timeout=5",
                ["Database:InstanceLockPath"] = Path.Combine(root, "signage.lock"), ["Storage:RootPath"] = Path.Combine(root, "content"), ["Storage:MinimumFreeBytes"] = "0",
                ["Rendering:TempRoot"] = Path.Combine(root, "temp"), ["Rendering:FontDirectories:0"] = null,
                ["Authentication:Mode"] = "ActiveDirectory", ["ActiveDirectory:Host"] = Settings.Host, ["ActiveDirectory:DomainName"] = Settings.DomainName,
                ["ActiveDirectory:NetBiosDomain"] = Settings.NetBiosDomain, ["ActiveDirectory:BaseDn"] = Settings.BaseDn, ["ActiveDirectory:BootstrapAdminUpn"] = Settings.BootstrapAdminUpn
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IActiveDirectoryAuthenticator>(); services.AddSingleton<IActiveDirectoryAuthenticator>(Directory);
                foreach (var descriptor in services.Where(item => item.ServiceType == typeof(IHostedService) && item.ImplementationType is { } type && (type == typeof(ConversionWorker) || type == typeof(MaintenanceWorker))).ToList()) services.Remove(descriptor);
            });
        }
        public async Task<StaffAccount> AccountAsync(string username)
        {
            await using var scope = Services.CreateAsyncScope(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SignageDbContext>>();
            await using var db = await factory.CreateDbContextAsync(); return await db.StaffAccounts.AsNoTracking().SingleAsync(item => item.NormalizedLoginName == username + "@example.test");
        }
        public async Task<int> CountStaffAsync()
        {
            await using var scope = Services.CreateAsyncScope(); await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<SignageDbContext>>().CreateDbContextAsync(); return await db.StaffAccounts.CountAsync();
        }
        public async Task<int> CountQueuedAsync()
        {
            await using var scope = Services.CreateAsyncScope(); await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<SignageDbContext>>().CreateDbContextAsync(); return await db.ConversionJobs.CountAsync();
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing); SqliteConnection.ClearAllPools();
            TestDataCleanup.DeleteDirectory(root);
        }
    }
}
