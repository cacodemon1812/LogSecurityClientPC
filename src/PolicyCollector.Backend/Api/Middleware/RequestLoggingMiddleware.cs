namespace PolicyCollector.Backend.Api.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var startTime = DateTime.UtcNow;
        var path = ctx.Request.Path;
        var method = ctx.Request.Method;

        try
        {
            await _next(ctx);
        }
        finally
        {
            var elapsed = DateTime.UtcNow - startTime;
            var statusCode = ctx.Response.StatusCode;

            _logger.LogInformation(
                "{Method} {Path} - Status: {StatusCode} - Duration: {ElapsedMs}ms - IP: {IP}",
                method, path, statusCode, elapsed.TotalMilliseconds, ctx.Connection.RemoteIpAddress);
        }
    }
}
