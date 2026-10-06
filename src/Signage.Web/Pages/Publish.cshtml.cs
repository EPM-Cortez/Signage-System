using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Signage.Domain;
using Signage.Web.Services;

namespace Signage.Web.Pages;

[Authorize(Policy = "Teacher")]
[RequestFormLimits(MultipartBodyLengthLimit = 262_144_000, MemoryBufferThreshold = 65_536)]
[RequestSizeLimit(262_144_000)]
public sealed class PublishModel(PresentationService presentationService) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public IReadOnlyList<ScreenGroup> Groups { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadGroupsAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadGroupsAsync(cancellationToken);
        if (!ModelState.IsValid) return Invalid();
        try
        {
            await using var stream = Input.File!.OpenReadStream();
            var versionId = await presentationService.UploadAsync(
                Subject,
                User.IsInRole("SignageAdmin"),
                Input.Name,
                Input.File.FileName,
                Input.File.Length,
                stream,
                Input.ScreenGroupIds,
                Input.PublishWhenReady,
                Input.StartsLocal?.ToUniversalTime(),
                Input.EndsLocal?.ToUniversalTime(),
                cancellationToken);
            return IsScriptedUpload
                ? new JsonResult(new { redirect = Url.Page("/Status", new { versionId }) })
                : RedirectToPage("/Status", new { versionId });
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or Signage.Application.PresentationRejectedException)
        {
            ModelState.AddModelError(string.Empty, exception is UnauthorizedAccessException
                ? "You cannot publish to one or more selected screen groups."
                : exception.Message);
            return Invalid();
        }
    }

    // publish.js posts the form with XMLHttpRequest to show upload progress, so it needs errors as data rather than a page.
    private bool IsScriptedUpload => Request.Headers.XRequestedWith == "XMLHttpRequest";

    private IActionResult Invalid() => IsScriptedUpload
        ? new JsonResult(new
        {
            errors = ModelState.Values
                .SelectMany(entry => entry.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Check the details and try again." : error.ErrorMessage)
                .Distinct()
        }) { StatusCode = StatusCodes.Status400BadRequest }
        : Page();

    private string Subject => User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private async Task LoadGroupsAsync(CancellationToken cancellationToken) =>
        Groups = await presentationService.GetAvailableGroupsAsync(Subject, User.IsInRole("SignageAdmin"), cancellationToken);

    public sealed class InputModel
    {
        [StringLength(200)] public string? Name { get; set; }
        [Required] public IFormFile? File { get; set; }
        [MinLength(1, ErrorMessage = "Select at least one screen group.")]
        public List<Guid> ScreenGroupIds { get; set; } = [];
        public bool PublishWhenReady { get; set; } = true;
        public DateTimeOffset? StartsLocal { get; set; }
        public DateTimeOffset? EndsLocal { get; set; }
    }
}
