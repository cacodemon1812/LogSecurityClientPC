# P6 — Backend: StorageWorker + AlertWorker + ViolationEngine

**Phase:** 2 | **Phụ thuộc:** P5 | **Output:** Consumer pipeline lưu DB + phát hiện vi phạm

## Mục tiêu

Implement StorageWorker (Redis → PostgreSQL), AlertWorker (Redis → check violations → alert), ViolationEngine (rule evaluation), DiffService (detect config changes), HostStatusService.

## Files cần tạo

```
src/PolicyCollector.Backend/
  Workers/
    StorageWorker.cs
    AlertWorker.cs
  Services/
    ViolationEngine.cs
    DiffService.cs
    HostStatusService.cs
    AlertSender.cs
  Data/
    Repositories/
      SnapshotRepository.cs
      ViolationRepository.cs
      HostRepository.cs
      ConfigChangeRepository.cs
    IDbConnectionFactory.cs
    NpgsqlConnectionFactory.cs

tests/PolicyCollector.Backend.Tests/
  Services/
    ViolationEngineTests.cs
    DiffServiceTests.cs
  Workers/
    StorageWorkerTests.cs
```

---

## Chi tiết từng file

### [FILE] `Workers/StorageWorker.cs`

```csharp
namespace PolicyCollector.Backend.Workers;

// Consumer group: "storage-workers"
// Đọc từ Redis Stream "ingest:queue", lưu vào PostgreSQL
public sealed class StorageWorker : BackgroundService
{
    private const string StreamKey = "ingest:queue";
    private const string GroupName = "storage-workers";
    private readonly string _consumerName = $"storage-{Environment.MachineName}";

    private readonly RedisQueue _queue;
    private readonly SnapshotRepository _snapshots;
    private readonly HostRepository _hosts;
    private readonly ConfigChangeRepository _changes;
    private readonly DiffService _diff;
    private readonly ILogger<StorageWorker> _logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("StorageWorker started, consumer={Consumer}", _consumerName);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var entries = await _queue.ReadGroupAsync(StreamKey, GroupName, _consumerName, count: 10);

                if (entries.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    continue;
                }

                foreach (var entry in entries)
                {
                    await ProcessEntry(entry, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StorageWorker error, will retry in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task ProcessEntry(StreamEntry entry, CancellationToken ct)
    {
        var messageId = entry.Id;
        string? json = null;
        try
        {
            json = entry.Values.FirstOrDefault(v => v.Name == "payload").Value;
            if (string.IsNullOrEmpty(json))
            {
                await _queue.DeadLetterAsync(StreamKey, messageId, "Empty payload");
                return;
            }

            var payload = JsonSerializer.Deserialize(json, JsonContext.Default.CollectionPayload);
            if (payload is null)
            {
                await _queue.DeadLetterAsync(StreamKey, messageId, "Deserialize returned null");
                return;
            }

            await SaveWithRetry(payload, ct);
            await _queue.AcknowledgeAsync(StreamKey, GroupName, messageId);

            _logger.LogDebug("Stored snapshot for {Hostname}", payload.Host?.Hostname);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message {Id}", messageId);
            // Không ACK — message sẽ được retry bởi PEL recovery
            // Sau 3 lần retry → dead letter
        }
    }

    private async Task SaveWithRetry(CollectionPayload payload, CancellationToken ct)
    {
        var retries = 0;
        while (true)
        {
            try
            {
                // Transaction: snapshot + host_latest + config_changes
                await using var conn = await _snapshots.OpenConnectionAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

                // 1. Insert snapshot (ON CONFLICT collection_id DO NOTHING — idempotent)
                var snapshotId = await _snapshots.UpsertAsync(conn, tx, payload, ct);

                // 2. Upsert host_latest
                await _hosts.UpsertAsync(conn, tx, payload, snapshotId, ct);

                // 3. Compute diff với snapshot trước → insert config_changes
                var previous = await _snapshots.GetPreviousAsync(conn, payload.Host!.Hostname, snapshotId, ct);
                if (previous is not null)
                {
                    var changes = _diff.ComputeDiff(previous, payload);
                    if (changes.Count > 0)
                        await _changes.InsertBatchAsync(conn, tx, changes, snapshotId, ct);
                }

                await tx.CommitAsync(ct);
                return;
            }
            catch (Exception ex) when (retries < 3)
            {
                retries++;
                _logger.LogWarning(ex, "DB error saving snapshot, retry {Attempt}/3", retries);
                await Task.Delay(TimeSpan.FromSeconds(retries * 2), ct);
            }
        }
    }
}
```

