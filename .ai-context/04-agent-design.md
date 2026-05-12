# 04 — Agent Design

## Cấu trúc thư mục

```
src/PolicyCollector.Agent/
├── Collectors/
│   ├── ICollector.cs                  # Interface chung
│   ├── CollectorResult.cs             # Wrapper kết quả + error
│   ├── GpoCollector.cs
│   ├── SecurityPolicyCollector.cs
│   ├── FirewallCollector.cs
│   ├── DefenderCollector.cs
│   ├── BitLockerCollector.cs
│   ├── AppInventoryCollector.cs
│   ├── AppxCollector.cs
│   ├── ServiceCollector.cs
│   ├── ScheduledTaskCollector.cs
│   └── StartupCollector.cs
├── Jobs/
│   ├── CollectionJob.cs               # Orchestrator
│   └── RetryJob.cs                    # Offline queue retry
├── Scheduler/
│   └── CollectionScheduler.cs         # PeriodicTimer wrapper
├── Transport/
│   ├── ITransport.cs
│   ├── HttpTransport.cs
│   └── LocalQueue.cs                  # SQLite offline buffer
├── Models/
│   ├── CollectionPayload.cs           # Root payload object
│   ├── HostInfo.cs
│   ├── GpoResult.cs
│   ├── SecPolicyResult.cs
│   └── ... (per collector)
├── Config/
│   ├── AgentOptions.cs                # Strongly-typed config
│   └── SecretsProvider.cs             # DPAPI / Credential Manager
├── Infrastructure/
│   ├── PowerShellRunner.cs            # PS Runspace pool
│   ├── WmiQuery.cs                    # CIM/WMI helper
│   └── RegistryReader.cs             # Safe registry read helper
└── Program.cs                         # Host builder + DI
```

---

## Interface cốt lõi

### ICollector

```csharp
// Mọi collector phải implement interface này
public interface ICollector<TResult>
{
    string ModuleName { get; }

    // Trả về CollectorResult — KHÔNG throw exception
    // Token để cancel nếu collector chạy quá lâu
    Task<CollectorResult<TResult>> CollectAsync(CancellationToken ct);
}

public sealed class CollectorResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
}
```

**Quy tắc:**
- Collector **không được throw** — catch toàn bộ exception, trả về `Success = false`.
- Collector **không gọi HTTP** — chỉ đọc local system.
- Collector phải hoàn thành trong **30 giây** — CollectionJob set CancellationToken timeout.
- Collector chạy **dưới SYSTEM account** — không giả định quyền user thông thường.

### CollectionJob

```csharp
// Orchestrate tất cả collectors song song, build payload
public sealed class CollectionJob
{
    public async Task<CollectionPayload> RunAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        var results = await Task.WhenAll(
            _gpoCollector.CollectAsync(timeout.Token),
            _secPolicyCollector.CollectAsync(timeout.Token),
            _firewallCollector.CollectAsync(timeout.Token),
            // ...
        );

        return PayloadBuilder.Build(_hostInfo, results);
    }
}
```

---

## Collector — Nguồn dữ liệu và kỹ thuật

### GpoCollector

```
Nguồn 1: gpresult /X <tempfile.xml> /SCOPE COMPUTER /FORCE
  → Parse XML với System.Xml.Linq
  → Lấy: AppliedGPOs, DeniedGPOs, GPO refresh time, CSE results

Nguồn 2: Registry HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\History
  → Lấy GUID và timestamp của mỗi GPO

Fallback: WMI RSOP namespace (Win32_RSOP_SettingStatus)
  → Dùng khi gpresult thất bại (máy không join domain)

Quyền cần: SYSTEM hoặc Administrators
Thời gian thực thi ước tính: 3–8 giây (gpresult chạy process riêng)
```

### SecurityPolicyCollector

```
Password policy:
  - Nguồn 1: secedit /export /cfg <tempfile.inf> /areas SECURITYPOLICY
    → Parse INI file
  - Nguồn 2: NetQueryDisplayInformation / NetUserModalsGet (P/Invoke)

Audit policy:
  - auditpol /get /category:* /r → CSV output parse

User rights:
  - secedit /export /cfg <tempfile.inf> /areas USER_RIGHTS
  - Parse [Privilege Rights] section

UAC / RDP / TLS:
  - Registry trực tiếp (RegistryReader helper)
  - Đọc HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System
  - Đọc HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols

Quyền cần: SYSTEM (secedit yêu cầu elevated)
Thời gian thực thi ước tính: 2–5 giây
```

### FirewallCollector

```
Profiles:
  - COM: HNetCfg.FwMgr (INetFwMgr) → GetCurrentProfileType()
  - Hoặc: PowerShell Get-NetFirewallProfile

Rules:
  - PowerShell: Get-NetFirewallRule | Get-NetFirewallPortFilter
  - Giới hạn export: tối đa 500 rules; nếu nhiều hơn → chỉ ghi tổng số + enabled rules
  - Sort: enabled rules trước, sort theo DisplayName

Quyền cần: SYSTEM hoặc Administrators
Thời gian thực thi ước tính: 3–10 giây (tùy số rules)
```

