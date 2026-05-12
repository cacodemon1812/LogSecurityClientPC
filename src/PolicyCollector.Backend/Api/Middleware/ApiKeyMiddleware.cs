using System.Security.Cryptography;
using System.Text;
using PolicyCollector.Backend.Api.Models;

namespace PolicyCollector.Backend.Api.Middleware;

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/health"))
        {
            await _next(ctx);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var keyValues))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Missing X-Api-Key header"));
            return;
        }

        var key = keyValues.ToString().Trim();
        if (key.Length < 32)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Invalid API key format"));
            return;
        }

        // For now, validate against environment API key (simple validation)
        // In production, this would lookup from DB with rate limiting
        var configKey = ctx.RequestServices.GetRequiredService<IConfiguration>()
            .GetSection("Backend:ApiKey").Value;

        if (!key.Equals(configKey, StringComparison.Ordinal))
        {
            _logger.LogWarning("Invalid API key attempt from {IP}", ctx.Connection.RemoteIpAddress);
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Invalid API key"));
            return;
        }

        ctx.Items["ApiKeyValid"] = true;
        await _next(ctx);
    }
}
