using Messenger.Api.Configuration;

namespace Messenger.Api.Middleware;

/// <summary>Защита cookie-запросов и WebSocket handshake от чужих браузерных origin.</summary>
public sealed class SameOriginMiddleware(RequestDelegate next)
{
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
            var foreignOrigin = origin.Count > 0 && (origin.Count != 1
                || !Uri.TryCreate(origin[0], UriKind.Absolute, out var uri)
                || uri.Scheme != request.Scheme
                || !string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0
                || uri.UserInfo.Length > 0);
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
}
