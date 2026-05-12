# PolicyCollector — Dịch vụ Thu thập Cấu hình Chính sách Máy trạm / Máy chủ Windows

> **Tài liệu đầy đủ:** Xem [.ai-context/](.ai-context/CONTEXT.md) cho kiến trúc chi tiết, data models, tech stack, deployment guide và roadmap.

## 1. Tổng quan

**PolicyCollector** là một Windows service chạy nền trên từng endpoint, định kỳ thu thập các thông tin cấu hình GPO, chính sách bảo mật, và ứng dụng đã cài đặt, sau đó đẩy dữ liệu về hệ thống thu thập trung tâm (Collector Backend) để phân tích, cảnh báo và quản lý tập trung.

```
┌─────────────────────────────────────────────────────────────┐
│                        ENDPOINT                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  PolicyCollector Service (Windows Service / Agent)   │   │
│  │  ┌──────────┐  ┌──────────┐  ┌────────────────────┐ │   │
│  │  │ GPO      │  │ Policy   │  │ App Inventory      │ │   │
│  │  │ Collector│  │ Collector│  │ Collector          │ │   │
│  │  └────┬─────┘  └────┬─────┘  └─────────┬──────────┘ │   │
│  │       └─────────────┴──────────────────┘             │   │
│  │                      │                               │   │
│  │               ┌──────▼──────┐                        │   │
│  │               │  Scheduler  │  (Task / Timer)        │   │
│  │               └──────┬──────┘                        │   │
│  │                      │                               │   │
│  │               ┌──────▼──────┐                        │   │
│  │               │  Transport  │  (HTTPS / mTLS)        │   │
│  │               └─────────────┘                        │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────┬───────────────────────────┘
                                  │ HTTPS POST (JSON/gRPC)
                     ┌────────────▼──────────────┐
                     │   Collector Backend        │
                     │  ┌───────────────────────┐ │
                     │  │  Ingest API           │ │
                     │  │  Storage (DB / TSDB)  │ │
                     │  │  Dashboard / Alerts   │ │
                     │  └───────────────────────┘ │
                     └────────────────────────────┘
```

---

## 2. Phạm vi mục tiêu

| Hạng mục | Chi tiết |
|---|---|
| **OS Client** | Windows 10 (1809+), Windows 11 |
| **OS Server** | Windows Server 2019, 2022, 2025 |
| **Kiến trúc** | x64 |
| **Môi trường** | Domain-joined và Workgroup |
| **Quyền chạy** | SYSTEM hoặc Local Administrator |

---

## 3. Dữ liệu cần thu thập

### 3.1 GPO — Group Policy Objects

| Trường | Mô tả | Nguồn |
|---|---|---|
| `gpo_name` | Tên GPO được áp dụng | `gpresult /R /SCOPE COMPUTER` |
| `gpo_guid` | GUID của GPO | WMI / GPMC COM API |
| `link_path` | OU/Site/Domain GPO liên kết | Registry `HKLM\SOFTWARE\Policies` |
| `applied_time` | Thời điểm áp dụng gần nhất | Event Log ID 1500, 1501, 1502 |
| `status` | Applied / Not Applied / Error | `gpresult /X` (XML output) |
| `cse_list` | Danh sách Client-Side Extensions đã chạy | `gpresult /X` |

### 3.2 Security Policy

| Trường | Mô tả | Nguồn |
|---|---|---|
| `password_policy` | Min length, complexity, max age, lockout | `secedit /export` / `net accounts` |
| `audit_policy` | Logon, Object Access, Privilege Use ... | `auditpol /get /category:*` |
| `user_rights` | SeDebugPrivilege, SeRemoteInteractiveLogon ... | `secedit /export /areas USER_RIGHTS` |
| `firewall_profile` | Domain/Private/Public — enabled, default action | `netsh advfirewall show allprofiles` |
| `firewall_rules` | Rule name, direction, action, port | `Get-NetFirewallRule` (PowerShell) |
| `defender_settings` | Real-time protection, cloud protection, exclusions | `Get-MpPreference` |
| `uac_level` | UAC consent prompt level | Registry `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` |
| `bitlocker_status` | Encryption status per volume | `manage-bde -status` / `Get-BitLockerVolume` |
| `tls_config` | Enabled protocols (TLS 1.0/1.1/1.2/1.3) | Registry `HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL` |
| `rdp_policy` | NLA required, port, timeout | Registry + Group Policy |

