using System.Security.Cryptography;
using System.Text;
using PolicyCollector.Backend.Api.Models;
using PolicyCollector.Backend.Config;

namespace PolicyCollector.Backend.Api.Middleware;

public sealed class HmacValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BackendOptions _options;
    private readonly ILogger<HmacValidationMiddleware> _logger;

    public HmacValidationMiddleware(RequestDelegate next, IOptions<BackendOptions> options, ILogger<HmacValidationMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api/v1/ingest"))
        {
            await _next(ctx);
            return;
        }

        ctx.Request.EnableBuffering();

        if (!ctx.Request.Headers.TryGetValue("X-Hmac-SHA256", out var hmacHeader))
        {
            if (_options.HmacRequired)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Missing X-Hmac-SHA256 header"));
                return;
            }
            await _next(ctx);
            return;
        }

        try
        {
            var bodyBytes = await ReadBodyAsync(ctx.Request);
            ctx.Request.Body.Position = 0;

            if (string.IsNullOrEmpty(_options.HmacSecret))
            {
                await _next(ctx);
                return;
            }

            var expectedHmac = ComputeHmac(bodyBytes, _options.HmacSecret);
            var receivedHmac = hmacHeader.ToString();

            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(receivedHmac),
                    Convert.FromBase64String(expectedHmac)))
            {
                _logger.LogWarning("HMAC validation failed from {IP}", ctx.Connection.RemoteIpAddress);
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("HMAC validation failed"));
                return;
            }

            await _next(ctx);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HMAC validation error");
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse("HMAC validation error"));
        }
    }

    private static string ComputeHmac(byte[] body, string secret)
    {
        try
        {
            var keyBytes = Convert.FromBase64String(secret);
            using var hmac = new HMACSHA256(keyBytes);
            return Convert.ToBase64String(hmac.ComputeHash(body));
        }
        catch
        {
            throw new InvalidOperationException("Failed to compute HMAC");
        }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request)
    {
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        return ms.ToArray();
    }
}
