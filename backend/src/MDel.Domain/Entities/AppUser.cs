namespace MDel.Domain.Entities;

/// <summary>
/// Utilizator din Active Directory, păstrat local (cache) pentru relații, preferințe și audit.
/// Se creează/actualizează la autentificare sau la selectarea din AD.
/// </summary>
public class AppUser
{
    public int Id { get; set; }

    /// <summary>objectGUID din AD – identificator stabil (nu se schimbă la redenumire).</summary>
    public Guid? AdObjectId { get; set; }

    /// <summary>DOMAIN\sam sau sam – folosit la login.</summary>
    public string SamAccountName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>"ro" sau "en".</summary>
    public string PreferredLanguage { get; set; } = "ro";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }

    public ICollection<Delegation> DelegationsAsSource { get; set; } = new List<Delegation>();
    public ICollection<Delegation> DelegationsAsTarget { get; set; } = new List<Delegation>();
    public ICollection<Delegation> DelegationsCreated { get; set; } = new List<Delegation>();
    public ICollection<Delegation> DelegationsApproved { get; set; } = new List<Delegation>();
    public ICollection<Delegation> DelegationsStopped { get; set; } = new List<Delegation>();
    public ICollection<DelegationAttachment> UploadedAttachments { get; set; } = new List<DelegationAttachment>();
    public ICollection<DelegationNotification> Notifications { get; set; } = new List<DelegationNotification>();
    public ICollection<DelegationStatusHistory> StatusChanges { get; set; } = new List<DelegationStatusHistory>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public InformGroupMember? InformGroupMembership { get; set; }
}
