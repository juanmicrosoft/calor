using Microsoft.AspNetCore.Http;
using WholesaleOrders.Api.Middleware;

namespace WholesaleOrders.Tests;

public class IdempotencyTests
{
    [Fact]
    public async Task GET_Without_Key_Passes_Through()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new IdempotencyMiddleware(c => { nextCalled = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task POST_With_Key_Caches_Response()
    {
        var middleware = new IdempotencyMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"id\":\"first\"}");
        });

        var ctx1 = new DefaultHttpContext();
        ctx1.Request.Method = "POST";
        ctx1.Request.Path = "/api/payments/charge";
        ctx1.Request.Headers["Idempotency-Key"] = "k-test-1";
        ctx1.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(ctx1);

        // Second call: next would write "second", but middleware should serve cached "first"
        var middleware2 = new IdempotencyMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"id\":\"second\"}");
        });
        var ctx2 = new DefaultHttpContext();
        ctx2.Request.Method = "POST";
        ctx2.Request.Path = "/api/payments/charge";
        ctx2.Request.Headers["Idempotency-Key"] = "k-test-1";
        ctx2.Response.Body = new MemoryStream();
        await middleware2.InvokeAsync(ctx2);

        ctx2.Response.Body.Position = 0;
        var body = await new StreamReader(ctx2.Response.Body).ReadToEndAsync();
        Assert.Contains("first", body);
    }

    [Fact]
    public async Task POST_Without_Key_Passes_Through()
    {
        var middleware = new IdempotencyMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("{}");
        });
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/orders";
        ctx.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(ctx);
        Assert.Equal(200, ctx.Response.StatusCode);
    }
}
