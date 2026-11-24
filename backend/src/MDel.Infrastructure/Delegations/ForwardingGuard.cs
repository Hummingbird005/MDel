using MDel.Domain.Entities;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MDel.Infrastructure.Delegations;

/// <summary>
/// O singură delegare pune forward pe o căsuță în același timp. Cât timp forwardul altei delegări pe aceeași
/// căsuță sursă e încă în Exchange (ex. eliminarea a eșuat și se reîncearcă), următoarea nu se activează:
/// altfel ar salva forwardul acelei delegări drept „forward anterior” și l-ar readuce la final, permanent.
/// </summary>
public static class ForwardingGuard
{
    public static Task<bool> SourceHasOtherAppliedForwardAsync(MDelDbContext db, Delegation d, CancellationToken ct) =>
        db.Delegations.AsNoTracking().AnyAsync(o =>
            o.FromUserId == d.FromUserId && o.Id != d.Id && o.ExchangeAppliedUtc != null && o.ExchangeRemovedUtc == null, ct);
}