---

### [FILE] `Workers/AlertWorker.cs`

```csharp
namespace PolicyCollector.Backend.Workers;

// Consumer group khác: "alert-workers"
// Đọc cùng Redis Stream, kiểm tra violations, gửi alert
public sealed class AlertWorker : BackgroundService
{
    private const string StreamKey = "ingest:queue";
    private const string GroupName = "alert-workers";
    private readonly string _consumerName = $"alert-{Environment.MachineName}";

    private readonly RedisQueue _queue;
    private readonly ViolationEngine _engine;
    private readonly ViolationRepository _violations;
    private readonly AlertSender _alertSender;
    private readonly ILogger<AlertWorker> _logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var entries = await _queue.ReadGroupAsync(StreamKey, GroupName, _consumerName, count: 10);
                if (entries.Count == 0) { await Task.Delay(1000, ct); continue; }

                foreach (var entry in entries)
                    await ProcessAlert(entry, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AlertWorker error");
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task ProcessAlert(StreamEntry entry, CancellationToken ct)
    {
        try
        {
            var json = entry.Values.FirstOrDefault(v => v.Name == "payload").Value.ToString();
            var payload = JsonSerializer.Deserialize(json, JsonContext.Default.CollectionPayload);
            if (payload is null) { await _queue.AcknowledgeAsync(StreamKey, GroupName, entry.Id); return; }

            var snapshotId = /* lấy từ entry hoặc query DB */ Guid.Empty;

            // Chạy tất cả rules
            var violations = await _engine.EvaluateAsync(payload, ct);

            if (violations.Count > 0)
            {
                // Lưu violations vào DB
                await _violations.InsertNewAsync(snapshotId, violations, ct);

                // Gửi alert cho critical/high
                var urgent = violations.Where(v => v.Severity is "critical" or "high").ToList();
                if (urgent.Count > 0)
                    await _alertSender.SendAsync(payload.Host?.Hostname ?? "unknown", urgent, ct);
            }

            await _queue.AcknowledgeAsync(StreamKey, GroupName, entry.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alert processing failed for message {Id}", entry.Id);
        }
    }
}
```

---

### [FILE] `Services/ViolationEngine.cs`

