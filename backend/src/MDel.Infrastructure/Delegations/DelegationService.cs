using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Domain.Entities;
using MDel.Domain.Enums;
using MDel.Infrastructure.Localization;
using MDel.Infrastructure.Notifications;
using MDel.Application.Common;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MDel.Infrastructure.Delegations;

/// <summary>Cazurile de utilizare pentru delegări: listare, detaliu, creare, modificare, oprire.</summary>
public sealed class DelegationService(
    MDelDbContext db,
    IDirectoryService directory,
    IExchangeService exchange,
    ISettingsService settings,
    UserProvisioningService users,
    ForwardingManager forwarding,
    NotificationService notifications,
    ICurrentUser currentUser,
    IAuditService audit,
    TimeProvider time)
{
    /// <summary>Toleranță pentru „acum” la data de început (ceasul clientului, timpul de completare).</summary>
    private static readonly TimeSpan StartTolerance = TimeSpan.FromMinutes(5);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ citire

    public async Task<PagedResult<DelegationListItem>> ListAsync(DelegationQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var q = Filter(query);
        var total = await q.CountAsync(ct);
        var items = await Sort(q, query).Skip((page - 1) * pageSize).Take(pageSize).Select(ListProjection).ToListAsync(ct);
        return new PagedResult<DelegationListItem>(items, total, page, pageSize);
    }

    /// <summary>Toate rezultatele filtrului (max. 10 000), pentru export.</summary>
    public async Task<IReadOnlyList<DelegationListItem>> ListAllAsync(DelegationQuery query, CancellationToken ct) =>
        await Sort(Filter(query), query).Take(10_000).Select(ListProjection).ToListAsync(ct);

    /// <summary>Activările și expirările care urmează în următoarele <paramref name="days"/> zile.</summary>
    public async Task<IReadOnlyList<UpcomingEvent>> UpcomingAsync(int days, CancellationToken ct)
    {
        var now = Now;
        var until = now.AddDays(Math.Clamp(days, 1, 31));

        var activations = await db.Delegations.AsNoTracking()
            .Where(d => (d.Status == DelegationStatus.Scheduled || d.Status == DelegationStatus.PendingApproval) &&
                        d.StartUtc >= now && d.StartUtc <= until)
            .Select(ListProjection).ToListAsync(ct);
        var expirations = await db.Delegations.AsNoTracking()
            .Where(d => d.Status == DelegationStatus.Active && d.EndUtc >= now && d.EndUtc <= until)
            .Select(ListProjection).ToListAsync(ct);

        return activations.Select(d => new UpcomingEvent(UpcomingEventType.Activation, d.StartUtc, d))
            .Concat(expirations.Select(d => new UpcomingEvent(UpcomingEventType.Expiration, d.EndUtc, d)))
            .OrderBy(e => e.AtUtc)
            .Take(50)
            .ToList();
    }

    private IQueryable<Delegation> Filter(DelegationQuery query)
    {
        var q = db.Delegations.AsNoTracking();
        if (query.Statuses is { Length: > 0 } statuses)
        {
            q = q.Where(d => statuses.Contains(d.Status));
        }

        if (query.KeepCopy is { } keepCopy)
        {
            q = q.Where(d => d.KeepCopyInSource == keepCopy);
        }

        if (query.FromUtc is { } fromValue)
        {
            var from = ToUtc(fromValue);
            q = q.Where(d => d.EndUtc > from);
        }

        if (query.ToUtc is { } toValue)
        {
            var to = ToUtc(toValue);
            q = q.Where(d => d.StartUtc < to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = int.TryParse(s.TrimStart('#'), out var id)
                ? q.Where(d => d.Id == id)
                : q.Where(d => d.FromUser.DisplayName.Contains(s) || d.FromUser.SamAccountName.Contains(s) ||
                               (d.FromUser.Email != null && d.FromUser.Email.Contains(s)) ||
                               d.ToUser.DisplayName.Contains(s) || d.ToUser.SamAccountName.Contains(s) ||
                               (d.ToUser.Email != null && d.ToUser.Email.Contains(s)));
        }

        return q;
    }

    private static IQueryable<Delegation> Sort(IQueryable<Delegation> q, DelegationQuery query) => (query.Sort, query.Descending) switch
    {
        (DelegationSort.Start, false) => q.OrderBy(d => d.StartUtc).ThenBy(d => d.Id),
        (DelegationSort.Start, true) => q.OrderByDescending(d => d.StartUtc).ThenByDescending(d => d.Id),
        (DelegationSort.End, false) => q.OrderBy(d => d.EndUtc).ThenBy(d => d.Id),
        (DelegationSort.End, true) => q.OrderByDescending(d => d.EndUtc).ThenByDescending(d => d.Id),
        (DelegationSort.Created, false) => q.OrderBy(d => d.CreatedUtc).ThenBy(d => d.Id),
        (DelegationSort.Created, true) => q.OrderByDescending(d => d.CreatedUtc).ThenByDescending(d => d.Id),
        (DelegationSort.Id, false) => q.OrderBy(d => d.Id),
        (DelegationSort.Id, true) => q.OrderByDescending(d => d.Id),
        // Implicit: programatele – cele mai apropiate primele; restul – cele mai recente primele.
        _ when query.Statuses is [DelegationStatus.Scheduled] => q.OrderBy(d => d.StartUtc).ThenBy(d => d.Id),
        _ => q.OrderByDescending(d => d.StartUtc).ThenByDescending(d => d.Id),
    };

    public async Task<IReadOnlyDictionary<DelegationStatus, int>> SummaryAsync(CancellationToken ct)
    {
        var counts = await db.Delegations.AsNoTracking()
            .GroupBy(d => d.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Enum.GetValues<DelegationStatus>()
            .ToDictionary(s => s, s => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0);
    }

    public async Task<DelegationDetail?> GetAsync(int id, CancellationToken ct)
    {
        var d = await db.Delegations.AsNoTracking().AsSplitQuery()
            .Include(x => x.FromUser).Include(x => x.ToUser).Include(x => x.CreatedBy)
            .Include(x => x.ApprovedBy).Include(x => x.StoppedBy)
            .Include(x => x.Attachments).ThenInclude(a => a.UploadedBy)
            .Include(x => x.Notifications).ThenInclude(n => n.RecipientUser)
            .Include(x => x.StatusHistory).ThenInclude(h => h.ChangedBy)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return d is null ? null : ToDetail(d);
    }

    public async Task<DelegationFormConfig> GetFormConfigAsync(CancellationToken ct) => new(
        await settings.GetIntAsync(SettingKeys.AttachmentsMaxSizeMb, 100, ct),
        AttachmentService.ParseExtensions(await settings.GetAsync(SettingKeys.AttachmentsAllowedExtensions, ct)),
        await settings.GetBoolAsync(SettingKeys.ExchangeKeepCopyInSource, true, ct));

    /// <summary>Informații pentru formular: forwardul existent pe sursă și delegările deschise.</summary>
    public async Task<SourceCheckResult> CheckSourceAsync(string fromSam, CancellationToken ct)
    {
        var sam = UserProvisioningService.NormalizeSam(fromSam);
        var open = await db.Delegations.AsNoTracking()
            .Where(d => d.FromUser.SamAccountName == sam && DelegationStateMachine.OpenStatuses.Contains(d.Status))
            .OrderBy(d => d.StartUtc).Select(ListProjection).ToListAsync(ct);

        MailboxForwarding? current = null;
        string? error = null;
        try
        {
            var adUser = await directory.FindBySamAccountNameAsync(sam, ct);
            if (adUser is not null)
            {
                current = await exchange.GetForwardingAsync(adUser.Email ?? adUser.SamAccountName, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
        }

        return new SourceCheckResult(current, error, open);
    }

    // ------------------------------------------------------------------ scriere

    public async Task<int> CreateAsync(SaveDelegationRequest request, CancellationToken ct)
    {
        try
        {
            return await CreateCoreAsync(request, ct);
        }
        catch (Exception ex) when (IsRejection(ex))
        {
            await AuditRejectedAsync("Delegation.CreateFailed", null, Attempt(request), ex, ct);
            throw;
        }
    }

    private async Task<int> CreateCoreAsync(SaveDelegationRequest request, CancellationToken ct)
    {
        var (fromAd, toAd) = await ValidateAsync(request, existingId: null, isCreate: true, ct);

        var from = await users.EnsureUserAsync(fromAd.SamAccountName, isLogin: false, ct);
        var to = await users.EnsureUserAsync(toAd.SamAccountName, isLogin: false, ct);
        var now = Now;

        var d = new Delegation
        {
            FromUserId = from.Id,
            FromUser = from,
            ToUserId = to.Id,
            ToUser = to,
            StartUtc = MaxDate(ToUtc(request.StartUtc), now),
            EndUtc = ToUtc(request.EndUtc),
            RequiresApproval = request.RequiresApproval,
            KeepCopyInSource = await settings.GetBoolAsync(SettingKeys.ExchangeKeepCopyInSource, true, ct),
            Notes = NullIfEmpty(request.Notes),
            CreatedById = RequireUserId(),
            CreatedUtc = now,
            Status = request.RequiresApproval ? DelegationStatus.PendingApproval : DelegationStatus.Scheduled,
        };
        d.StatusHistory.Add(new DelegationStatusHistory
        {
            ToStatus = d.Status,
            ChangedById = d.CreatedById,
            ChangedUtc = now,
            Reason = request.RequiresApproval ? null : Messages.Get("CreatedWithoutApproval"),
        });

        db.Delegations.Add(d);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(new AuditEntry("Delegation.Created", nameof(Delegation), d.Id.ToString(),
            NewValues: Snapshot(d)), ct);

        // Cu aprobare: cerere către aprobatori. Fără aprobare: aprobatorii sunt doar informați.
        await notifications.EnqueueAsync(d, NotificationEvent.Created, d.Notes, ct);

        // Fără aprobare și cu startul atins: se activează imediat.
        if (!d.RequiresApproval && d.StartUtc <= now && !await forwarding.ApplyAsync(d, currentUser.UserId, ct))
        {
            await notifications.EnqueueAsync(d, NotificationEvent.Failed, d.LastError, ct);
        }

        await db.SaveChangesAsync(ct);
        return d.Id;
    }

    public async Task UpdateAsync(int id, SaveDelegationRequest request, CancellationToken ct)
    {
        try
        {
            await UpdateCoreAsync(id, request, ct);
        }
        catch (Exception ex) when (IsRejection(ex))
        {
            await AuditRejectedAsync("Delegation.UpdateFailed", id, Attempt(request), ex, ct);
            throw;
        }
    }

    private async Task UpdateCoreAsync(int id, SaveDelegationRequest request, CancellationToken ct)
    {
        var d = await LoadForUpdateAsync(id, request.RowVersion, ct);
        var before = Snapshot(d);
        var now = Now;

        switch (d.Status)
        {
            case DelegationStatus.PendingApproval:
            case DelegationStatus.Scheduled:
            {
                var (fromAd, toAd) = await ValidateAsync(request, d.Id, isCreate: false, ct);
                var from = await users.EnsureUserAsync(fromAd.SamAccountName, isLogin: false, ct);
                var to = await users.EnsureUserAsync(toAd.SamAccountName, isLogin: false, ct);

                d.FromUserId = from.Id;
                d.FromUser = from;
                d.ToUserId = to.Id;
                d.ToUser = to;
                d.StartUtc = MaxDate(ToUtc(request.StartUtc), now);
                d.EndUtc = ToUtc(request.EndUtc);
                d.Notes = NullIfEmpty(request.Notes);
                d.UpdatedUtc = now;

                // O delegare aprobată și modificată trebuie reaprobată.
                if (d.Status == DelegationStatus.Scheduled && d.RequiresApproval)
                {
                    d.ApprovedById = null;
                    d.ApprovedUtc = null;
                    d.TransitionTo(DelegationStatus.PendingApproval, currentUser.UserId, now, Messages.Get("EditedReapproval"));
                    await notifications.EnqueueAsync(d, NotificationEvent.Created, Messages.Get("EditedReapproval"), ct);
                }

                break;
            }

            case DelegationStatus.Active:
            {
                var sameUsers = UserProvisioningService.NormalizeSam(request.FromSam) == d.FromUser.SamAccountName &&
                                UserProvisioningService.NormalizeSam(request.ToSam) == d.ToUser.SamAccountName &&
                                ToUtc(request.StartUtc) == d.StartUtc;
                if (!sameUsers)
                {
                    throw Invalid(nameof(request.StartUtc), Messages.Get("ActiveOnlyEnd"));
                }

                var end = ToUtc(request.EndUtc);
                if (end <= now) throw Invalid(nameof(request.EndUtc), Messages.Get("EndInPast"));

                d.EndUtc = end;
                d.Notes = NullIfEmpty(request.Notes);
                d.UpdatedUtc = now;
                break;
            }

            default:
                throw new DelegationConflictException(Messages.Get("NotEditable", d.Status));
        }

        await SaveAsync(ct);
        await audit.WriteAsync(new AuditEntry("Delegation.Updated", nameof(Delegation), d.Id.ToString(),
            OldValues: before, NewValues: Snapshot(d)), ct);

        if (d.Status == DelegationStatus.Scheduled && d.StartUtc <= now)
        {
            if (!await forwarding.ApplyAsync(d, currentUser.UserId, ct))
                await notifications.EnqueueAsync(d, NotificationEvent.Failed, d.LastError, ct);
            await SaveAsync(ct);
        }
    }

    // ------------------------------------------------------------------ aprobare

    /// <summary>Un singur aprobator din grup este suficient; al doilea primește conflict.</summary>
    public async Task ApproveAsync(int id, string? rowVersion, CancellationToken ct)
    {
        try
        {
            await ApproveCoreAsync(id, rowVersion, ct);
        }
        catch (Exception ex) when (IsRejection(ex))
        {
            await AuditRejectedAsync("Delegation.ApproveFailed", id, null, ex, ct);
            throw;
        }
    }

    private async Task ApproveCoreAsync(int id, string? rowVersion, CancellationToken ct)
    {
        var d = await LoadForUpdateAsync(id, rowVersion, ct);
        await EnsureCanDecideAsync(d, ct);

        var now = Now;
        if (d.EndUtc <= now) throw new DelegationConflictException(Messages.Get("ApprovalTooLate"));

        var statusBefore = d.Status;
        d.ApprovedById = RequireUserId();
        d.ApprovedUtc = now;
        d.TransitionTo(DelegationStatus.Scheduled, d.ApprovedById, now, Messages.Get("Approved"));
        await SaveAsync(ct); // aici al doilea aprobator simultan primește DbUpdateConcurrencyException -> 409

        await audit.WriteAsync(new AuditEntry("Delegation.Approved", nameof(Delegation), d.Id.ToString(),
            OldValues: new { Status = statusBefore.ToString(), ApprovedBy = (string?)null },
            NewValues: new { Status = d.Status.ToString(), ApprovedBy = currentUser.UserName, ApprovedUtc = d.ApprovedUtc }), ct);
        await notifications.EnqueueAsync(d, NotificationEvent.Approved, null, ct);

        if (d.StartUtc <= now && !await forwarding.ApplyAsync(d, currentUser.UserId, ct))
        {
            await notifications.EnqueueAsync(d, NotificationEvent.Failed, d.LastError, ct);
        }

        await SaveAsync(ct);
    }

    public async Task RejectAsync(int id, string reason, string? rowVersion, CancellationToken ct)
    {
        try
        {
            await RejectCoreAsync(id, reason, rowVersion, ct);
        }
        catch (Exception ex) when (IsRejection(ex))
        {
            await AuditRejectedAsync("Delegation.RejectFailed", id, new { RejectReason = reason }, ex, ct);
            throw;
        }
    }

    private async Task RejectCoreAsync(int id, string reason, string? rowVersion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw Invalid(nameof(reason), Messages.Get("ReasonRequired"));

        var d = await LoadForUpdateAsync(id, rowVersion, ct);
        await EnsureCanDecideAsync(d, ct);

        var rejectedFrom = d.Status;
        reason = reason.Trim();
        d.RejectReason = reason.Length > 1000 ? reason[..1000] : reason;
        d.TransitionTo(DelegationStatus.Rejected, RequireUserId(), Now, d.RejectReason);
        await notifications.EnqueueAsync(d, NotificationEvent.Rejected, d.RejectReason, ct);
        await SaveAsync(ct);

        await audit.WriteAsync(new AuditEntry("Delegation.Rejected", nameof(Delegation), d.Id.ToString(),
            OldValues: new { Status = rejectedFrom.ToString(), RejectReason = (string?)null },
            NewValues: new { Status = d.Status.ToString(), d.RejectReason }), ct);
    }

    private async Task EnsureCanDecideAsync(Delegation d, CancellationToken ct)
    {
        if (d.Status != DelegationStatus.PendingApproval)
            throw new DelegationConflictException(Messages.Get("AlreadyProcessed", d.Status));

        if (d.CreatedById == currentUser.UserId &&
            await settings.GetBoolAsync(SettingKeys.PreventSelfApproval, true, ct))
            throw new DelegationConflictException(Messages.Get("SelfApproval"));
    }

    public async Task<DelegationStats> StatsAsync(CancellationToken ct)
    {
        var now = Now;
        var byStatus = await SummaryAsync(ct);
        var expiringSoon = await db.Delegations.CountAsync(d =>
            d.Status == DelegationStatus.Active && d.EndUtc <= now.AddDays(7), ct);
        var expired30 = await db.Delegations.CountAsync(d =>
            d.Status == DelegationStatus.Expired && d.EndUtc >= now.AddDays(-30), ct);
        return new DelegationStats(byStatus, expiringSoon, expired30);
    }

    /// <summary>Oprire de către administrator; dacă forwardul e activ, se elimină din Exchange.</summary>
    public async Task<bool> StopAsync(int id, StopDelegationRequest request, CancellationToken ct)
    {
        try
        {
            return await StopCoreAsync(id, request, ct);
        }
        catch (Exception ex) when (IsRejection(ex))
        {
            await AuditRejectedAsync("Delegation.StopFailed", id, new { StopReason = request.Reason }, ex, ct);
            throw;
        }
    }

    private async Task<bool> StopCoreAsync(int id, StopDelegationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw Invalid(nameof(request.Reason), Messages.Get("ReasonRequired"));

        var d = await LoadForUpdateAsync(id, request.RowVersion, ct);
        if (!DelegationStateMachine.CanTransition(d.Status, DelegationStatus.Stopped))
        {
            throw new DelegationConflictException(Messages.Get("NotStoppable", d.Status));
        }

        var stoppedFrom = d.Status;
        var reason = request.Reason.Trim();
        d.StoppedById = RequireUserId();
        d.StoppedUtc = Now;
        d.StopReason = reason.Length > 1000 ? reason[..1000] : reason;

        var removed = await forwarding.RemoveAsync(d, DelegationStatus.Stopped, currentUser.UserId, d.StopReason, ct);
        await notifications.EnqueueAsync(d, removed ? NotificationEvent.Stopped : NotificationEvent.Failed,
            removed ? d.StopReason : d.LastError, ct);
        await SaveAsync(ct);

        await audit.WriteAsync(new AuditEntry("Delegation.Stopped", nameof(Delegation), d.Id.ToString(),
            OldValues: new { Status = stoppedFrom.ToString(), StopReason = (string?)null },
            NewValues: new { Status = d.Status.ToString(), d.StopReason, d.LastError },
            Result: removed ? AuditResult.Success : AuditResult.Failure), ct);
        return removed;
    }

    // ------------------------------------------------------------------ validare

    private async Task<(DirectoryUser From, DirectoryUser To)> ValidateAsync(
        SaveDelegationRequest r, int? existingId, bool isCreate, CancellationToken ct)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message) =>
            (errors.TryGetValue(field, out var list) ? list : errors[field] = []).Add(message);

        var from = await ResolveUserAsync(r.FromSam, nameof(r.FromSam), Add, ct);
        var to = await ResolveUserAsync(r.ToSam, nameof(r.ToSam), Add, ct);

        if (from is not null && to is not null && from.SamAccountName == to.SamAccountName)
        {
            Add(nameof(r.ToSam), Messages.Get("SameUser"));
        }
        else if (to is { HasMailbox: true })
        {
            try
            {
                if (!await exchange.IsInternalRecipientAsync(to.Email ?? to.SamAccountName, ct))
                    Add(nameof(r.ToSam), Messages.Get("NotInternal"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Add(nameof(r.ToSam), Messages.Get("CannotVerifyRecipient", ex.Message));
            }
        }

        var now = Now;
        var start = ToUtc(r.StartUtc);
        var end = ToUtc(r.EndUtc);
        if (end <= start) Add(nameof(r.EndUtc), Messages.Get("EndAfterStart"));
        if (end <= now) Add(nameof(r.EndUtc), Messages.Get("EndInPast"));
        if (isCreate && start < now - StartTolerance) Add(nameof(r.StartUtc), Messages.Get("StartInPast"));

        if (from is not null && to is not null && errors.Count == 0)
        {
            var effectiveStart = MaxDate(start, now);

            var overlap = await db.Delegations.AsNoTracking()
                .Where(d => d.Id != existingId && d.FromUser.SamAccountName == from.SamAccountName &&
                            DelegationStateMachine.OpenStatuses.Contains(d.Status) &&
                            d.StartUtc < end && effectiveStart < d.EndUtc)
                .Select(d => (int?)d.Id).FirstOrDefaultAsync(ct);
            if (overlap is { } o) Add(nameof(r.FromSam), Messages.Get("Overlap", o));

            var loop = await db.Delegations.AsNoTracking()
                .Where(d => d.Id != existingId && d.FromUser.SamAccountName == to.SamAccountName &&
                            d.ToUser.SamAccountName == from.SamAccountName &&
                            DelegationStateMachine.OpenStatuses.Contains(d.Status) &&
                            d.StartUtc < end && effectiveStart < d.EndUtc)
                .Select(d => (int?)d.Id).FirstOrDefaultAsync(ct);
            if (loop is { } l) Add(nameof(r.ToSam), Messages.Get("Loop", l));
        }

        if (errors.Count > 0)
        {
            throw new DelegationValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }

        return (from!, to!);
    }

    private async Task<DirectoryUser?> ResolveUserAsync(string? sam, string field, Action<string, string> add, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sam))
        {
            add(field, Messages.Get("UserRequired"));
            return null;
        }

        var user = await directory.FindBySamAccountNameAsync(sam.Trim(), ct);
        if (user is null) add(field, Messages.Get("UserNotFound"));
        else if (!user.HasMailbox) add(field, Messages.Get("UserNoMailbox"));
        return user is { HasMailbox: true } ? user : null;
    }

    // ------------------------------------------------------------------ utilitare

    private async Task<Delegation> LoadForUpdateAsync(int id, string? rowVersion, CancellationToken ct)
    {
        var d = await db.Delegations
            .Include(x => x.FromUser).Include(x => x.ToUser)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"Delegation {id} not found.");

        if (!string.IsNullOrEmpty(rowVersion))
        {
            byte[] expected;
            try { expected = Convert.FromBase64String(rowVersion); }
            catch (FormatException) { throw new DelegationConflictException(Messages.Get("Concurrency")); }

            if (!expected.AsSpan().SequenceEqual(d.RowVersion))
                throw new DelegationConflictException(Messages.Get("Concurrency"));

            // SQL Server verifică și la UPDATE (WHERE RowVersion = @original).
            db.Entry(d).Property(x => x.RowVersion).OriginalValue = expected;
        }

        return d;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DelegationConflictException(Messages.Get("Concurrency"));
        }
    }

    private int RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("The current user is not provisioned.");

    private static DelegationValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static DateTime MaxDate(DateTime a, DateTime b) => a > b ? a : b;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Valorile introduse de utilizator într-o cerere de creare / modificare (pentru auditul încercărilor respinse).</summary>
    private static object Attempt(SaveDelegationRequest r) => new
    {
        From = UserProvisioningService.NormalizeSam(r.FromSam ?? ""),
        To = UserProvisioningService.NormalizeSam(r.ToSam ?? ""),
        StartUtc = r.StartUtc,
        EndUtc = r.EndUtc,
        r.RequiresApproval,
        r.Notes,
    };

    private static bool IsRejection(Exception ex) =>
        ex is DelegationValidationException or DelegationConflictException or Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException;

    /// <summary>
    /// O acțiune refuzată (validare, stare nepermisă, conflict) rămâne în audit, cu valorile încercate și motivul refuzului.
    /// </summary>
    private Task AuditRejectedAsync(string action, int? id, object? attempted, Exception ex, CancellationToken ct)
    {
        var reason = ex switch
        {
            DelegationValidationException v => string.Join("; ", v.Errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}")),
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => Messages.Get("Concurrency"),
            _ => ex.Message,
        };
        return audit.WriteAsync(new AuditEntry(action, nameof(Delegation), id?.ToString(),
            NewValues: attempted, Details: reason, Result: AuditResult.Failure), ct);
    }

    private static object Snapshot(Delegation d) => new
    {
        From = d.FromUser?.SamAccountName,
        To = d.ToUser?.SamAccountName,
        d.StartUtc,
        d.EndUtc,
        d.RequiresApproval,
        d.KeepCopyInSource,
        Status = d.Status.ToString(),
        d.Notes,
    };

    private static UserRef Ref(AppUser u) => new(u.Id, u.SamAccountName, u.DisplayName, u.Email);

    private static readonly System.Linq.Expressions.Expression<Func<Delegation, DelegationListItem>> ListProjection =
        d => new DelegationListItem(
            d.Id, d.Status,
            new UserRef(d.FromUser.Id, d.FromUser.SamAccountName, d.FromUser.DisplayName, d.FromUser.Email),
            new UserRef(d.ToUser.Id, d.ToUser.SamAccountName, d.ToUser.DisplayName, d.ToUser.Email),
            d.StartUtc, d.EndUtc, d.RequiresApproval, d.KeepCopyInSource,
            new UserRef(d.CreatedBy.Id, d.CreatedBy.SamAccountName, d.CreatedBy.DisplayName, d.CreatedBy.Email),
            d.CreatedUtc,
            d.Attachments.Count);

    private DelegationDetail ToDetail(Delegation d)
    {
        var isAdmin = currentUser.IsInRole(AppRoles.Admin);
        var isApprover = currentUser.IsInRole(AppRoles.Approver);
        var canDecide = isApprover && d.Status == DelegationStatus.PendingApproval;
        var actions = new AllowedActions(
            CanEdit: isAdmin && d.Status is DelegationStatus.PendingApproval or DelegationStatus.Scheduled,
            CanEditEndOnly: isAdmin && d.Status == DelegationStatus.Active,
            CanStop: isAdmin && DelegationStateMachine.CanTransition(d.Status, DelegationStatus.Stopped),
            CanManageAttachments: isAdmin,
            CanApprove: canDecide,
            CanReject: canDecide,
            IsOwnRequest: d.CreatedById == currentUser.UserId);

        return new DelegationDetail(
            d.Id, d.Status, Ref(d.FromUser), Ref(d.ToUser), d.StartUtc, d.EndUtc,
            d.RequiresApproval, d.KeepCopyInSource, d.Notes,
            Ref(d.CreatedBy), d.CreatedUtc,
            d.ApprovedBy is null ? null : Ref(d.ApprovedBy), d.ApprovedUtc, d.RejectReason,
            d.StoppedBy is null ? null : Ref(d.StoppedBy), d.StoppedUtc, d.StopReason,
            d.PreviousForwardingAddress, d.ExchangeAppliedUtc, d.ExchangeRemovedUtc, d.LastError,
            Convert.ToBase64String(d.RowVersion),
            d.Attachments.OrderBy(a => a.UploadedUtc).Select(a => new AttachmentDto(
                a.Id, a.OriginalFileName, a.SizeBytes, a.ContentType, Ref(a.UploadedBy), a.UploadedUtc,
                a.PurgedUtc is not null)).ToList(),
            d.Notifications.OrderBy(n => n.CreatedUtc).Select(n => new NotificationDto(
                n.RecipientEmail, n.RecipientUser?.DisplayName, n.RecipientType, n.Event,
                n.CreatedUtc, n.SentUtc, n.Error)).ToList(),
            d.StatusHistory.OrderBy(h => h.ChangedUtc).ThenBy(h => h.Id).Select(h => new StatusHistoryDto(
                h.FromStatus, h.ToStatus, h.ChangedBy?.DisplayName ?? "System", h.ChangedUtc, h.Reason)).ToList(),
            actions);
    }
}
