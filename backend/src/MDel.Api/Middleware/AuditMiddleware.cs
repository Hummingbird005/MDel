using MDel.Application.Abstractions;
using MDel.Application.Common;
using MDel.Domain.Enums;

namespace MDel.Api.Middleware;

/// <summary>
/// Audit automat la nivel HTTP pentru toate acțiunile de modificare (POST/PUT/PATCH/DELETE)
/// și pentru orice acces refuzat (401/403). Acțiunile de business scriu în plus
/// înregistrări detaliate prin IAuditService (valori vechi/noi).
/// </summary>
public sealed class AuditMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> MutatingMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public async Task InvokeAsync(HttpContext context, IAuditService audit)
    {
        Exception? failure = null;
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var status = failure is null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError;
                var denied = status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden;

                if (MutatingMethods.Contains(context.Request.Method) || denied)
                {
                    var result = denied ? AuditResult.Denied : status >= 400 ? AuditResult.Failure : AuditResult.Success;
                    await audit.WriteAsync(new AuditEntry(
                        AuditActions.HttpRequest,
                        Details: $"{context.Request.Method} {context.Request.Path} -> {status}",
                        Result: result,
                        HttpMethod: context.Request.Method,
                        Path: context.Request.Path.Value,
                        StatusCode: status));
                }
            }
        }
    }
}