### AppInventoryCollector

```
Registry keys cần đọc:
  HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*
  HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\* (x86 apps)
  HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\* (per-user, optional)

Fields: DisplayName, DisplayVersion, Publisher, InstallDate,
        InstallLocation, UninstallString, EstimatedSize

Lọc bỏ: entries không có DisplayName (system components)

Quyền cần: Read access to HKLM (SYSTEM có)
Thời gian thực thi ước tính: < 1 giây
```

### ServiceCollector

```
Nguồn: System.ServiceProcess.ServiceController + Win32_Service WMI
  - ServiceController cho status nhanh
  - WMI Win32_Service cho: StartName (account), PathName, Description

Filter: Bỏ qua driver services (Win32_SystemDriver)

Quyền cần: SYSTEM
Thời gian thực thi ước tính: 1–3 giây
```

---

## Scheduler

```csharp
// Dùng PeriodicTimer — không drift, chính xác hơn Timer
public sealed class CollectionScheduler : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_options.CollectOnStartup)
            await RunCollectionCycle(ct);

        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(_options.IntervalMinutes));

        while (await timer.WaitForNextTickAsync(ct))
            await RunCollectionCycle(ct);
    }
}
```

---

## Transport & Offline Queue

### HttpTransport

```
POST {BackendUrl}/api/v1/ingest
Content-Type: application/json
X-Api-Key: {key}
X-Agent-Version: {ver}
X-Hmac-SHA256: {base64(HMAC-SHA256(body, secret))}

Response:
  200 OK        → lưu OK
  400 Bad Request → log, KHÔNG retry (payload lỗi)
  422 Unprocessable → log schema mismatch, KHÔNG retry
  401/403       → log auth error, KHÔNG retry
  429           → đọc Retry-After header, backoff
  5xx           → retry với exponential backoff
  Network error → enqueue LocalQueue
```

### LocalQueue (SQLite)

```sql
CREATE TABLE outbox (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    payload     TEXT NOT NULL,
    created_at  TEXT NOT NULL,
    retry_count INTEGER NOT NULL DEFAULT 0,
    last_error  TEXT
);
```

- RetryJob poll mỗi 5 phút.
- Payload quá 7 ngày bị xóa (không có ý nghĩa sau khi stale).
- Tối đa 1000 entries — nếu đầy, drop oldest.

---

## Cấu hình Agent

### appsettings.json

```json
{
  "Agent": {
    "IntervalMinutes": 60,
    "CollectOnStartup": true,
    "CollectorTimeoutSeconds": 30,
    "Modules": {
      "GPO": true,
      "SecurityPolicy": true,
      "Firewall": true,
      "Defender": true,
      "BitLocker": true,
      "AppInventory": true,
      "AppxPackages": false,
      "Services": true,
      "ScheduledTasks": true,
      "StartupEntries": true
    }
  },
  "Transport": {
    "BackendUrl": "https://collector.internal/api/v1/ingest",
    "TimeoutSeconds": 30,
    "MaxRetries": 5,
    "InitialRetryDelaySeconds": 10,
    "UseMtls": false,
    "ClientCertStore": "LocalMachine",
    "ClientCertThumbprint": ""
  },
  "LocalQueue": {
    "MaxAgeHours": 168,
    "MaxEntries": 1000,
    "RetryIntervalMinutes": 5
  },
  "Logging": {
    "Serilog": {
      "MinimumLevel": "Information",
      "WriteTo": [
        {
          "Name": "RollingFile",
          "Args": {
            "pathFormat": "C:\\ProgramData\\PolicyCollector\\logs\\agent-{Date}.log",
            "fileSizeLimitBytes": 10485760,
            "retainedFileCountLimit": 7
          }
        }
      ]
    }
  }
}
```

### Override qua Registry (cho GPO ADMX)

```
HKLM\SOFTWARE\Policies\PolicyCollector\
  BackendUrl          REG_SZ
  IntervalMinutes     REG_DWORD
  EnabledModules      REG_MULTI_SZ
```

Thứ tự ưu tiên: **Registry (GPO) > appsettings.json > defaults**

---

## Windows Service

```
Service Name:    PolicyCollectorSvc
Display Name:    Policy Collector Service
Description:     Collects system configuration and sends to central management
Startup Type:    Automatic (Delayed Start)
Account:         LocalSystem (NT AUTHORITY\SYSTEM)
Recovery:        Restart on failure, 3 attempts, 60 second delay
Dependencies:    RpcSs, WinMgmt (WMI)
```

### Cài đặt service

```batch
sc create PolicyCollectorSvc binPath="C:\Program Files\PolicyCollector\PolicyCollector.Agent.exe" start=delayed-auto
sc description PolicyCollectorSvc "Collects system configuration and sends to central management"
sc failure PolicyCollectorSvc reset=86400 actions=restart/60000/restart/60000/restart/60000
```
