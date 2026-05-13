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

        group.MapGet("/security-overview", GetSecurityOverview)
             .Produces(200)
             .WithName("GetSecurityOverview");

        group.MapGet("/patches", GetPatchStatus)
             .Produces(200)
             .WithName("GetPatchStatus");
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

    private static async Task<IResult> GetSecurityOverview(
        IDbConnectionFactory db,
        CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = (await conn.QueryAsync("""
            WITH latest AS (
                SELECT DISTINCT ON (hostname) hostname, collected_at, payload
                FROM collection_snapshots
                ORDER BY hostname, collected_at DESC
            )
            SELECT
                hostname,
                collected_at,
                (payload->'defender'->>'antivirus_enabled')::boolean           AS defender_enabled,
                (payload->'defender'->>'real_time_protection')::boolean        AS real_time_protection,
                (payload->'defender'->>'cloud_protection')::boolean            AS cloud_protection,
                payload->'defender'->>'signature_version'                      AS signature_version,
                jsonb_array_length(COALESCE(payload->'bitlocker','[]'::jsonb)) AS bitlocker_volume_count,
                (payload->'hardware_security'->>'tpm_present')::boolean        AS tpm_present,
                (payload->'hardware_security'->>'tpm_enabled')::boolean        AS tpm_enabled,
                (payload->'hardware_security'->>'secure_boot_enabled')::boolean AS secure_boot_enabled,
                (payload->'hardware_security'->>'uefi_mode')::boolean          AS uefi_mode,
                (payload->'laps'->>'policy_configured')::boolean               AS laps_configured
            FROM latest
            ORDER BY hostname
            """)).ToList();

        return Results.Ok(new
        {
            total_hosts       = rows.Count,
            defender_disabled = rows.Count(r => r.defender_enabled     == false),
            rtprotection_off  = rows.Count(r => r.real_time_protection == false),
            tpm_missing       = rows.Count(r => r.tpm_present          == false),
            secure_boot_off   = rows.Count(r => r.secure_boot_enabled  == false),
            laps_unconfigured = rows.Count(r => r.laps_configured      == false),
            items             = rows
        });
    }

    private static async Task<IResult> GetPatchStatus(
        IDbConnectionFactory db,
        CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync("""
            WITH latest AS (
                SELECT DISTINCT ON (hostname) hostname, collected_at, payload
                FROM collection_snapshots
                ORDER BY hostname, collected_at DESC
            )
            SELECT
                hostname,
                collected_at,
                (payload->'patch'->>'hotfix_count')::int        AS hotfix_count,
                payload->'patch'->>'wsus_server'                AS wsus_server,
                (payload->'patch'->>'no_auto_update')::boolean  AS no_auto_update,
                (payload->'patch'->>'auto_update_options')::int AS auto_update_options,
                payload->'patch'->>'last_success_install'       AS last_success_install,
                payload->'patch'->>'last_success_detect'        AS last_success_detect
            FROM latest
            ORDER BY hostname
            """);

        return Results.Ok(new { items = rows });
    }
}

public sealed record UpdateRuleRequest(bool Enabled, string? Severity);
