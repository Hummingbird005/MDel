namespace MDel.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "mdel.correlationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
        {
            id = Guid.NewGuid().ToString("N");
        }

        context.Items[ItemKey] = id;
        context.Response.Headers[HeaderName] = id;
        await next(context);
    }
}