### 3.3 Application Inventory

| Trường | Mô tả | Nguồn |
|---|---|---|
| `app_name` | Tên ứng dụng | Registry `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` |
| `version` | Phiên bản | Registry |
| `publisher` | Nhà phát hành | Registry |
| `install_date` | Ngày cài đặt | Registry |
| `install_location` | Đường dẫn cài đặt | Registry |
| `architecture` | x64 / x86 | Registry (Wow6432Node) |
| `source` | MSI / MSIX / Appx / Manual | Registry / AppxPackage |
| `appx_packages` | Universal Windows Platform apps | `Get-AppxPackage` |
| `running_services` | Tên, trạng thái, startup type, binary path | `Get-Service` + SCM |
| `scheduled_tasks` | Task name, action, trigger, last run result | `Get-ScheduledTask` |
| `startup_programs` | Run key, Startup folder entries | Registry Run keys |

### 3.4 System Metadata (kèm theo mỗi payload)

| Trường | Mô tả |
|---|---|
| `hostname` | Tên máy |
| `domain` | Domain / Workgroup |
| `os_version` | Build number, Edition |
| `agent_version` | Phiên bản của PolicyCollector |
| `collected_at` | Timestamp UTC (ISO 8601) |
| `collection_id` | UUID ngẫu nhiên của lần thu thập |

---

## 4. Kiến trúc Agent (PolicyCollector Service)

### 4.1 Ngôn ngữ & Runtime

- **Ngôn ngữ chính:** C# (.NET 8, self-contained executable) **hoặc** Go (single binary)
- **Lý do:** Tích hợp native với Windows API (WMI, COM, Registry, PowerShell runspace), hỗ trợ Windows Service lifecycle.

### 4.2 Các thành phần

```
PolicyCollector/
├── src/
│   ├── Collectors/
│   │   ├── GpoCollector.cs          # gpresult XML + WMI RSOP
│   │   ├── SecurityPolicyCollector.cs  # secedit, auditpol, registry
│   │   ├── FirewallCollector.cs     # netsh / Get-NetFirewallRule
│   │   ├── AppInventoryCollector.cs # registry + appx
│   │   ├── ServiceCollector.cs      # SCM
│   │   └── ScheduledTaskCollector.cs
│   ├── Transport/
│   │   ├── HttpTransport.cs         # HTTPS POST với retry + backoff
│   │   └── LocalQueue.cs            # SQLite offline buffer
│   ├── Scheduler/
│   │   └── CollectionScheduler.cs   # Timer-based, configurable interval
│   ├── Config/
│   │   └── AgentConfig.cs           # appsettings.json reader
│   └── Program.cs                   # Windows Service host
├── installer/
│   └── setup.iss                    # Inno Setup script
├── appsettings.json                 # Cấu hình mặc định
└── PolicyCollector.csproj
```

### 4.3 Luồng hoạt động

```
[Khởi động Service]
        │
        ▼
[Đọc appsettings.json]
        │
        ▼
[Scheduler tick mỗi N phút]
        │
        ├──► GpoCollector.Collect()
        ├──► SecurityPolicyCollector.Collect()
        ├──► FirewallCollector.Collect()
        ├──► AppInventoryCollector.Collect()
        ├──► ServiceCollector.Collect()
        └──► ScheduledTaskCollector.Collect()
                │
                ▼
        [Merge thành CollectionPayload]
                │
                ▼
        [HttpTransport.Send() — HTTPS POST]
                │
         ┌──────┴──────┐
      Success       Failure
         │               │
         ▼               ▼
    [Log OK]    [Enqueue LocalQueue (SQLite)]
                         │
                         ▼
                  [Retry khi có mạng]
```

---

## 5. Cấu hình Agent (`appsettings.json`)

