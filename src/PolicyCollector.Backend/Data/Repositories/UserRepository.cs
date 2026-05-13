using Dapper;
using PolicyCollector.Backend.Data.Models;

namespace PolicyCollector.Backend.Data.Repositories;

public sealed class UserRepository
{
    private readonly IDbConnectionFactory _db;

    public UserRepository(IDbConnectionFactory db) => _db = db;

    public async Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<AppUser>(
            "SELECT id, username, email, full_name, password_hash, role, active, created_at, updated_at, last_login FROM app_users WHERE username = @username AND active = TRUE",
            new { username });
    }

    public async Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<AppUser>(
            "SELECT id, username, email, full_name, password_hash, role, active, created_at, updated_at, last_login FROM app_users WHERE id = @id",
            new { id });
    }

    public async Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var users = await conn.QueryAsync<AppUser>(
            "SELECT id, username, email, full_name, role, active, created_at, updated_at, last_login FROM app_users ORDER BY created_at DESC");
        return users.ToList();
    }

    public async Task<AppUser> CreateAsync(string username, string email, string? fullName, string passwordHash, string role, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var id = await conn.ExecuteScalarAsync<int>("""
            INSERT INTO app_users (username, email, full_name, password_hash, role)
            VALUES (@username, @email, @fullName, @passwordHash, @role)
            RETURNING id
            """, new { username, email, fullName, passwordHash, role });

        return (await GetByIdAsync(id, ct))!;
    }

    public async Task UpdateAsync(int id, string? fullName, string? role, bool? active, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync("""
            UPDATE app_users SET
                full_name  = COALESCE(@fullName, full_name),
                role       = COALESCE(@role, role),
                active     = COALESCE(@active, active),
                updated_at = NOW()
            WHERE id = @id
            """, new { id, fullName, role, active });
    }

    public async Task UpdatePasswordAsync(int id, string passwordHash, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE app_users SET password_hash = @passwordHash, updated_at = NOW() WHERE id = @id",
            new { id, passwordHash });
    }

    public async Task UpdateLastLoginAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE app_users SET last_login = NOW() WHERE id = @id",
            new { id });
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync("DELETE FROM app_users WHERE id = @id", new { id });
    }
}
