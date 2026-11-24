using MDel.Application.Abstractions;
using MDel.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MDel.Api.Controllers;

/// <summary>Căutare utilizatori în AD (autocomplete pentru „De la”, „Către”, grupul de informare).</summary>
[ApiController]
[Authorize(Policy = AppPolicies.DirectorySearch)]
[Route("api/directory")]
public sealed class DirectoryController(IDirectoryService directory, ILogger<DirectoryController> logger) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<DirectoryUser>>> SearchUsers(
        [FromQuery] string q, [FromQuery] bool mailboxOnly = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Ok(Array.Empty<DirectoryUser>());

        try
        {
            var users = await directory.SearchUsersAsync(q, 25, ct);
            return Ok(mailboxOnly ? users.Where(u => u.HasMailbox).ToList() : users);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "AD search failed for '{Query}'", q);
            return Problem("Active Directory search failed. Check the AD connection in Administration.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
