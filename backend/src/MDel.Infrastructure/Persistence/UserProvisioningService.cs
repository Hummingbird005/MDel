using MDel.Application.Abstractions;
using MDel.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDel.Infrastructure.Persistence;

/// <summary>Creează sau actualizează înregistrarea locală (AppUsers) a unui utilizator AD.</summary>
public sealed class UserProvisioningService(MDelDbContext db, IDirectoryService directory)
{
    public async Task<AppUser> EnsureUserAsync(string loginName, bool isLogin, CancellationToken ct)
    {
        var sam = NormalizeSam(loginName);
        var user = await db.Users.FirstOrDefaultAsync(u => u.SamAccountName == sam, ct);
        var adUser = await directory.FindBySamAccountNameAsync(sam, ct);

        if (user is null)
        {
            user = new AppUser
            {
                SamAccountName = sam,
                DisplayName = adUser?.DisplayName ?? sam,
                CreatedUtc = DateTime.UtcNow,
            };
            db.Users.Add(user);
        }

        if (adUser is not null)
        {
            user.AdObjectId = adUser.ObjectId;
            user.DisplayName = adUser.DisplayName;
            user.Email = adUser.Email;
        }

        if (isLogin)
        {
            user.LastLoginUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>„DOMAIN\user” sau „user@domain” → „user” (lowercase).</summary>
    public static string NormalizeSam(string loginName)
    {
        var name = loginName;
        var slash = name.IndexOf('\\');
        if (slash >= 0) name = name[(slash + 1)..];
        var at = name.IndexOf('@');
        if (at >= 0) name = name[..at];
        return name.Trim().ToLowerInvariant();
    }
}
