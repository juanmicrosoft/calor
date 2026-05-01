using WholesaleOrders.Infra.Logging;

namespace WholesaleOrders.Api.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IStructuredLogger _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, IStructuredLogger logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (InvalidOperationException ex)
        {
            _logger.Warn("InvalidOperation caught", new { path = context.Request.Path.Value, message = ex.Message });
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            _logger.Warn("ArgumentException caught", new { path = context.Request.Path.Value, message = ex.Message });
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.Error("Unhandled exception", ex, new { path = context.Request.Path.Value });
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Internal server error." });
        }
    }
}
