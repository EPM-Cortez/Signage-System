using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Signage.Domain;
using Signage.Infrastructure.Persistence;
using Signage.Web.Services;

namespace Signage.Web.Pages.Admin;

[Authorize(Policy = "SignageAdmin")]
public sealed class PresentationsModel(IDbContextFactory<SignageDbContext> dbFactory, PresentationService service, TimeProvider timeProvider) : PageModel
{
    public IReadOnlyList<PresentationVersion> Versions { get; private set; } = [];
    public IReadOnlyList<PresentationEntry> Presentations { get; private set; } = [];
    public IReadOnlyList<ScreenGroup> Groups { get; private set; } = [];
    public ILookup<Guid, string> OnScreen { get; private set; } = Array.Empty<string>().ToLookup(_ => Guid.Empty);
    public DateTimeOffset Now { get; private set; }
    public async Task OnGetAsync(CancellationToken token) => await LoadAsync(token);
    public async Task<IActionResult> OnPostRetryAsync(Guid versionId, CancellationToken token) { await service.RetryAsync(versionId, User.FindFirstValue("sub")!, token); TempData["Message"] = "Conversion retry queued."; return RedirectToPage(); }
    public async Task<IActionResult> OnPostRollbackAsync(Guid versionId, Guid screenGroupId, CancellationToken token) { await service.PublishReadyAsync(versionId, screenGroupId, User.FindFirstValue("sub")!, token); TempData["Message"] = "Ready version published."; return RedirectToPage(); }
    private async Task LoadAsync(CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        Now = timeProvider.GetUtcNow();
        Versions = await db.PresentationVersions.AsNoTracking().Include(item => item.Presentation).OrderByDescending(item => item.CreatedUtc).Take(200).ToListAsync(token);
        Groups = await db.ScreenGroups.AsNoTracking().Where(item => !item.IsArchived).OrderBy(item => item.Name).ToListAsync(token);
        Presentations = Versions
            .GroupBy(item => item.PresentationId)
            .Select(group => new PresentationEntry(group.First().Presentation, group.OrderByDescending(item => item.VersionNumber).ToList()))
            .ToList();

        var publications = await db.Publications.AsNoTracking().Include(item => item.ScreenGroup)
            .Where(item => item.IsEnabled && !item.ScreenGroup.IsArchived).ToListAsync(token);
        OnScreen = publications
            .GroupBy(item => item.ScreenGroupId)
            .Select(group => PublicationRules.SelectActive(group, Now))
            .OfType<Publication>()
            .OrderBy(item => item.ScreenGroup.Name)
            .ToLookup(item => item.PresentationVersionId, item => item.ScreenGroup.Name);
    }

    public static VersionDiagnostics? ParseDiagnostics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new VersionDiagnostics([], [], null, null, null, null, json);
            return new VersionDiagnostics(
                Strings(root, "warnings"),
                Strings(root, "fonts"),
                root.TryGetProperty("renderer", out var renderer) && renderer.ValueKind == JsonValueKind.String ? renderer.GetString() : null,
                root.TryGetProperty("exitCode", out var exitCode) && exitCode.TryGetInt32(out var code) ? code : null,
                root.TryGetProperty("elapsedMs", out var elapsed) && elapsed.TryGetInt64(out var milliseconds) ? milliseconds : null,
                root.TryGetProperty("standardOutput", out var output) && output.ValueKind == JsonValueKind.String ? output.GetString() : null,
                null);
        }
        catch (JsonException)
        {
            return new VersionDiagnostics([], [], null, null, null, null, json);
        }

        static IReadOnlyList<string> Strings(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList()
                : [];
    }

    public sealed record PresentationEntry(Presentation Presentation, IReadOnlyList<PresentationVersion> Versions)
    {
        public PresentationVersion Latest => Versions[0];
        public PresentationVersion? Showcase => Versions.FirstOrDefault(item => item.Id == Presentation.CurrentVersionId && item.Status == PresentationVersionStatus.Ready)
            ?? Versions.FirstOrDefault(item => item.Status == PresentationVersionStatus.Ready);
    }

    public sealed record VersionDiagnostics(
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Fonts,
        string? Renderer,
        int? ExitCode,
        long? ElapsedMs,
        string? Output,
        string? Raw);
}
