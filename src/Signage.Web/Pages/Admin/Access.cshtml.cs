using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;
using Signage.Web.Services;

namespace Signage.Web.Pages.Admin;

[Authorize(Policy = "SignageAdmin")]
public sealed class AccessModel(IDbContextFactory<SignageDbContext> dbFactory, StaffAccessService staff, IOptions<ActiveDirectoryOptions> directory) : PageModel
{
    [BindProperty] public AccessInput Input { get; set; } = new();
    [TempData] public string? Message { get; set; }
    public IReadOnlyList<StaffAccount> People { get; private set; } = [];
    public IReadOnlyList<ScreenGroup> Groups { get; private set; } = [];
    public IReadOnlyList<PublisherAccess> Access { get; private set; } = [];
    public IReadOnlyList<StaffAccount> Administrators => People.Where(item => item.Role == StaffRole.Administrator).ToList();
    // Accounts awaiting approval sort first so they stand out.
    public IReadOnlyList<StaffAccount> StaffMembers => People.Where(item => item.Role != StaffRole.Administrator).OrderBy(item => item.Role != StaffRole.Pending).ToList();
    /// <summary>The row to show expanded, so a failed save stays in view.</summary>
    public Guid? OpenId { get; private set; }
    public bool DirectoryConfigured => directory.Value.IsConfigured;
    public Task OnGetAsync(CancellationToken token) => LoadAsync(token);

    public async Task<IActionResult> OnPostAddAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(Input.Username)) ModelState.AddModelError(string.Empty, "Enter a school username.");
        if (!ModelState.IsValid) { await LoadAsync(token); return Page(); }
        try
        {
            await staff.AddDirectoryStaffAsync(Input.Username!, Input.Role, Input.IsEnabled, Input.ScreenGroupIds, Actor, token);
            Message = "Staff account added. Its AD identity will be verified and linked on first sign-in.";
            return RedirectToPage();
        }
        catch (ArgumentException exception) { ModelState.AddModelError(string.Empty, exception.Message); await LoadAsync(token); return Page(); }
    }

    public async Task<IActionResult> OnPostSaveAsync(Guid id, CancellationToken token)
    {
        OpenId = id;
        if (!ModelState.IsValid) { await LoadAsync(token); return Page(); }
        try
        {
            await staff.UpdateAsync(id, Input.Role, Input.IsEnabled, Input.ScreenGroupIds, Actor, token);
            Message = "Permissions saved. They take effect on the staff member's next request.";
            return RedirectToPage();
        }
        catch (ArgumentException exception) { ModelState.AddModelError(string.Empty, exception.Message); await LoadAsync(token); return Page(); }
    }

    private string Actor => User.FindFirstValue("sub")!;
    private async Task LoadAsync(CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        People = await db.StaffAccounts.AsNoTracking().OrderBy(item => item.DisplayName).ToListAsync(token);
        Groups = await db.ScreenGroups.AsNoTracking().Where(item => !item.IsArchived).OrderBy(item => item.Name).ToListAsync(token);
        Access = await db.PublisherAccess.AsNoTracking().ToListAsync(token);
    }

    public sealed class AccessInput
    {
        [StringLength(300)] public string? Username { get; set; }
        public StaffRole Role { get; set; } = StaffRole.Teacher;
        public bool IsEnabled { get; set; } = true;
        public List<Guid> ScreenGroupIds { get; set; } = [];
    }
}
