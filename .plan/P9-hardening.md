# P9 — Hardening: mTLS + Collectors nâng cao + GPO Config Override

**Phase:** 3 | **Phụ thuộc:** P4, P8 | **Output:** Production-ready v1.0

## Mục tiêu

Thêm mTLS transport, hoàn thiện registry config override từ GPO, implement collectors còn lại (Defender, BitLocker, AppX, ScheduledTask, Startup), thêm compliance report export, và Kubernetes production setup.

## Files cần tạo / chỉnh sửa

```
src/PolicyCollector.Agent/
  [EDIT] Config/AgentOptions.cs      — thêm RegistryConfigProvider
  [FILE] Config/RegistryConfigProvider.cs
  [EDIT] Transport/HttpTransport.cs  — thêm mTLS certificate loading
  [FILE] Transport/MtlsHandler.cs    — HttpMessageHandler với client cert

src/PolicyCollector.Backend/
  [FILE] Api/Endpoints/ReportEndpoint.cs   — export compliance CSV
  [FILE] Services/ComplianceReportService.cs
  [FILE] Api/Endpoints/AdminEndpoint.cs    — rotate API key, manage rules
  [FILE] Data/Repositories/PolicyRuleRepository.cs

installer/
  [EDIT] admx/PolicyCollector.admx   — thêm mTLS settings
  [EDIT] admx/en-US/PolicyCollector.adml

tests/
  [TEST] Config/RegistryConfigProviderTests.cs
  [TEST] Services/ComplianceReportServiceTests.cs
```

---

## Chi tiết từng file

### [FILE] `Config/RegistryConfigProvider.cs`

```csharp
namespace PolicyCollector.Agent.Config;

// Registry config provider — đọc GPO settings từ HKLM\SOFTWARE\Policies\PolicyCollector
// Thứ tự ưu tiên: Registry (GPO) > appsettings.json > defaults
public sealed class RegistryConfigProvider : ConfigurationProvider, IConfigurationSource
{
    private const string RegistryPath = @"SOFTWARE\Policies\PolicyCollector";

    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public override void Load()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RegistryPath);
        if (key is null) return;  // Không có GPO settings → dùng appsettings.json

        // BackendUrl
        var backendUrl = key.GetValue("BackendUrl") as string;
        if (!string.IsNullOrEmpty(backendUrl))
            Data["Transport:BackendUrl"] = backendUrl;

        // IntervalMinutes
        if (key.GetValue("IntervalMinutes") is int interval && interval > 0)
            Data["Agent:IntervalMinutes"] = interval.ToString();

        // Modules (HKLM\...\PolicyCollector\Modules\)
        using var modulesKey = key.OpenSubKey("Modules");
        if (modulesKey is not null)
        {
            foreach (var moduleName in modulesKey.GetValueNames())
            {
                var enabled = modulesKey.GetValue(moduleName) is int val && val == 1;
                Data[$"Agent:Modules:{moduleName}"] = enabled.ToString();
            }
        }

        // mTLS settings
        if (key.GetValue("UseMtls") is int mtls)
            Data["Transport:UseMtls"] = (mtls == 1).ToString();

        if (key.GetValue("ClientCertThumbprint") is string thumbprint)
            Data["Transport:ClientCertThumbprint"] = thumbprint;
    }
}
```

Đăng ký trong `Program.cs`:
```csharp
builder.Configuration.Add(new RegistryConfigProvider());
// Thêm SAU các source khác để override
```

---

### [FILE] `Transport/MtlsHandler.cs`

