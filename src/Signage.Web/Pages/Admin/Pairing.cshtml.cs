using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Signage.Domain;
using Signage.Infrastructure.Persistence;
using Signage.Web.Services;

namespace Signage.Web.Pages.Admin;

[Authorize(Policy = "SignageAdmin")]
public sealed class PairingModel(PairingService pairingService, IDbContextFactory<SignageDbContext> dbFactory) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public IReadOnlyList<ScreenGroup> Groups { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public async Task OnGetAsync(CancellationToken token) => await LoadAsync(token);
    public async Task<IActionResult> OnPostAsync(CancellationToken token)
    {
        await LoadAsync(token);
        if (!ModelState.IsValid) return Page();
        try
        {
            await pairingService.ApproveAsync(Input.Code!, Input.DeviceName!, Input.ScreenGroupId, User.FindFirstValue("sub")!, token);
            Message = "Device approved. It will begin downloading its assigned content shortly.";
            ModelState.Clear(); Input = new(); return Page();
        }
        catch (InvalidOperationException exception) { Message = exception.Message; IsError = true; return Page(); }
    }
    private async Task LoadAsync(CancellationToken token) { await using var db = await dbFactory.CreateDbContextAsync(token); Groups = await db.ScreenGroups.AsNoTracking().Where(item => !item.IsArchived).OrderBy(item => item.Name).ToListAsync(token); }
    public sealed class InputModel { [Required, RegularExpression("^[0-9]{6}$")] public string? Code { get; set; } [Required, StringLength(160)] public string? DeviceName { get; set; } [Required] public Guid ScreenGroupId { get; set; } }
}
