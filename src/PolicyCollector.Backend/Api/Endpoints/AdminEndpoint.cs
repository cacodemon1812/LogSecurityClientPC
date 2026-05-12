using Dapper;
using PolicyCollector.Backend.Data.Models;
using PolicyCollector.Backend.Data.Repositories;

namespace PolicyCollector.Backend.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin");

        group.MapGet("/rules", GetRules)
             .Produces<IReadOnlyList<PolicyRule>>(200)
             .WithName("GetRules");

        group.MapPut("/rules/{ruleId}", UpdateRule)
             .Produces(200)
             .Produces<ErrorResponse>(404)
             .WithName("UpdateRule");

        group.MapGet("/stats", GetStats)
             .Produces(200)
             .WithName("GetStats");
    }

    private static async Task<IResult> GetRules(
        PolicyRuleRepository rules,
        CancellationToken ct)
    {
        var allRules = await rules.GetAllRulesAsync(ct);
        return Results.Ok(allRules);
    }

    private static async Task<IResult> UpdateRule(
        string ruleId,
        [FromBody] UpdateRuleRequest request,
        PolicyRuleRepository rules,
        CancellationToken ct)
    {
        await rules.UpdateAsync(ruleId, request.Enabled, request.Severity, ct);
        return Results.Ok();
    }

    private static async Task<IResult> GetStats(
        IDbConnectionFactory db,
        CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var stats = await conn.QueryFirstAsync("""
            SELECT
                (SELECT COUNT(*) FROM host_latest) AS total_hosts,
                (SELECT COUNT(*) FROM host_latest WHERE last_seen > NOW() - INTERVAL '2 hours') AS online_hosts,
                (SELECT COUNT(*) FROM host_latest WHERE last_seen < NOW() - INTERVAL '24 hours') AS offline_hosts,
                (SELECT COUNT(*) FROM policy_violations WHERE resolved = FALSE) AS open_violations,
                (SELECT COUNT(*) FROM policy_violations WHERE resolved = FALSE AND severity = 'critical') AS critical_violations,
                (SELECT COUNT(*) FROM collection_snapshots WHERE received_at > NOW() - INTERVAL '1 hour') AS ingestions_last_hour
            """);

        return Results.Ok(stats);
    }
}

public sealed record UpdateRuleRequest(bool Enabled, string? Severity);
