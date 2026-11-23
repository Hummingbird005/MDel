namespace MDel.Domain.Enums;

/// <summary>Ciclul de viață al unei delegări (forward de căsuță).</summary>
public enum DelegationStatus
{
    PendingApproval = 0,
    Scheduled = 1,
    Active = 2,
    Expired = 3,
    Rejected = 4,
    Stopped = 5,
    Failed = 6,
}

/// <summary>Categoria destinatarului unei notificări.</summary>
public enum RecipientType
{
    ApproverGroup = 0,
    AdminGroup = 1,
    SourceUser = 2,
    TargetUser = 3,
    InformGroup = 4,
}

/// <summary>Evenimentul care a generat notificarea.</summary>
public enum NotificationEvent
{
    Created = 0,
    Approved = 1,
    Rejected = 2,
    Activated = 3,
    Expired = 4,
    Stopped = 5,
    Failed = 6,
}

public enum AuditResult
{
    Success = 0,
    Failure = 1,
    Denied = 2,
}
