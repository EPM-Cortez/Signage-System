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
public sealed class PresentationsModel(IDbContextFactory<SignageDbContext> dbFactory, PresentationService service, PresentationLibraryService library, TimeProvider timeProvider) : PageModel
{
    [BindProperty(SupportsGet = true)] public bool Archived { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? FolderId { get; set; }
    [BindProperty(SupportsGet = true)] public bool Unfiled { get; set; }
    public int ActiveCount { get; private set; }
    public int ArchivedCount { get; private set; }
    public int UnfiledCount { get; private set; }
    public IReadOnlyList<PresentationFolder> Folders { get; private set; } = [];
    public IReadOnlyDictionary<Guid, int> FolderCounts { get; private set; } = new Dictionary<Guid, int>();
    public PresentationFolder? SelectedFolder => Folders.FirstOrDefault(item => item.Id == FolderId);
    public IReadOnlyList<PresentationVersion> Versions { get; private set; } = [];
    public IReadOnlyList<PresentationEntry> Presentations { get; private set; } = [];
    public IReadOnlyList<ScreenGroup> Groups { get; private set; } = [];
    public ILookup<Guid, string> OnScreen { get; private set; } = Array.Empty<string>().ToLookup(_ => Guid.Empty);
    public DateTimeOffset Now { get; private set; }
    public async Task OnGetAsync(CancellationToken token) => await LoadAsync(token);
    public Task<IActionResult> OnPostRetryAsync(Guid versionId, CancellationToken token) => ManageAsync(() => service.RetryAsync(versionId, Actor, token), "Conversion retry queued.");
    public Task<IActionResult> OnPostRollbackAsync(Guid versionId, Guid screenGroupId, CancellationToken token) => ManageAsync(() => service.PublishReadyAsync(versionId, screenGroupId, Actor, token), "Ready version published.");
    public Task<IActionResult> OnPostArchiveAsync(Guid presentationId, CancellationToken token) => ManageAsync(() => library.ArchiveAsync(presentationId, true, Actor, token), "Presentation archived and removed from screen schedules. Its files have been kept.");
    public Task<IActionResult> OnPostRestoreAsync(Guid presentationId, CancellationToken token) => ManageAsync(() => library.ArchiveAsync(presentationId, false, Actor, token), "Presentation restored. Publish it when you want it back on screens.");
    public Task<IActionResult> OnPostDeleteAsync(Guid presentationId, bool confirmed, CancellationToken token) => confirmed
        ? ManageAsync(() => library.DeleteAsync(presentationId, Actor, token), "Presentation and its versions deleted. Stored files are being removed.")
        : Task.FromResult<IActionResult>(BadRequest("Confirm deletion before continuing."));
    public Task<IActionResult> OnPostMoveAsync(Guid presentationId, Guid? targetFolderId, CancellationToken token) => ManageAsync(() => library.MoveAsync(presentationId, targetFolderId, Actor, token), "Presentation moved.");
    public async Task<IActionResult> OnPostCreateFolderAsync(string? folderName, CancellationToken token)
    {
        try { FolderId = await library.SaveFolderAsync(null, folderName, Actor, token); Unfiled = false; TempData["Message"] = "Folder created."; }
        catch (InvalidOperationException exception) { TempData["Error"] = exception.Message; }
        catch (DbUpdateException) { TempData["Error"] = "A folder with that name already exists."; }
        return ReturnToLibrary();
    }
    public Task<IActionResult> OnPostRenameFolderAsync(Guid id, string? folderName, CancellationToken token) => ManageAsync(async () => { await library.SaveFolderAsync(id, folderName, Actor, token); }, "Folder renamed.");
    public Task<IActionResult> OnPostDeleteFolderAsync(Guid id, bool confirmed, CancellationToken token) => confirmed
        ? ManageAsync(async () => { await library.DeleteFolderAsync(id, Actor, token); if (FolderId == id) FolderId = null; }, "Folder removed. Its presentations are now unfiled.")
        : Task.FromResult<IActionResult>(BadRequest("Confirm folder removal before continuing."));
    private string Actor => User.FindFirstValue("sub")!;
    private IActionResult ReturnToLibrary() => RedirectToPage(new { archived = Archived, folderId = FolderId, unfiled = Unfiled });
    private async Task<IActionResult> ManageAsync(Func<Task> action, string message)
    {
        try { await action(); TempData["Message"] = message; }
        catch (InvalidOperationException exception) { TempData["Error"] = exception.Message; }
        catch (DbUpdateException) { TempData["Error"] = "The change could not be saved. Folder names must be unique."; }
        return ReturnToLibrary();
    }
    private async Task LoadAsync(CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        Now = timeProvider.GetUtcNow();
        ActiveCount = await db.Presentations.CountAsync(item => item.ArchivedUtc == null, token);
        ArchivedCount = await db.Presentations.CountAsync(item => item.ArchivedUtc != null, token);
        Folders = await db.PresentationFolders.AsNoTracking().OrderBy(item => item.Name).ToListAsync(token);
        var query = db.Presentations.AsNoTracking().Where(item => Archived ? item.ArchivedUtc != null : item.ArchivedUtc == null);
        UnfiledCount = await query.CountAsync(item => item.FolderId == null, token);
        FolderCounts = await query.Where(item => item.FolderId != null).GroupBy(item => item.FolderId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() }).ToDictionaryAsync(item => item.Id, item => item.Count, token);
        if (FolderId is not null) query = query.Where(item => item.FolderId == FolderId);
        else if (Unfiled) query = query.Where(item => item.FolderId == null);
        var presentations = await query.Include(item => item.Folder).Include(item => item.Versions).AsSplitQuery().OrderByDescending(item => item.CreatedUtc).ToListAsync(token);
        Versions = presentations.SelectMany(item => item.Versions).ToList();
        Groups = await db.ScreenGroups.AsNoTracking().Where(item => !item.IsArchived).OrderBy(item => item.Name).ToListAsync(token);
        Presentations = presentations.Where(item => item.Versions.Count > 0)
            .Select(item => new PresentationEntry(item, item.Versions.OrderByDescending(version => version.VersionNumber).ToList())).ToList();

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
