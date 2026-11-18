namespace Messenger.Api.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; " +
                "connect-src 'self'; img-src 'self' blob:; media-src 'self' blob:; " +
                "object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
        await next(context);
    }
}