```csharp
namespace PolicyCollector.Backend.Services;

public sealed class ViolationEngine
{
    private readonly PolicyRuleRepository _rules;
    private readonly ILogger<ViolationEngine> _logger;

    public async Task<List<ViolationEntry>> EvaluateAsync(
        CollectionPayload payload,
        CancellationToken ct)
    {
        var enabledRules = await _rules.GetEnabledRulesAsync(ct);
        var violations = new List<ViolationEntry>();
        var hostname = payload.Host?.Hostname ?? "unknown";

        foreach (var rule in enabledRules)
        {
            try
            {
                var violation = rule.RuleId switch
                {
                    "password.min_length"  => CheckPasswordMinLength(payload, rule),
                    "password.complexity"  => CheckPasswordComplexity(payload, rule),
                    "password.max_age"     => CheckPasswordMaxAge(payload, rule),
                    "password.lockout"     => CheckPasswordLockout(payload, rule),
                    "audit.logon"          => CheckAuditLogon(payload, rule),
                    "firewall.disabled"    => CheckFirewallDisabled(payload, rule),
                    "defender.realtime"    => CheckDefenderRealtime(payload, rule),
                    "uac.disabled"         => CheckUacDisabled(payload, rule),
                    "bitlocker.os_volume"  => CheckBitLockerOsVolume(payload, rule),
                    "tls.weak_protocol"    => CheckTlsWeakProtocol(payload, rule),
                    "rdp.nla_disabled"     => CheckRdpNla(payload, rule),
                    _                      => null
                };

                if (violation is not null)
                {
                    violation = violation with { Hostname = hostname };
                    violations.Add(violation);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rule {RuleId} evaluation failed for {Host}",
                    rule.RuleId, hostname);
            }
        }

        return violations;
    }

    // password.min_length: vi phạm nếu min_length < 8
    private static ViolationEntry? CheckPasswordMinLength(
        CollectionPayload payload, PolicyRule rule)
    {
        var policy = payload.SecurityPolicy?.PasswordPolicy;
        if (policy is null) return null;   // Module không collect được → bỏ qua
        if (policy.MinLength >= 8) return null;

        return new ViolationEntry
        {
            RuleId   = rule.RuleId,
            Severity = rule.Severity,
            Message  = $"Password minimum length is {policy.MinLength}, expected ≥ 8",
            Expected = "≥ 8",
            Actual   = policy.MinLength.ToString()
        };
    }

    // password.complexity: vi phạm nếu complexity = false
    private static ViolationEntry? CheckPasswordComplexity(
        CollectionPayload payload, PolicyRule rule)
    {
        var policy = payload.SecurityPolicy?.PasswordPolicy;
        if (policy is null) return null;
        if (policy.ComplexityEnabled) return null;

        return new ViolationEntry
        {
            RuleId   = rule.RuleId,
            Severity = rule.Severity,
            Message  = "Password complexity requirement is disabled",
            Expected = "true", Actual = "false"
        };
    }

    // firewall.disabled: vi phạm nếu bất kỳ profile nào bị tắt
    private static ViolationEntry? CheckFirewallDisabled(
        CollectionPayload payload, PolicyRule rule)
    {
        var fw = payload.Firewall;
        if (fw is null) return null;

        var disabledProfiles = new List<string>();
        if (fw.Profiles?.Domain?.Enabled == false)  disabledProfiles.Add("Domain");
        if (fw.Profiles?.Private?.Enabled == false) disabledProfiles.Add("Private");
        if (fw.Profiles?.Public?.Enabled == false)  disabledProfiles.Add("Public");

        if (disabledProfiles.Count == 0) return null;

        return new ViolationEntry
        {
            RuleId   = rule.RuleId,
            Severity = rule.Severity,
            Message  = $"Firewall disabled on profiles: {string.Join(", ", disabledProfiles)}",
            Expected = "Enabled on all profiles",
            Actual   = $"Disabled: {string.Join(", ", disabledProfiles)}"
        };
    }

    // defender.realtime: vi phạm nếu real_time_protection = false
    private static ViolationEntry? CheckDefenderRealtime(
        CollectionPayload payload, PolicyRule rule)
    {
        var defender = payload.Defender;
        if (defender is null) return null;
        if (defender.RealTimeProtectionEnabled) return null;

        return new ViolationEntry
        {
            RuleId = rule.RuleId, Severity = rule.Severity,
            Message = "Windows Defender real-time protection is disabled",
            Expected = "true", Actual = "false"
        };
    }

    // uac.disabled: vi phạm nếu UAC tắt
    private static ViolationEntry? CheckUacDisabled(
        CollectionPayload payload, PolicyRule rule)
    {
        var uac = payload.SecurityPolicy?.Uac;
        if (uac is null) return null;
        if (uac.Enabled) return null;

        return new ViolationEntry
        {
            RuleId = rule.RuleId, Severity = rule.Severity,
            Message = "User Account Control (UAC) is disabled",
            Expected = "enabled", Actual = "disabled"
        };
    }

    // tls.weak_protocol: vi phạm nếu TLS 1.0 hoặc SSL 3.0 bật
    private static ViolationEntry? CheckTlsWeakProtocol(
        CollectionPayload payload, PolicyRule rule)
    {
        var tls = payload.SecurityPolicy?.Tls?.Protocols;
        if (tls is null) return null;

        var weak = new List<string>();
        if (tls.Ssl30) weak.Add("SSL 3.0");
        if (tls.Tls10) weak.Add("TLS 1.0");

        if (weak.Count == 0) return null;

        return new ViolationEntry
        {
            RuleId = rule.RuleId, Severity = rule.Severity,
            Message = $"Weak protocols enabled: {string.Join(", ", weak)}",
            Expected = "Only TLS 1.2 and TLS 1.3", Actual = string.Join(", ", weak)
        };
    }

    // rdp.nla_disabled: vi phạm nếu RDP enabled + NLA disabled
    private static ViolationEntry? CheckRdpNla(CollectionPayload payload, PolicyRule rule)
    {
        var rdp = payload.SecurityPolicy?.Rdp;
        if (rdp is null || !rdp.Enabled) return null;
        if (rdp.NlaRequired) return null;

        return new ViolationEntry
        {
            RuleId = rule.RuleId, Severity = rule.Severity,
            Message = "RDP is enabled without Network Level Authentication (NLA)",
            Expected = "NLA required", Actual = "NLA not required"
        };
    }

    // bitlocker.os_volume: vi phạm nếu C: không phải FullyEncrypted
    private static ViolationEntry? CheckBitLockerOsVolume(
        CollectionPayload payload, PolicyRule rule)
    {
        if (payload.BitLocker is null) return null;
        var osVolume = payload.BitLocker.FirstOrDefault(v =>
            v.MountPoint?.Equals("C:", StringComparison.OrdinalIgnoreCase) == true);

        if (osVolume is null) return null;
        if (osVolume.VolumeStatus == "FullyEncrypted") return null;

        return new ViolationEntry
        {
            RuleId = rule.RuleId, Severity = rule.Severity,
            Message = $"OS volume C: is not fully encrypted (status: {osVolume.VolumeStatus})",
            Expected = "FullyEncrypted", Actual = osVolume.VolumeStatus
        };
    }

    // Các checks còn lại: CheckPasswordMaxAge, CheckPasswordLockout,
    //                     CheckAuditLogon — tương tự pattern trên
}
```

