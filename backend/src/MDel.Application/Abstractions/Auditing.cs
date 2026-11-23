using MDel.Domain.Enums;

namespace MDel.Application.Abstractions;

/// <summary>Utilizatorul curent (din contextul HTTP sau „system” pentru joburi).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Id-ul din tabela AppUsers (null pentru sistem/neautentificat).</summary>
    int? UserId { get; }

    string UserName { get; }
    string? IpAddress { get; }
    string? CorrelationId { get; }
    bool IsInRole(string role);
}

public sealed record AuditEntry(
    string Action,
    string? EntityType = null,
    string? EntityId = null,
    object? OldValues = null,
    object? NewValues = null,
    string? Details = null,
    AuditResult Result = AuditResult.Success,
    string? HttpMethod = null,
    string? Path = null,
    int? StatusCode = null,
    int? UserIdOverride = null,
    string? UserNameOverride = null);

/// <summary>Scrie înregistrări în jurnalul de audit (append-only).</summary>
public interface IAuditService
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
}
