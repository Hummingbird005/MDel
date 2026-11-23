using MDel.Domain.Entities;
using MDel.Domain.Enums;

namespace MDel.Domain;

/// <summary>
/// Tranzițiile permise între statusurile unei delegări.
///
///   PendingApproval ──aprobare──► Scheduled ──start──► Active ──end──► Expired
///         │   └────aprobare (start trecut)──────────────►│
///         ├─respingere─► Rejected                        │
///         └─oprire──► Stopped ◄────── oprire ────────────┘
///   Scheduled ──editare (necesită aprobare)──► PendingApproval
///   Scheduled/Active ──eroare Exchange──► Failed ──reîncercare──► Scheduled
/// </summary>
public static class DelegationStateMachine
{
    private static readonly Dictionary<DelegationStatus, DelegationStatus[]> Allowed = new()
    {
        [DelegationStatus.PendingApproval] =
            [DelegationStatus.Scheduled, DelegationStatus.Active, DelegationStatus.Rejected, DelegationStatus.Stopped],
        [DelegationStatus.Scheduled] =
            [DelegationStatus.Active, DelegationStatus.PendingApproval, DelegationStatus.Stopped, DelegationStatus.Failed,
             DelegationStatus.Expired],
        [DelegationStatus.Active] =
            [DelegationStatus.Expired, DelegationStatus.Stopped, DelegationStatus.Failed],
        [DelegationStatus.Failed] =
            [DelegationStatus.Scheduled, DelegationStatus.Active, DelegationStatus.Stopped, DelegationStatus.Expired],
        [DelegationStatus.Expired] = [],
        [DelegationStatus.Rejected] = [],
        [DelegationStatus.Stopped] = [],
    };

    /// <summary>Statusuri care „ocupă” căsuța sursă (folosite la verificarea suprapunerilor).</summary>
    public static readonly DelegationStatus[] OpenStatuses =
        [DelegationStatus.PendingApproval, DelegationStatus.Scheduled, DelegationStatus.Active, DelegationStatus.Failed];

    public static bool IsFinal(DelegationStatus status) => Allowed[status].Length == 0;

    public static bool CanTransition(DelegationStatus from, DelegationStatus to) => Allowed[from].Contains(to);

    /// <summary>Schimbă statusul și adaugă înregistrarea în istoric. Aruncă la tranziție invalidă.</summary>
    public static void TransitionTo(this Delegation delegation, DelegationStatus to, int? changedById, DateTime nowUtc,
        string? reason = null)
    {
        var from = delegation.Status;
        if (!CanTransition(from, to))
        {
            throw new InvalidStatusTransitionException(from, to);
        }

        delegation.Status = to;
        delegation.UpdatedUtc = nowUtc;
        delegation.StatusHistory.Add(new DelegationStatusHistory
        {
            FromStatus = from,
            ToStatus = to,
            ChangedById = changedById,
            ChangedUtc = nowUtc,
            Reason = reason,
        });
    }

    /// <summary>Statusul inițial după creare / aprobare, în funcție de data de început.</summary>
    public static DelegationStatus StatusAfterApproval(Delegation delegation, DateTime nowUtc) =>
        delegation.StartUtc <= nowUtc ? DelegationStatus.Active : DelegationStatus.Scheduled;
}

public sealed class InvalidStatusTransitionException(DelegationStatus from, DelegationStatus to)
    : InvalidOperationException($"A delegation cannot go from {from} to {to}.")
{
    public DelegationStatus From { get; } = from;
    public DelegationStatus To { get; } = to;
}