```json
{
  "Collector": {
    "IntervalMinutes": 60,
    "CollectOnStartup": true,
    "Modules": {
      "GPO": true,
      "SecurityPolicy": true,
      "Firewall": true,
      "AppInventory": true,
      "Services": true,
      "ScheduledTasks": true
    }
  },
  "Transport": {
    "BackendUrl": "https://collector.internal/api/v1/ingest",
    "ApiKey": "{{API_KEY}}",
    "TimeoutSeconds": 30,
    "MaxRetries": 5,
    "RetryBackoffSeconds": 60,
    "UseMtls": false,
    "ClientCertThumbprint": ""
  },
  "Logging": {
    "Level": "Information",
    "MaxFileSizeMb": 10,
    "RetainDays": 7
  }
}
```

> Cấu hình có thể phân phối qua **GPO Administrative Template (.admx)** để quản lý tập trung.

---

## 6. Bảo mật Transport

| Yêu cầu | Cách thực hiện |
|---|---|
| Mã hoá kênh truyền | HTTPS / TLS 1.2+ bắt buộc |
| Xác thực agent | API Key trong header `X-Api-Key` (tối thiểu) |
| Xác thực 2 chiều | mTLS — agent dùng client certificate cấp bởi CA nội bộ |
| Toàn vẹn payload | HMAC-SHA256 trên body, Backend xác minh trước khi lưu |
| Offline buffer | SQLite local, dữ liệu chờ retry khi mất kết nối |
| Không lưu secret rõ | API Key lưu trong Windows Credential Manager, không plain-text |

---

## 7. Định dạng Payload (JSON)

```json
{
  "collection_id": "550e8400-e29b-41d4-a716-446655440000",
  "collected_at": "2026-05-12T10:00:00Z",
  "host": {
    "hostname": "WS-FINANCE-01",
    "domain": "corp.local",
    "os_version": "Windows 11 Pro 22H2 (22621)",
    "agent_version": "1.0.0"
  },
  "gpo": {
    "applied_gpos": [
      { "name": "Default Domain Policy", "guid": "{31B2F340-...}", "link": "DC=corp,DC=local", "applied_at": "2026-05-12T08:30:00Z" }
    ],
    "last_refresh": "2026-05-12T08:30:00Z"
  },
  "security_policy": {
    "password": { "min_length": 12, "complexity": true, "max_age_days": 90, "lockout_threshold": 5 },
    "audit": { "logon": "Success,Failure", "privilege_use": "Failure" },
    "uac_level": 2,
    "bitlocker": [{ "volume": "C:", "status": "FullyEncrypted", "method": "XtsAes256" }],
    "defender": { "real_time_protection": true, "cloud_protection": true }
  },
  "firewall": {
    "profiles": {
      "domain": { "enabled": true, "default_inbound": "Block" },
      "private": { "enabled": true, "default_inbound": "Block" },
      "public":  { "enabled": true, "default_inbound": "Block" }
    },
    "rules_count": 142
  },
  "applications": [
    { "name": "Google Chrome", "version": "124.0.6367.82", "publisher": "Google LLC", "architecture": "x64" }
  ],
  "services": [
    { "name": "WinRM", "status": "Running", "startup_type": "Automatic", "binary_path": "C:\\Windows\\System32\\svchost.exe" }
  ]
}
```

---

## 8. Collector Backend

### 8.1 Công nghệ đề xuất

| Thành phần | Lựa chọn |
|---|---|
| Ingest API | ASP.NET Core 8 / Go Fiber / FastAPI |
| Database | PostgreSQL (dữ liệu cấu hình) + TimescaleDB (timeseries) |
| Message Queue | Redis Streams hoặc RabbitMQ (buffer ingest) |
| Dashboard | Grafana + PostgreSQL datasource |
| Alerting | Grafana Alerts / Alertmanager |

### 8.2 API Endpoints

| Method | Path | Mô tả |
|---|---|---|
| `POST` | `/api/v1/ingest` | Nhận payload từ agent |
| `GET` | `/api/v1/hosts` | Danh sách máy đã check-in |
| `GET` | `/api/v1/hosts/{hostname}/latest` | Snapshot mới nhất của một máy |
| `GET` | `/api/v1/hosts/{hostname}/diff` | So sánh 2 snapshot theo thời gian |
| `GET` | `/api/v1/policy/violations` | Các máy vi phạm policy baseline |
| `GET` | `/api/v1/apps/inventory` | Tổng hợp inventory ứng dụng toàn hệ thống |