---

### [FILE] `Services/DiffService.cs`

```csharp
namespace PolicyCollector.Backend.Services;

public sealed class DiffService
{
    // So sánh 2 payload, trả về list field paths đã thay đổi
    public List<ConfigChange> ComputeDiff(
        CollectionPayload before,
        CollectionPayload after)
    {
        var changes = new List<ConfigChange>();
        var changedAt = after.CollectedAt;

        // Flatten cả 2 payload thành Dictionary<string, string>
        var flatBefore = FlattenPayload(before);
        var flatAfter  = FlattenPayload(after);

        // So sánh từng field
        foreach (var (path, newValue) in flatAfter)
        {
            flatBefore.TryGetValue(path, out var oldValue);
            if (oldValue != newValue)
                changes.Add(new ConfigChange
                {
                    Hostname    = after.Host?.Hostname ?? "unknown",
                    ChangedAt   = changedAt,
                    FieldPath   = path,
                    OldValue    = oldValue,
                    NewValue    = newValue
                });
        }

        // Detect removed fields
        foreach (var (path, oldValue) in flatBefore)
        {
            if (!flatAfter.ContainsKey(path))
                changes.Add(new ConfigChange
                {
                    Hostname  = after.Host?.Hostname ?? "unknown",
                    ChangedAt = changedAt,
                    FieldPath = path,
                    OldValue  = oldValue,
                    NewValue  = null  // removed
                });
        }

        return changes;
    }

    // Flatten CollectionPayload → Dictionary<"dot.path", "value">
    // Chỉ flatten các trường security-relevant, không flatten toàn bộ app list
    private static Dictionary<string, string> FlattenPayload(CollectionPayload p)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // security_policy.password.*
        var pw = p.SecurityPolicy?.PasswordPolicy;
        if (pw is not null)
        {
            result["security_policy.password.min_length"] = pw.MinLength.ToString();
            result["security_policy.password.complexity"] = pw.ComplexityEnabled.ToString();
            result["security_policy.password.max_age_days"] = pw.MaxAgeDays.ToString();
            result["security_policy.password.lockout_threshold"] = pw.LockoutThreshold.ToString();
        }

        // security_policy.uac.*
        var uac = p.SecurityPolicy?.Uac;
        if (uac is not null)
        {
            result["security_policy.uac.enabled"] = uac.Enabled.ToString();
            result["security_policy.uac.consent_prompt_level"] = uac.ConsentPromptLevel.ToString();
        }

        // security_policy.tls.protocols.*
        var tls = p.SecurityPolicy?.Tls?.Protocols;
        if (tls is not null)
        {
            result["security_policy.tls.TLS_1_0"] = tls.Tls10.ToString();
            result["security_policy.tls.TLS_1_2"] = tls.Tls12.ToString();
            result["security_policy.tls.SSL_3_0"] = tls.Ssl30.ToString();
        }

        // security_policy.rdp.*
        var rdp = p.SecurityPolicy?.Rdp;
        if (rdp is not null)
        {
            result["security_policy.rdp.enabled"] = rdp.Enabled.ToString();
            result["security_policy.rdp.nla_required"] = rdp.NlaRequired.ToString();
            result["security_policy.rdp.port"] = rdp.Port.ToString();
        }

        // firewall.profiles.*
        var fw = p.Firewall?.Profiles;
        if (fw is not null)
        {
            result["firewall.domain.enabled"]  = (fw.Domain?.Enabled ?? false).ToString();
            result["firewall.private.enabled"] = (fw.Private?.Enabled ?? false).ToString();
            result["firewall.public.enabled"]  = (fw.Public?.Enabled ?? false).ToString();
        }

        // defender.*
        var def = p.Defender;
        if (def is not null)
            result["defender.realtime_protection"] = def.RealTimeProtectionEnabled.ToString();

        // gpo: số GPO applied
        result["gpo.applied_count"] = p.Gpo?.ComputerGpos.Count(g => g.Applied).ToString() ?? "0";

        return result;
    }
}
```

