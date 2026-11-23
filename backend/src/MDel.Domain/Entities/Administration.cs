using MDel.Domain.Enums;

namespace MDel.Domain.Entities;

/// <summary>Membru al grupului de informare (gestionat din aplicație, nu din AD).</summary>
public class InformGroupMember
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public int AddedById { get; set; }
    public AppUser AddedBy { get; set; } = null!;
    public DateTime AddedUtc { get; set; }
}

/// <summary>Setare de aplicație configurabilă din ecranul de administrare.</summary>
public class AppSetting
{
    /// <summary>Ex.: "Retention.AuditDays". Vezi <see cref="SettingKeys"/>.</summary>
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    /// <summary>Valoarea este criptată (Data Protection) și nu se returnează niciodată în API.</summary>
    public bool IsSecret { get; set; }

    public string Category { get; set; } = string.Empty;

    public int? UpdatedById { get; set; }
    public AppUser? UpdatedBy { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>Jurnal de audit – append-only.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }

    /// <summary>null = sistem (joburi automate) sau utilizator neautentificat.</summary>
    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Numele de login la momentul acțiunii (rămâne valid și dacă userul se schimbă).</summary>
    public string UserName { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? Details { get; set; }

    public string? HttpMethod { get; set; }
    public string? Path { get; set; }
    public int? StatusCode { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }

    public AuditResult Result { get; set; }
}
