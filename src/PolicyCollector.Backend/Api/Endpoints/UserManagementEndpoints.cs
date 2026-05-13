using System.Security.Claims;
using PolicyCollector.Backend.Data.Repositories;

namespace PolicyCollector.Backend.Api.Endpoints;

public static class UserManagementEndpoints
{
    public static void MapUserManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/users");

        group.MapGet("/", GetAll);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);
        group.MapPut("/{id:int}/password", ChangePassword);
    }

    private static bool IsAdmin(HttpContext ctx)
    {
        var principal = ctx.Items["AuthedUser"] as ClaimsPrincipal;
        return principal?.FindFirstValue(ClaimTypes.Role) == "admin";
    }

    private static async Task<IResult> GetAll(HttpContext ctx, UserRepository users, CancellationToken ct)
    {
        if (!IsAdmin(ctx)) return Results.Forbid();
        var all = await users.GetAllAsync(ct);
        return Results.Ok(all.Select(u => new
        {
            id        = u.Id,
            username  = u.Username,
            email     = u.Email,
            fullName  = u.FullName,
            role      = u.Role,
            active    = u.Active,
            createdAt = u.CreatedAt,
            lastLogin = u.LastLogin
        }));
    }

    private static async Task<IResult> Create(
        HttpContext ctx,
        CreateUserRequest req,
        UserRepository users,
        CancellationToken ct)
    {
        if (!IsAdmin(ctx)) return Results.Forbid();

        if (string.IsNullOrWhiteSpace(req.Username) ||
            string.IsNullOrWhiteSpace(req.Email) ||
            string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest(new { error = "Username, email, and password are required" });

        if (!new[] { "admin", "analyst", "viewer" }.Contains(req.Role))
            return Results.BadRequest(new { error = "Role must be admin, analyst, or viewer" });

        if (req.Password.Length < 8)
            return Results.BadRequest(new { error = "Password must be at least 8 characters" });

        try
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(req.Password, 10);
            var user = await users.CreateAsync(req.Username.Trim(), req.Email.Trim(), req.FullName, hash, req.Role, ct);
            return Results.Created($"/api/v1/admin/users/{user.Id}", new
            {
                id       = user.Id,
                username = user.Username,
                email    = user.Email,
                fullName = user.FullName,
                role     = user.Role,
                active   = user.Active
            });
        }
        catch (Exception ex) when (ex.Message.Contains("unique") || ex.Message.Contains("duplicate"))
        {
            return Results.Conflict(new { error = "Username or email already exists" });
        }
    }

    private static async Task<IResult> Update(
        int id,
        HttpContext ctx,
        UpdateUserRequest req,
        UserRepository users,
        CancellationToken ct)
    {
        if (!IsAdmin(ctx)) return Results.Forbid();

        var currentUser = ctx.Items["AuthedUser"] as System.Security.Claims.ClaimsPrincipal;
        var currentId = int.Parse(currentUser?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        // Prevent admin from deactivating themselves
        if (id == currentId && req.Active == false)
            return Results.BadRequest(new { error = "Cannot deactivate your own account" });

        if (req.Role is not null && !new[] { "admin", "analyst", "viewer" }.Contains(req.Role))
            return Results.BadRequest(new { error = "Invalid role" });

        await users.UpdateAsync(id, req.FullName, req.Role, req.Active, ct);
        return Results.Ok();
    }

    private static async Task<IResult> ChangePassword(
        int id,
        HttpContext ctx,
        ChangePasswordRequest req,
        UserRepository users,
        CancellationToken ct)
    {
        if (!IsAdmin(ctx)) return Results.Forbid();
        if (req.NewPassword.Length < 8)
            return Results.BadRequest(new { error = "Password must be at least 8 characters" });

        var hash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword, 10);
        await users.UpdatePasswordAsync(id, hash, ct);
        return Results.Ok();
    }

    private static async Task<IResult> Delete(int id, HttpContext ctx, UserRepository users, CancellationToken ct)
    {
        if (!IsAdmin(ctx)) return Results.Forbid();

        var currentUser = ctx.Items["AuthedUser"] as System.Security.Claims.ClaimsPrincipal;
        var currentId = int.Parse(currentUser?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        if (id == currentId) return Results.BadRequest(new { error = "Cannot delete your own account" });

        await users.DeleteAsync(id, ct);
        return Results.Ok();
    }

    public sealed record CreateUserRequest(string Username, string Email, string? FullName, string Password, string Role);
    public sealed record UpdateUserRequest(string? FullName, string? Role, bool? Active);
    public sealed record ChangePasswordRequest(string NewPassword);
}
