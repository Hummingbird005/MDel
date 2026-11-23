using MDel.Domain.Enums;

namespace MDel.Domain.Entities;

/// <summary>
/// Forward de email de pe căsuța <see cref="FromUser"/> către <see cref="ToUser"/>
/// în intervalul [StartUtc, EndUtc). Doar forwarding, fără drepturi pe inbox.
/// </summary>
public class Delegation
{
    public int Id { get; set; }

    public int FromUserId { get; set; }
    public AppUser FromUser { get; set; } = null!;

    public int ToUserId { get; set; }
    public AppUser ToUser { get; set; } = null!;

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public DelegationStatus Status { get; set; } = DelegationStatus.PendingApproval;

    /// <summary>false = bifa „fără aprobare”: aprobatorii sunt doar notificați.</summary>
    public bool RequiresApproval { get; set; } = true;

    /// <summary>DeliverToMailboxAndForward – preluat din setarea globală la creare.</summary>
    public bool KeepCopyInSource { get; set; } = true;

    /// <summary>Forwardul existent pe sursă înainte de aplicare; se restaurează la final.</summary>
    public string? PreviousForwardingAddress { get; set; }
    public bool? PreviousDeliverToMailboxAndForward { get; set; }

    public string? Notes { get; set; }

    public int CreatedById { get; set; }
    public AppUser CreatedBy { get; set; } = null!;
    public DateTime CreatedUtc { get; set; }

    public int? ApprovedById { get; set; }
    public AppUser? ApprovedBy { get; set; }
    public DateTime? ApprovedUtc { get; set; }
    public string? RejectReason { get; set; }

    public int? StoppedById { get; set; }
    public AppUser? StoppedBy { get; set; }
    public DateTime? StoppedUtc { get; set; }
    public string? StopReason { get; set; }

    public DateTime? ExchangeAppliedUtc { get; set; }
    public DateTime? ExchangeRemovedUtc { get; set; }
    public string? LastError { get; set; }

    public DateTime? UpdatedUtc { get; set; }

    /// <summary>Token de concurență optimistă (ex. doi aprobatori simultan).</summary>
    public byte[] RowVersion { get; set; } = [];

    public ICollection<DelegationAttachment> Attachments { get; set; } = new List<DelegationAttachment>();
    public ICollection<DelegationNotification> Notifications { get; set; } = new List<DelegationNotification>();
    public ICollection<DelegationStatusHistory> StatusHistory { get; set; } = new List<DelegationStatusHistory>();
}
