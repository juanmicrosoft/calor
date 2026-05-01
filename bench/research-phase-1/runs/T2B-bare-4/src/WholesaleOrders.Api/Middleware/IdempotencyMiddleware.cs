using System.Collections.Concurrent;
using System.Text;

namespace WholesaleOrders.Api.Middleware;

public class IdempotencyMiddleware
{
    private const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly RequestDelegate _next;
    private static readonly ConcurrentDictionary<string, CachedResponse> _cache = new();

    public IdempotencyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) && !HttpMethods.IsPut(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var key) || string.IsNullOrWhiteSpace(key))
        {
            await _next(context);
            return;
        }

        var cacheKey = $"{key}:{context.Request.Path}";
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTimeOffset.UtcNow)
        {
            context.Response.StatusCode = cached.StatusCode;
            context.Response.ContentType = cached.ContentType;
            await context.Response.Body.WriteAsync(cached.Body);
            return;
        }

        var originalBody = context.Response.Body;
        using var ms = new MemoryStream();
        context.Response.Body = ms;
        try
        {
            await _next(context);
            ms.Position = 0;
            var bytes = ms.ToArray();
            _cache[cacheKey] = new CachedResponse(
                StatusCode: context.Response.StatusCode,
                ContentType: context.Response.ContentType ?? "application/json",
                Body: bytes,
                Expiry: DateTimeOffset.UtcNow.Add(Ttl));
            ms.Position = 0;
            await ms.CopyToAsync(originalBody);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private sealed record CachedResponse(int StatusCode, string ContentType, byte[] Body, DateTimeOffset Expiry);
}
