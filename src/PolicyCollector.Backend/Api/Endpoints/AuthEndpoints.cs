using System.Security.Claims;
using BCrypt.Net;
using PolicyCollector.Backend.Data.Repositories;
using PolicyCollector.Backend.Services;

namespace PolicyCollector.Backend.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth");

        group.MapPost("/login", Login).AllowAnonymous();
        group.MapPost("/logout", Logout).AllowAnonymous();
        group.MapGet("/me", Me);
    }

    private static async Task<IResult> Login(
        LoginRequest req,
        UserRepository users,
        JwtService jwt,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest(new { error = "Username and password required" });

        var user = await users.GetByUsernameAsync(req.Username.Trim(), ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            logger.LogWarning("Failed login attempt for username '{Username}'", req.Username);
            return Results.Unauthorized();
        }

        await users.UpdateLastLoginAsync(user.Id, ct);

        var token = jwt.Generate(user.Id, user.Username, user.Email, user.Role);
        return Results.Ok(new
        {
            token,
            user = new
            {
                id       = user.Id,
                username = user.Username,
                email    = user.Email,
                fullName = user.FullName,
                role     = user.Role
            }
        });
    }

    private static IResult Logout() => Results.Ok(new { message = "Logged out" });

    private static IResult Me(HttpContext ctx)
    {
        var principal = ctx.Items["AuthedUser"] as ClaimsPrincipal;
        if (principal is null) return Results.Unauthorized();

        return Results.Ok(new
        {
            id       = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0"),
            username = principal.FindFirstValue(ClaimTypes.Name),
            email    = principal.FindFirstValue(ClaimTypes.Email),
            role     = principal.FindFirstValue(ClaimTypes.Role)
        });
    }

    public sealed record LoginRequest(string Username, string Password);
}
