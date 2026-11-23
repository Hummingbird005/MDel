using MDel.Application.Abstractions;
using MDel.Domain.Entities;
using MDel.Infrastructure.Delegations;
using MDel.Infrastructure.Localization;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MDel.Infrastructure.Notifications;

public sealed record InformGroupMemberDto(int Id, UserRef User, string AddedBy, DateTime AddedUtc);

/// <summary>Grupul de informare: utilizatori AD adăugați din administrare, notificați la toate evenimentele.</summary>
public sealed class InformGroupService(
    MDelDbContext db,
    IDirectoryService directory,
    UserProvisioningService users,
    ICurrentUser currentUser,
    IAuditService audit,
    TimeProvider time)
{
    public async Task<IReadOnlyList<InformGroupMemberDto>> ListAsync(CancellationToken ct) =>
        await db.InformGroupMembers.AsNoTracking()
            .OrderBy(m => m.User.DisplayName)
            .Select(m => new InformGroupMemberDto(
                m.Id,
                new UserRef(m.User.Id, m.User.SamAccountName, m.User.DisplayName, m.User.Email),
                m.AddedBy.DisplayName,
                m.AddedUtc))
            .ToListAsync(ct);

    public async Task AddAsync(string samAccountName, CancellationToken ct)
    {
        var adUser = await directory.FindBySamAccountNameAsync(samAccountName, ct);
        if (adUser is null)
            throw new DelegationValidationException(new Dictionary<string, string[]> { ["samAccountName"] = [Messages.Get("UserNotFound")] });
        if (string.IsNullOrWhiteSpace(adUser.Email))
            throw new DelegationValidationException(new Dictionary<string, string[]> { ["samAccountName"] = [Messages.Get("UserNoMailbox")] });

        var user = await users.EnsureUserAsync(adUser.SamAccountName, isLogin: false, ct);
        if (await db.InformGroupMembers.AnyAsync(m => m.UserId == user.Id, ct)) return; // deja membru

        db.InformGroupMembers.Add(new InformGroupMember
        {
            UserId = user.Id,
            AddedById = currentUser.UserId ?? throw new InvalidOperationException("User not provisioned."),
            AddedUtc = time.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(new AuditEntry("InformGroup.MemberAdded", nameof(AppUser), user.Id.ToString(),
            NewValues: new { user.SamAccountName, user.Email }), ct);
    }

    public async Task<bool> RemoveAsync(int memberId, CancellationToken ct)
    {
        var member = await db.InformGroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId, ct);
        if (member is null) return false;

        db.InformGroupMembers.Remove(member);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(new AuditEntry("InformGroup.MemberRemoved", nameof(AppUser), member.UserId.ToString(),
            OldValues: new { member.User.SamAccountName, member.User.Email }), ct);
        return true;
    }
}
