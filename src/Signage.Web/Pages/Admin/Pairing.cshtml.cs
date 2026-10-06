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
    // Value of the "New group…" option in the group list.
    public const string NewGroupOption = "new";
    [BindProperty] public InputModel Input { get; set; } = new();
    public IReadOnlyList<ScreenGroup> Groups { get; private set; } = [];
    public bool CreatingGroup => Input.ScreenGroupId == NewGroupOption;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public async Task OnGetAsync(CancellationToken token) => await LoadAsync(token);
    public async Task<IActionResult> OnPostAsync(CancellationToken token)
    {
        var groupId = Guid.Empty;
        if (CreatingGroup && string.IsNullOrWhiteSpace(Input.NewGroupName)) ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.NewGroupName)}", "Enter a name for the new group.");
        else if (!CreatingGroup && Input.ScreenGroupId is not null && !Guid.TryParse(Input.ScreenGroupId, out groupId)) ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.ScreenGroupId)}", "Select a screen group.");
        if (!ModelState.IsValid) { await LoadAsync(token); return Page(); }
        try
        {
            var actor = User.FindFirstValue("sub")!;
            if (CreatingGroup)
            {
                await pairingService.ApproveIntoNewGroupAsync(Input.Code!, Input.DeviceName!, Input.NewGroupName!, actor, token);
                Message = $"Device approved and added to the new group {Input.NewGroupName!.Trim()}. It will begin downloading its assigned content shortly.";
            }
            else
            {
                await pairingService.ApproveAsync(Input.Code!, Input.DeviceName!, groupId, actor, token);
                Message = "Device approved. It will begin downloading its assigned content shortly.";
            }
            ModelState.Clear(); Input = new();
        }
        catch (InvalidOperationException exception) { Message = exception.Message; IsError = true; }
        // Loaded after approving so a group created just now is in the list.
        await LoadAsync(token); return Page();
    }
    private async Task LoadAsync(CancellationToken token) { await using var db = await dbFactory.CreateDbContextAsync(token); Groups = await db.ScreenGroups.AsNoTracking().Where(item => !item.IsArchived).OrderBy(item => item.Name).ToListAsync(token); }
    public sealed class InputModel { [Required, RegularExpression("^[0-9]{6}$")] public string? Code { get; set; } [Required, StringLength(160)] public string? DeviceName { get; set; } [Required(ErrorMessage = "Select a screen group.")] public string? ScreenGroupId { get; set; } [StringLength(120)] public string? NewGroupName { get; set; } }
}