```csharp
namespace PolicyCollector.Agent.Transport;

// HttpMessageHandler tải client certificate từ Windows Certificate Store
public sealed class MtlsHandler : HttpClientHandler
{
    public MtlsHandler(TransportOptions options, ILogger<MtlsHandler> logger)
    {
        // TLS 1.2+ only
        SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        CheckCertificateRevocationList = true;

        if (options.UseMtls && !string.IsNullOrEmpty(options.ClientCertThumbprint))
        {
            var cert = LoadCertificate(
                options.ClientCertStore,
                options.ClientCertThumbprint,
                logger);

            if (cert is not null)
                ClientCertificates.Add(cert);
            else
                logger.LogWarning("mTLS enabled but certificate not found: {Thumbprint}",
                    options.ClientCertThumbprint);
        }
    }

    private static X509Certificate2? LoadCertificate(
        string storeName, string thumbprint, ILogger logger)
    {
        var location = storeName.Equals("CurrentUser", StringComparison.OrdinalIgnoreCase)
            ? StoreLocation.CurrentUser
            : StoreLocation.LocalMachine;

        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly);

        var thumb = thumbprint.Replace(" ", "").ToUpperInvariant();
        var certs = store.Certificates.Find(
            X509FindType.FindByThumbprint, thumb, validOnly: true);

        if (certs.Count == 0)
        {
            logger.LogWarning("Certificate not found or invalid: {Thumbprint}", thumb);
            return null;
        }

        var cert = certs[0];
        if (!cert.HasPrivateKey)
        {
            logger.LogWarning("Certificate {Thumbprint} has no private key", thumb);
            return null;
        }

        return cert;
    }
}
```

---

### [FILE] `Api/Endpoints/ReportEndpoint.cs`

```csharp
namespace PolicyCollector.Backend.Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        // Export compliance report CSV
        app.MapGet("/api/v1/reports/compliance", ExportComplianceReport)
           .Produces(200, contentType: "text/csv")
           .Produces<ErrorResponse>(400);

        // Export violations CSV
        app.MapGet("/api/v1/reports/violations", ExportViolationsReport)
           .Produces(200, contentType: "text/csv");
    }

    private static async Task<IResult> ExportComplianceReport(
        [FromQuery] string? domain,
        ComplianceReportService reportService,
        CancellationToken ct)
    {
        var csv = await reportService.GenerateComplianceCsvAsync(domain, ct);
        var bytes = Encoding.UTF8.GetBytes(csv);
        var filename = $"compliance-report-{DateTime.UtcNow:yyyy-MM-dd}.csv";
        return Results.File(bytes, "text/csv", filename);
    }

    private static async Task<IResult> ExportViolationsReport(
        [FromQuery] string? domain,
        [FromQuery] string? severity,
        ComplianceReportService reportService,
        CancellationToken ct)
    {
        var csv = await reportService.GenerateViolationsCsvAsync(domain, severity, ct);
        var bytes = Encoding.UTF8.GetBytes(csv);
        return Results.File(bytes, "text/csv", $"violations-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }
}
```

---

### [FILE] `Services/ComplianceReportService.cs`

```csharp
namespace PolicyCollector.Backend.Services;

public sealed class ComplianceReportService
{
    private readonly IDbConnectionFactory _db;

    // Compliance report: mỗi host + pass/fail từng rule
    public async Task<string> GenerateComplianceCsvAsync(
        string? domain, CancellationToken ct)
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

        // Build CSV
        var sb = new StringBuilder();
        sb.AppendLine("Hostname,Domain,OS Version,Agent Version,Last Seen,Rule ID,Severity,Rule,Status,Message");

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                CsvEscape(r.hostname),
                CsvEscape(r.domain),
                CsvEscape(r.os_version),
                CsvEscape(r.agent_version),
                r.last_seen?.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                CsvEscape(r.rule_id),
                CsvEscape(r.rule_severity),
                CsvEscape(r.rule_description),
                r.status,
                CsvEscape(r.violation_message)));
        }

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
```

---

### [FILE] `Api/Endpoints/AdminEndpoint.cs`

