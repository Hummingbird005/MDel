using MDel.Domain.Enums;

namespace MDel.Domain.Entities;

/// <summary>Fișier atașat unei delegări; conținutul stă pe disc, în calea din administrare.</summary>
public class DelegationAttachment
{
    public int Id { get; set; }

    public int DelegationId { get; set; }
    public Delegation Delegation { get; set; } = null!;

    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Nume generat (GUID) folosit pe disc – nu se folosește niciodată numele trimis de client.</summary>
    public string StoredFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 hex.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public int UploadedById { get; set; }
    public AppUser UploadedBy { get; set; } = null!;
    public DateTime UploadedUtc { get; set; }

    /// <summary>Setat de jobul de retenție; înregistrarea rămâne, fișierul de pe disc dispare.</summary>
    public DateTime? PurgedUtc { get; set; }
}

/// <summary>Cine a fost informat, despre ce eveniment și când (afișat read-only în formular).</summary>
public class DelegationNotification
{
    public long Id { get; set; }

    public int DelegationId { get; set; }
    public Delegation Delegation { get; set; } = null!;

    /// <summary>Utilizatorul destinatar (null când mailul merge la o adresă de grup).</summary>
    public int? RecipientUserId { get; set; }
    public AppUser? RecipientUser { get; set; }

    public string RecipientEmail { get; set; } = string.Empty;
    public RecipientType RecipientType { get; set; }
    public NotificationEvent Event { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? SentUtc { get; set; }
    public string? Error { get; set; }
}

/// <summary>Istoricul tranzițiilor de status.</summary>
public class DelegationStatusHistory
{
    public long Id { get; set; }

    public int DelegationId { get; set; }
    public Delegation Delegation { get; set; } = null!;

    public DelegationStatus? FromStatus { get; set; }
    public DelegationStatus ToStatus { get; set; }

    /// <summary>null = acțiune automată a sistemului (scheduler).</summary>
    public int? ChangedById { get; set; }
    public AppUser? ChangedBy { get; set; }

    public DateTime ChangedUtc { get; set; }
    public string? Reason { get; set; }
}
