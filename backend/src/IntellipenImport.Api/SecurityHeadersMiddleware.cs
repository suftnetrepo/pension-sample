namespace IntellipenImport.Api;

/// <summary>
/// Headers on every response. Member data must never be cached by the browser or a proxy, so everything under /api (data and
/// downloads alike) is no-store. The rest stop content-type sniffing, referrer leaks and framing. HSTS is added in Program.cs
/// outside Development.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        http.Response.OnStarting(() =>
        {
            var h = http.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "no-referrer";
            h["X-Frame-Options"] = "DENY";
            if (http.Request.Path.StartsWithSegments("/api"))
            {
                h.CacheControl = "no-store";
                h.Pragma = "no-cache";
            }
            return Task.CompletedTask;
        });
        return next(http);
    }
}