```csharp
namespace PolicyCollector.Backend.Api.Endpoints;

// Admin endpoints — require additional admin API key or role
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin");

        // GET /api/v1/admin/rules — list all policy rules
        group.MapGet("/rules", GetRules);

        // PUT /api/v1/admin/rules/{ruleId} — enable/disable rule hoặc change severity
        group.MapPut("/rules/{ruleId}", UpdateRule);

        // POST /api/v1/admin/keys/rotate — tạo new API key, old valid 24h
        group.MapPost("/keys/rotate", RotateApiKey);

        // GET /api/v1/admin/stats — agent stats summary
        group.MapGet("/stats", GetStats);
    }

    private static async Task<IResult> GetRules(
        PolicyRuleRepository rules, CancellationToken ct) { ··· }

    private static async Task<IResult> UpdateRule(
        string ruleId,
        [FromBody] UpdateRuleRequest request,
        PolicyRuleRepository rules,
        CancellationToken ct) { ··· }

    // Rotate API key: tạo key mới, mark key cũ expires_at = NOW() + 24h
    private static async Task<IResult> RotateApiKey(
        IDbConnectionFactory db, CancellationToken ct) { ··· }

    private static async Task<IResult> GetStats(
        IDbConnectionFactory db, CancellationToken ct)
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
```

---

### [FILE] `Data/Repositories/PolicyRuleRepository.cs`

```csharp
namespace PolicyCollector.Backend.Data.Repositories;

public sealed class PolicyRuleRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IMemoryCache _cache;

    // Cache rules 5 phút — không cần query DB mỗi message
    public async Task<IReadOnlyList<PolicyRule>> GetEnabledRulesAsync(CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync("policy_rules", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            await using var conn = await _db.OpenAsync(ct);
            var rules = await conn.QueryAsync<PolicyRule>(
                "SELECT rule_id, severity, description, enabled FROM policy_rules WHERE enabled = TRUE");
            return rules.ToList() as IReadOnlyList<PolicyRule>;
        }) ?? [];
    }

    public async Task UpdateAsync(string ruleId, bool enabled, string? severity, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync("""
            UPDATE policy_rules
            SET enabled = @enabled,
                severity = COALESCE(@severity, severity),
                updated_at = NOW()
            WHERE rule_id = @ruleId
            """, new { ruleId, enabled, severity });

        _cache.Remove("policy_rules");  // Invalidate cache
    }
}
```

---

## Kiểm tra toàn bộ hệ thống (End-to-End Checklist)

```
AGENT side:
  [ ] Registry override: set HKLM\...\PolicyCollector\BackendUrl → agent dùng URL mới
  [ ] mTLS: agent gửi client cert, backend xác minh
  [ ] Offline: tắt backend 10 phút, agent buffer, bật lại → retry thành công
  [ ] Collectors: Defender, BitLocker, AppX, ScheduledTask, Startup hoạt động đúng
  [ ] MSI upgrade: cài v1.0.1 trên máy có v1.0.0 → service tiếp tục, data giữ nguyên

BACKEND side:
  [ ] Compliance CSV export: download được, mở trong Excel đúng
  [ ] Violations CSV export: đúng dữ liệu
  [ ] Admin API: enable/disable rule → ViolationEngine không detect rule bị disable
  [ ] API key rotation: old key vẫn valid 24h, new key hoạt động ngay
  [ ] /api/v1/admin/stats: đúng số liệu

PIPELINE end-to-end:
  [ ] Agent collect → Backend ingest → StorageWorker lưu DB → Grafana hiển thị
  [ ] Agent collect → AlertWorker phát hiện violation → webhook gửi
  [ ] Diff: collect lần 1, thay đổi policy, collect lần 2 → diff API trả đúng change
```

---

## Acceptance Criteria

- [ ] `RegistryConfigProvider` override `appsettings.json` khi registry key tồn tại
- [ ] `RegistryConfigProvider` không throw khi registry key không tồn tại
- [ ] mTLS: `HttpTransport` gửi client cert khi `UseMtls=true` và cert tồn tại trong store
- [ ] mTLS: agent vẫn gửi được (không crash) nếu cert không tìm thấy (log warning, fallback)
- [ ] Compliance CSV: mỗi host × rule = 1 row, status PASS/FAIL đúng
- [ ] Admin rule update: disable rule → violation engine không vi phạm rule đó
- [ ] API key rotation: old key valid 24h (grace period), new key working immediately
- [ ] ADMX: GPO ADMX file load được vào GPMC, settings hiển thị trong `Computer Configuration`
