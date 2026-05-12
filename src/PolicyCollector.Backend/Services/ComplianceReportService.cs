using System.Text;
using Dapper;

namespace PolicyCollector.Backend.Services;

public sealed class ComplianceReportService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ComplianceReportService> _logger;

    public ComplianceReportService(IDbConnectionFactory db, ILogger<ComplianceReportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> GenerateComplianceCsvAsync(string? domain, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var rows = await conn.QueryAsync("""
            WITH latest_violations AS (
                SELECT DISTINCT ON (hostname, rule_id)
                    hostname, rule_id, severity, message, resolved
                FROM policy_violations
                ORDER BY hostname, rule_id, detected_at DESC
            )
            SELECT
                hl.hostname,
                hl.domain,
                hl.os_version,
                hl.agent_version,
                hl.last_seen,
                pr.rule_id,
                pr.severity AS rule_severity,
                pr.description AS rule_description,
                CASE WHEN lv.rule_id IS NULL THEN 'PASS' ELSE 'FAIL' END AS status,
                lv.message AS violation_message
            FROM host_latest hl
            CROSS JOIN policy_rules pr
            LEFT JOIN latest_violations lv
                ON hl.hostname = lv.hostname AND pr.rule_id = lv.rule_id AND lv.resolved = FALSE
            WHERE pr.enabled = TRUE
              AND (@domain IS NULL OR hl.domain = @domain)
            ORDER BY hl.hostname, pr.rule_id
            """, new { domain });

        var sb = new StringBuilder();
        sb.AppendLine("Hostname,Domain,OS Version,Agent Version,Last Seen,Rule ID,Severity,Rule,Status,Message");

        foreach (dynamic r in rows)
        {
            sb.AppendLine(string.Join(",",
                CsvEscape((string?)r.hostname),
                CsvEscape((string?)r.domain),
                CsvEscape((string?)r.os_version),
                CsvEscape((string?)r.agent_version),
                ((DateTimeOffset?)r.last_seen)?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "",
                CsvEscape((string?)r.rule_id),
                CsvEscape((string?)r.rule_severity),
                CsvEscape((string?)r.rule_description),
                (string?)r.status,
                CsvEscape((string?)r.violation_message)));
        }

        _logger.LogInformation("Generated compliance report for domain: {Domain}, rows: {Count}",
            domain ?? "all", rows.Count());

        return sb.ToString();
    }

    public async Task<string> GenerateViolationsCsvAsync(
        string? domain, string? severity, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var rows = await conn.QueryAsync("""
            SELECT
                v.id,
                v.hostname,
                v.rule_id,
                v.severity,
                v.message,
                v.expected,
                v.actual,
                v.detected_at,
                v.resolved,
                v.resolved_at
            FROM policy_violations v
            WHERE (@domain IS NULL OR EXISTS (
                SELECT 1 FROM host_latest hl
                WHERE hl.hostname = v.hostname AND hl.domain = @domain
            ))
            AND (@severity IS NULL OR v.severity = @severity)
            ORDER BY v.detected_at DESC
            """, new { domain, severity });

        var sb = new StringBuilder();
        sb.AppendLine("ID,Hostname,Rule ID,Severity,Message,Expected,Actual,Detected At,Resolved,Resolved At");

        foreach (dynamic r in rows)
        {
            sb.AppendLine(string.Join(",",
                (long?)r.id,
                CsvEscape((string?)r.hostname),
                CsvEscape((string?)r.rule_id),
                CsvEscape((string?)r.severity),
                CsvEscape((string?)r.message),
                CsvEscape((string?)r.expected),
                CsvEscape((string?)r.actual),
                ((DateTimeOffset?)r.detected_at)?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "",
                ((bool?)r.resolved ?? false) ? "Yes" : "No",
                ((DateTimeOffset?)r.resolved_at)?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? ""));
        }

        _logger.LogInformation("Generated violations report: domain={Domain}, severity={Severity}, rows={Count}",
            domain ?? "all", severity ?? "all", rows.Count());

        return sb.ToString();
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
