using PolicyCollector.Backend.Api.Models;
using PolicyCollector.Backend.Services;

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
        var path = ctx.Request.Path.Value ?? "";

        // Skip auth for health checks and UI auth endpoints
        if (path.StartsWith("/health") || path.StartsWith("/api/v1/auth/"))
        {
            await _next(ctx);
            return;
        }

        // Try Bearer JWT first (dashboard UI)
        if (ctx.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var bearer = authHeader.ToString();
            if (bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = bearer["Bearer ".Length..].Trim();
                var jwt = ctx.RequestServices.GetRequiredService<JwtService>();
                var principal = jwt.Validate(token);

                if (principal is not null)
                {
                    ctx.Items["AuthedUser"] = principal;
                    await _next(ctx);
                    return;
                }

                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Invalid or expired token"));
                return;
            }
        }

        // Fall back to API key (agent)
        if (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var keyValues))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Authentication required"));
            return;
        }

        var key = keyValues.ToString().Trim();
        if (key.Length < 32)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Invalid API key format"));
            return;
        }

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