---

## 9. Use Cases quản lý

| Tình huống | Cách hệ thống hỗ trợ |
|---|---|
| Phát hiện GPO không được áp dụng | So sánh danh sách GPO expected vs actual trên endpoint |
| Phát hiện ứng dụng lạ / chưa được phê duyệt | So sánh app inventory với whitelist |
| Cảnh báo mật khẩu policy yếu | Alert khi `min_length < 12` hoặc `complexity = false` |
| Tracking thay đổi cấu hình theo thời gian | Diff giữa 2 snapshot liên tiếp |
| Compliance report | Export danh sách máy đạt/không đạt chuẩn CIS |
| Phát hiện service/task lạ | So sánh với baseline đã phê duyệt |

---

## 10. Kế hoạch triển khai

### Phase 1 — Agent MVP (4 tuần)
- [ ] Thiết lập project C# .NET 8 Windows Service
- [ ] GPO Collector (`gpresult /X` parser)
- [ ] Security Policy Collector (`secedit`, `auditpol`, Registry)
- [ ] App Inventory Collector (Registry Uninstall + Appx)
- [ ] HTTP Transport + API Key auth
- [ ] Local SQLite offline queue
- [ ] Installer (MSI / Inno Setup)

### Phase 2 — Backend & Dashboard (4 tuần)
- [ ] Ingest API (POST /ingest với validation + HMAC)
- [ ] PostgreSQL schema + TimescaleDB hypertable
- [ ] REST API endpoints (hosts, latest, diff)
- [ ] Grafana dashboard cơ bản (host list, last seen, GPO status)
- [ ] Alerting rule: offline > 2 giờ, password policy vi phạm

### Phase 3 — Mở rộng & Hardening (3 tuần)
- [ ] mTLS cho transport
- [ ] GPO Administrative Template (.admx) để deploy config qua GPO
- [ ] Firewall Collector chi tiết (rules dump)
- [ ] Scheduled Task + Service Collector
- [ ] Policy Baseline engine (so sánh với CIS Benchmark)
- [ ] Diff view trên dashboard
- [ ] Packaging: silent install qua GPO Software Deployment / SCCM / Intune

### Phase 4 — Production (ongoing)
- [ ] Rollout theo OU (pilot → toàn bộ domain)
- [ ] Monitoring agent health (last check-in, error rate)
- [ ] Rotation API key / certificate
- [ ] Versioned agent update mechanism

---

## 11. Yêu cầu phi chức năng

| Yêu cầu | Chỉ tiêu |
|---|---|
| Tài nguyên agent | CPU < 2% (khi collect), RAM < 50 MB |
| Thời gian collect | < 30 giây / lần cho toàn bộ modules |
| Độ trễ ingest | Backend xử lý < 500 ms / payload |
| Tính sẵn sàng Backend | 99.5% uptime (có offline queue trên agent) |
| Bảo mật dữ liệu | TLS 1.2+, API Key / mTLS, không log sensitive value |
| Kiểm toán | Mọi ingest request được ghi audit log (hostname, timestamp, IP) |

---

## 12. Phụ lục — Nguồn dữ liệu Windows

| Module | Lệnh / API |
|---|---|
| GPO result | `gpresult /X report.xml /SCOPE COMPUTER` |
| RSoP (WMI) | `Win32_RSOP_SettingStatus`, `RSOP_Namespace` |
| Password policy | `net accounts`, `secedit /export /areas SECURITYPOLICY` |
| Audit policy | `auditpol /get /category:* /r` |
| User rights | `secedit /export /areas USER_RIGHTS` |
| Firewall | `netsh advfirewall`, `Get-NetFirewallRule` |
| Defender | `Get-MpPreference`, `Get-MpComputerStatus` |
| BitLocker | `manage-bde -status`, `Get-BitLockerVolume` |
| Registry TLS | `HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL` |
| App Uninstall | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` |
| Appx | `Get-AppxPackage -AllUsers` |
| Services | `Get-Service`, `Get-CimInstance Win32_Service` |
| Scheduled Tasks | `Get-ScheduledTask`, `schtasks /query /fo CSV /v` |
| Startup | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` |