---

### [FILE] `Services/AlertSender.cs`

```csharp
namespace PolicyCollector.Backend.Services;

// Gửi alert qua webhook (Alertmanager / Slack / Teams)
public sealed class AlertSender
{
    private readonly HttpClient _http;
    private readonly BackendOptions _options;
    private readonly ILogger<AlertSender> _logger;

    public async Task SendAsync(
        string hostname,
        IReadOnlyList<ViolationEntry> violations,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.AlertWebhookUrl)) return;

        var payload = new
        {
            alerts = violations.Select(v => new
            {
                labels = new
                {
                    alertname = $"PolicyViolation_{v.RuleId}",
                    severity  = v.Severity,
                    hostname,
                    rule_id   = v.RuleId
                },
                annotations = new
                {
                    summary     = v.Message,
                    description = $"Host: {hostname}\nExpected: {v.Expected}\nActual: {v.Actual}"
                },
                startsAt = DateTimeOffset.UtcNow
            })
        };

        try
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(_options.AlertWebhookUrl, content, ct);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Alert sent for {Host}: {Count} violations", hostname, violations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alert for {Host}", hostname);
            // Không re-throw — alert failure không block pipeline
        }
    }
}
```

---

## Unit Tests

### [TEST] `Services/ViolationEngineTests.cs`

```csharp
// Mock PolicyRuleRepository để trả về tất cả rules enabled
// Test cases cho từng rule:
//   CheckPasswordMinLength:  min_length=6 → violation HIGH
//   CheckPasswordMinLength:  min_length=12 → no violation
//   CheckPasswordMinLength:  SecurityPolicy=null → no violation (null safe)
//   CheckFirewallDisabled:   Domain=false → violation CRITICAL
//   CheckFirewallDisabled:   all enabled → no violation
//   CheckDefenderRealtime:   false → violation CRITICAL
//   CheckUacDisabled:        enabled=false → violation CRITICAL
//   CheckTlsWeakProtocol:    TLS 1.0=true → violation HIGH
//   CheckTlsWeakProtocol:    SSL 3.0=true → violation HIGH
//   CheckTlsWeakProtocol:    all modern → no violation
//   CheckRdpNla:             enabled=true, nla=false → violation HIGH
//   CheckRdpNla:             enabled=false → no violation (RDP off)
//   CheckBitLocker:          C: FullyEncrypted → no violation
//   CheckBitLocker:          C: EncryptionInProgress → violation HIGH
```

### [TEST] `Services/DiffServiceTests.cs`

```csharp
// Test cases:
//   - Same payload twice → 0 changes
//   - min_length changed 8→12 → 1 change detected
//   - Firewall domain disabled → 1 change detected
//   - New field added (forward compat) → detected as new
//   - Field removed → detected as removed
//   - null payload fields → handled gracefully
```

---

## Acceptance Criteria

- [ ] `StorageWorker` lưu snapshot vào DB sau khi receive từ Redis
- [ ] `StorageWorker` upsert `host_latest` với last_seen mới nhất
- [ ] `StorageWorker` compute diff và lưu vào `config_changes`
- [ ] `StorageWorker` retry 3 lần khi DB lỗi, sau đó dead letter
- [ ] `AlertWorker` phát hiện firewall disabled → `policy_violations` table có record
- [ ] `AlertWorker` gửi webhook cho violations critical/high
- [ ] `ViolationEngine` không throw khi payload module là null
- [ ] `DiffService` phát hiện thay đổi password policy
- [ ] Cả 2 workers đọc cùng Redis Stream với consumer group khác nhau (không miss message)
- [ ] ViolationEngine tests: ≥ 13 test cases, 100% pass
