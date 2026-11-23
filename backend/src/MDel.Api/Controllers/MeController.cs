using MDel.Application.Abstractions;
using MDel.Application.Common;
using MDel.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDel.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me")]
public sealed class MeController(MDelDbContext db, ICurrentUser currentUser, IAuditService audit) : ControllerBase
{
    public sealed record CurrentUserDto(
        string SamAccountName,
        string DisplayName,
        string? Email,
        IReadOnlyList<string> Roles,
        string PreferredLanguage);

    public sealed record SetLanguageRequest(string Language);

    private static readonly string[] SupportedLanguages = ["ro", "en"];

    [HttpGet]
    public async Task<ActionResult<CurrentUserDto>> Get(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct);
        if (user is null) return Unauthorized();

        var roles = new[] { AppRoles.Admin, AppRoles.Approver }.Where(currentUser.IsInRole).ToList();
        return new CurrentUserDto(user.SamAccountName, user.DisplayName, user.Email, roles, user.PreferredLanguage);
    }

    [HttpPut("language")]
    public async Task<IActionResult> SetLanguage(SetLanguageRequest request, CancellationToken ct)
    {
        var language = request.Language?.Trim().ToLowerInvariant();
        if (language is null || !SupportedLanguages.Contains(language))
        {
            return ValidationProblem(detail: "Language must be 'ro' or 'en'.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct);
        if (user is null) return Unauthorized();

        var old = user.PreferredLanguage;
        if (old != language)
        {
            user.PreferredLanguage = language;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry(AuditActions.LanguageChanged, "AppUser", user.Id.ToString(),
                OldValues: new { PreferredLanguage = old }, NewValues: new { PreferredLanguage = language }), ct);
        }

        return NoContent();
    }
}
