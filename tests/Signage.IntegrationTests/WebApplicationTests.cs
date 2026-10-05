using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Signage.IntegrationTests;

public sealed class WebApplicationTests : IDisposable
{
    private readonly TestApplication factory = new();

    [Fact]
    public async Task Development_login_reaches_teacher_publish_page()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true, HandleCookies = true });
        var response = await client.GetAsync("/dev-login?asRole=teacher");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Publish to screens", html);
        Assert.Contains("Development sign-in", html);
    }

    [Fact]
    public async Task Player_can_create_a_short_lived_pairing_session()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/player/pairing-sessions", null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PairingResponse>();
        Assert.NotNull(result);
        Assert.Matches("^[0-9]{6}$", result.Code);
        Assert.NotEqual(result.Code, result.TemporaryToken);
        Assert.True(result.ExpiresUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task YouTube_permissions_are_scoped_to_the_player_and_worker_revalidates()
    {
        using var client = factory.CreateClient();
        var player = await client.GetAsync("/player/");
        player.EnsureSuccessStatusCode();
        Assert.Contains("https://www.youtube-nocookie.com", player.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("strict-origin-when-cross-origin", player.Headers.GetValues("Referrer-Policy").Single());
        var human = await client.GetAsync("/dev-login?asRole=teacher");
        Assert.DoesNotContain("youtube", human.Headers.GetValues("Content-Security-Policy").Single());
        var worker = await client.GetAsync("/player/sw.js");
        Assert.True(worker.Headers.CacheControl!.NoCache);
    }

    public void Dispose() => factory.Dispose();
    private sealed record PairingResponse(Guid SessionId, string TemporaryToken, string Code, DateTimeOffset ExpiresUtc);

    private sealed class TestApplication : WebApplicationFactory<Program>
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"signage-web-tests-{Guid.NewGuid():N}");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(root);
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SignageDb"] = $"Data Source={Path.Combine(root, "signage.db")};Foreign Keys=True;Default Timeout=5",
                ["Database:InstanceLockPath"] = Path.Combine(root, "signage.lock"),
                ["Storage:RootPath"] = Path.Combine(root, "content"),
                ["Storage:MinimumFreeBytes"] = "0",
                ["Rendering:TempRoot"] = Path.Combine(root, "temp"),
                ["Rendering:ConverterEntryPoint"] = Path.GetFullPath("src/Signage.Converter/dist/cli.js"),
                ["Rendering:WorkingDirectory"] = Path.GetFullPath("src/Signage.Converter"),
                ["Rendering:FontDirectories:0"] = null,
                ["Authentication:Mode"] = "Development"
            }));
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            SqliteConnection.ClearAllPools();
            TestDataCleanup.DeleteDirectory(root);
        }
    }
}
