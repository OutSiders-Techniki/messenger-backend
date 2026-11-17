using Messenger.Api.Configuration;

namespace Messenger.Api.Middleware;

/// <summary>Защита cookie-запросов и WebSocket handshake от чужих браузерных origin.</summary>
public sealed class SameOriginMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private readonly HashSet<string> _allowedOrigins = ReadAllowedOrigins(configuration);

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var mutates = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method);
        // Проверяется также GET handshake WebSocket: CORS сам его не защищает.
        var realtime = request.Path.StartsWithSegments("/hubs");
        if (mutates || realtime)
        {
            var origin = request.Headers.Origin;
            var normalized = origin.Count == 1 ? NormalizeOrigin(origin[0]) : null;
            var requestOrigin = NormalizeOrigin($"{request.Scheme}://{request.Host}");
            // Dev-proxy сохраняет браузерный Origin, но меняет upstream Host.
            // Допускаются только явно настроенные публичные origin; Forwarded-заголовкам не доверяем.
            var foreignOrigin = origin.Count > 0 && (normalized is null
                || (!string.Equals(normalized, requestOrigin, StringComparison.OrdinalIgnoreCase)
                    && !_allowedOrigins.Contains(normalized)));
            var crossSite = request.Headers["Sec-Fetch-Site"].Any(value =>
                string.Equals(value, "cross-site", StringComparison.OrdinalIgnoreCase));

            if (foreignOrigin || crossSite)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                var (title, detail, code) = ApiProblem.ForStatus(StatusCodes.Status403Forbidden);
                await context.Response.WriteAsJsonAsync(
                    ApiProblem.Create(StatusCodes.Status403Forbidden, title, detail, code),
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: context.RequestAborted);
                return;
            }
        }
        await next(context);
    }

    private static HashSet<string> ReadAllowedOrigins(IConfiguration configuration)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var origin in configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [])
        {
            result.Add(NormalizeOrigin(origin) ?? throw new InvalidOperationException(
                "Security:AllowedOrigins должен содержать точные HTTP(S) origin без пути и wildcard."));
        }
        return result;
    }

    private static string? NormalizeOrigin(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https")
            || uri.Host.Contains('*') || uri.AbsolutePath != "/"
            || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            return null;
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
