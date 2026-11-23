using MDel.Application.Abstractions;
using MDel.Domain.Enums;

namespace MDel.Infrastructure.Delegations;

public sealed record UserRef(int Id, string SamAccountName, string DisplayName, string? Email);

public sealed record DelegationListItem(
    int Id,
    DelegationStatus Status,
    UserRef From,
    UserRef To,
    DateTime StartUtc,
    DateTime EndUtc,
    bool RequiresApproval,
    bool KeepCopyInSource,
    UserRef CreatedBy,
    DateTime CreatedUtc,
    int AttachmentCount);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record DelegationQuery(
    DelegationStatus[]? Statuses = null,
    string? Search = null,
    bool? KeepCopy = null,
    int Page = 1,
    int PageSize = 25);

public sealed record AttachmentDto(
    int Id,
    string FileName,
    long SizeBytes,
    string ContentType,
    UserRef UploadedBy,
    DateTime UploadedUtc,
    bool Purged);

public sealed record NotificationDto(
    string RecipientEmail,
    string? RecipientName,
    RecipientType RecipientType,
    NotificationEvent Event,
    DateTime CreatedUtc,
    DateTime? SentUtc,
    string? Error);

public sealed record StatusHistoryDto(
    DelegationStatus? FromStatus,
    DelegationStatus ToStatus,
    string? ChangedBy,
    DateTime ChangedUtc,
    string? Reason);

/// <summary>IsOwnRequest: interfața avertizează că aprobarea propriei cereri poate fi interzisă.</summary>
public sealed record AllowedActions(
    bool CanEdit, bool CanEditEndOnly, bool CanStop, bool CanManageAttachments,
    bool CanApprove, bool CanReject, bool IsOwnRequest);

public sealed record DelegationStats(
    IReadOnlyDictionary<DelegationStatus, int> ByStatus,
    int ExpiringIn7Days,
    int ExpiredLast30Days);

public sealed record DecisionRequest(string? Reason = null, string? RowVersion = null);

public sealed record DelegationDetail(
    int Id,
    DelegationStatus Status,
    UserRef From,
    UserRef To,
    DateTime StartUtc,
    DateTime EndUtc,
    bool RequiresApproval,
    bool KeepCopyInSource,
    string? Notes,
    UserRef CreatedBy,
    DateTime CreatedUtc,
    UserRef? ApprovedBy,
    DateTime? ApprovedUtc,
    string? RejectReason,
    UserRef? StoppedBy,
    DateTime? StoppedUtc,
    string? StopReason,
    string? PreviousForwardingAddress,
    DateTime? ExchangeAppliedUtc,
    DateTime? ExchangeRemovedUtc,
    string? LastError,
    string RowVersion,
    IReadOnlyList<AttachmentDto> Attachments,
    IReadOnlyList<NotificationDto> Notifications,
    IReadOnlyList<StatusHistoryDto> History,
    AllowedActions Actions);

public sealed record SaveDelegationRequest(
    string FromSam,
    string ToSam,
    DateTime StartUtc,
    DateTime EndUtc,
    bool RequiresApproval = true,
    string? Notes = null,
    string? RowVersion = null);

public sealed record StopDelegationRequest(string Reason, string? RowVersion = null);

public sealed record DelegationFormConfig(
    int MaxAttachmentMb,
    IReadOnlyList<string> AllowedExtensions,
    bool KeepCopyInSource);

public sealed record SourceCheckResult(
    MailboxForwarding? CurrentForwarding,
    string? ExchangeError,
    IReadOnlyList<DelegationListItem> OpenDelegations);

/// <summary>Erori de validare per câmp (→ 400 ValidationProblem).</summary>
public sealed class DelegationValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The delegation is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Operație nepermisă în starea curentă sau conflict de concurență (→ 409).</summary>
public sealed class DelegationConflictException(string message) : Exception(message);
