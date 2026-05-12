# 09 — Roadmap

## Phase 1 — Agent MVP `v0.1` (4 tuần)

**Mục tiêu:** Agent có thể thu thập và gửi dữ liệu cơ bản.

### Tuần 1-2: Core infrastructure

- [ ] Tạo project `PolicyCollector.Agent` (.NET 8 Worker Service)
- [ ] Implement `ICollector<T>` interface + `CollectorResult<T>`
- [ ] Implement `CollectionScheduler` với `PeriodicTimer`
- [ ] Implement `HttpTransport` (POST + API key header)
- [ ] Implement `LocalQueue` (SQLite offline buffer)
- [ ] Implement `RetryJob` (poll queue mỗi 5 phút)
- [ ] Strongly-typed `AgentOptions` từ appsettings.json
- [ ] Serilog rolling file logger
- [ ] Windows Service host (`UseWindowsService()`)

### Tuần 3: Collectors cơ bản

- [ ] `HostInfoCollector` — hostname, domain, OS version, IP
- [ ] `GpoCollector` — parse gpresult /X XML
- [ ] `SecurityPolicyCollector` — secedit, auditpol, registry UAC/TLS/RDP
- [ ] `AppInventoryCollector` — registry Uninstall keys (HKLM x64 + x86)
- [ ] `ServiceCollector` — ServiceController + WMI Win32_Service
- [ ] Unit tests cho từng collector (mock registry / file input)

### Tuần 4: Packaging & testing

- [ ] WiX MSI installer với silent install properties
- [ ] Custom Action: lưu API key vào Credential Manager
- [ ] `PowerShellRunner` helper (PS runspace pool)
- [ ] `FirewallCollector` (Get-NetFirewallProfile/Rule via PS)
- [ ] Integration test: cài trên VM, verify payload JSON
- [ ] HMAC-SHA256 signing trên transport

**Deliverable:** MSI cài được, agent gửi payload JSON chuẩn lên mock Backend.

---

## Phase 2 — Backend & Dashboard `v0.2` (4 tuần)

**Mục tiêu:** Backend nhận, lưu trữ, query và hiển thị dữ liệu.

### Tuần 5-6: Ingest & Storage

- [ ] Tạo project `PolicyCollector.Backend` (ASP.NET Core 8 Minimal API)
- [ ] `POST /api/v1/ingest` endpoint
- [ ] `ApiKeyMiddleware` (validate + rate limit)
- [ ] `HmacValidationMiddleware`
- [ ] PostgreSQL schema + TimescaleDB migrations (DbUp)
- [ ] `StorageWorker` (Redis Streams consumer → PostgreSQL)
- [ ] `GET /api/v1/hosts` endpoint
- [ ] `GET /api/v1/hosts/{hostname}/latest` endpoint
- [ ] Docker Compose dev environment
- [ ] Health check endpoints

### Tuần 7: Query & Alert

- [ ] `DiffService` — so sánh 2 snapshot
- [ ] `GET /api/v1/hosts/{hostname}/diff` endpoint
- [ ] `ViolationEngine` — rule set cơ bản (password, firewall, defender, UAC)
- [ ] `AlertWorker` (Redis Streams consumer → check violations)
- [ ] `GET /api/v1/policy/violations` endpoint
- [ ] Alertmanager webhook integration (email)

### Tuần 8: Dashboard & App Inventory

- [ ] Grafana dashboard: host list, last seen, status
- [ ] Grafana dashboard: violation summary
- [ ] `GET /api/v1/apps/inventory` endpoint
- [ ] Grafana dashboard: app inventory panel
- [ ] End-to-end test: Agent → Backend → Dashboard

**Deliverable:** Full pipeline hoạt động trên dev environment, dashboard hiển thị dữ liệu real.

---

## Phase 3 — Hardening & More Collectors `v1.0` (3 tuần)

**Mục tiêu:** Production-ready, thêm collectors, deployment qua GPO.

### Tuần 9-10: Collectors nâng cao & GPO ADMX

- [ ] `DefenderCollector` (Get-MpPreference + Get-MpComputerStatus)
- [ ] `BitLockerCollector` (Get-BitLockerVolume)
- [ ] `ScheduledTaskCollector` (Get-ScheduledTask)
- [ ] `AppxCollector` (Get-AppxPackage -AllUsers)
- [ ] `StartupCollector` (Run registry keys)
- [ ] GPO ADMX template (BackendUrl, IntervalMinutes, Modules)
- [ ] Registry config override (GPO > appsettings)
- [ ] mTLS support (client certificate từ Credential Store)

### Tuần 11: Production deployment & monitoring

- [ ] Kubernetes manifests (Backend + Workers)
- [ ] Staging rollout (5 pilot endpoints)
- [ ] Monitor: agent uptime, last_seen stats, error rate
- [ ] Agent update notification (X-Latest-Agent-Version header)
- [ ] Violation rule admin API (CRUD rules)
- [ ] Compliance report export (CSV, PDF)
- [ ] Runbook: deploy, rollback, troubleshoot

**Deliverable:** v1.0.0 production release. GPO deployment rollout bắt đầu.

---

## Phase 4 — Scale & Advanced Features (ongoing)

| Feature | Mức độ ưu tiên | Ghi chú |
|---|---|---|
| CIS Benchmark rule set | High | Mapping với CIS Windows 10/11 Benchmark |
| Config drift baseline | High | So sánh với "golden" snapshot per OU |
| SCCM / Intune integration | Medium | Push collection trigger từ Management |
| Geo-aware Backend | Medium | Multi-region nếu nhiều site |
| Agent auto-update | Low | Cân nhắc security risk |
| REST API cho SIEM integration | Medium | Forward violations sang Splunk/QRadar |
| Role-based access (RBAC) | Medium | Phân quyền xem theo OU/Domain |
| Anomaly detection (ML) | Low | Phát hiện pattern bất thường |

---

## Backlog (chưa prioritize)

- Hỗ trợ thu thập từ domain controller (SYSVOL, replication status)
- Thu thập LAPS (Local Administrator Password Solution) status
- Tích hợp Active Directory: map hostname → OU → Owner
- Export report theo CIS Controls framework
- Mobile dashboard (Grafana mobile)
- Alert qua Teams / Telegram (ngoài email)
- Per-machine policy exception list (whitelist app per hostname)
