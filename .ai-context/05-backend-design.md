# 05 — Backend Design

## Cấu trúc thư mục

```
src/PolicyCollector.Backend/
├── Api/
│   ├── Endpoints/
│   │   ├── IngestEndpoint.cs          # POST /api/v1/ingest
│   │   ├── HostsEndpoint.cs           # GET /api/v1/hosts
│   │   ├── SnapshotEndpoint.cs        # GET /api/v1/hosts/{hostname}/latest
│   │   ├── DiffEndpoint.cs            # GET /api/v1/hosts/{hostname}/diff
│   │   ├── ViolationsEndpoint.cs      # GET /api/v1/policy/violations
│   │   └── InventoryEndpoint.cs       # GET /api/v1/apps/inventory
│   ├── Middleware/
│   │   ├── ApiKeyMiddleware.cs
│   │   ├── HmacValidationMiddleware.cs
│   │   └── RequestLoggingMiddleware.cs
│   └── Models/                        # Request/Response DTOs
├── Workers/
│   ├── StorageWorker.cs               # Redis consumer → PostgreSQL
│   └── AlertWorker.cs                 # Redis consumer → check violations
├── Services/
│   ├── ViolationEngine.cs             # So sánh snapshot với policy baseline
│   ├── DiffService.cs                 # Diff 2 snapshots
│   └── HostStatusService.cs          # Track online/offline
├── Data/
│   ├── Repositories/
│   │   ├── SnapshotRepository.cs
│   │   ├── ViolationRepository.cs
│   │   └── HostRepository.cs
│   └── Migrations/                    # SQL migration files
├── Config/
│   └── BackendOptions.cs
└── Program.cs
```

---

## API Endpoints

### POST /api/v1/ingest

```
Auth:    X-Api-Key header (required)
         X-Hmac-SHA256 header (optional, enforced nếu HMAC_REQUIRED=true)

Request:  CollectionPayload JSON (max 10 MB)

Response:
  202 Accepted    → { "ingestion_id": "<uuid>" }
  400 Bad Request → { "error": "...", "field": "..." }
  401             → { "error": "Invalid API key" }
  422             → { "error": "Schema version not supported", "supported": "1.x" }
  429             → Retry-After: 60
  503             → Queue unavailable

Flow:
  1. Validate API key
  2. Validate HMAC nếu bật
  3. Deserialize + validate payload (schema version, required fields, size)
  4. Enqueue vào Redis Stream "ingest:queue"
  5. Trả 202 ngay — không chờ lưu DB
```

### GET /api/v1/hosts

```
Query params:
  ?domain=corp.local          Filter theo domain
  ?status=online|offline      Filter theo trạng thái
  ?page=1&size=50             Pagination (max 200)
  ?sort=last_seen&order=desc

Response: { "total": 150, "items": [HostSummary] }

HostSummary:
  hostname, domain, os_version, agent_version,
  last_seen, status (online nếu last_seen < 2h, offline nếu > 24h),
  violation_count (open violations)
```

### GET /api/v1/hosts/{hostname}/latest

```
Response: CollectionPayload đầy đủ của snapshot mới nhất

404 nếu hostname chưa check-in bao giờ
```

### GET /api/v1/hosts/{hostname}/diff

```
Query params:
  ?from=<ISO-8601>    (default: snapshot trước latest)
  ?to=<ISO-8601>      (default: latest)

Response:
{
  "hostname": "...",
  "from_snapshot": "<uuid>",
  "to_snapshot": "<uuid>",
  "from_time": "...",
  "to_time": "...",
  "changes": [
    {
      "field_path": "security_policy.password.min_length",
      "old_value": "8",
      "new_value": "12",
      "changed_at": "..."
    }
  ]
}
```

### GET /api/v1/policy/violations

```
Query params:
  ?hostname=...       Filter
  ?severity=critical  Filter
  ?resolved=false     Default: false
  ?rule_id=...        Filter theo rule
  ?page=1&size=50

Response: { "total": 30, "items": [ViolationEntry] }
```

### GET /api/v1/apps/inventory

```
Query params:
  ?name=chrome        Tìm kiếm tên app (ILIKE)
  ?publisher=google   Filter publisher
  ?hostname=...       Chỉ máy này
  ?page=1&size=100

Response: Danh sách app + số máy đang cài
```

---

## Storage Worker

```
Consumer group: "storage-workers" trên Redis Stream "ingest:queue"

Mỗi message:
  1. Deserialize payload
  2. INSERT INTO collection_snapshots (upsert on conflict collection_id)
  3. UPSERT host_latest
  4. Compute diff với snapshot trước → INSERT config_changes
  5. ACK message

Error handling:
  - Deserialize fail → dead letter stream "ingest:dead"
  - DB error → retry 3 lần, sau đó dead letter
  - Dead letter được xử lý bởi ops team
```

---

## Alert Worker

```
Consumer group: "alert-workers" trên Redis Stream "ingest:queue"
(Đọc cùng stream nhưng consumer group khác — song song với Storage Worker)

Mỗi message:
  1. Deserialize payload
  2. Chạy ViolationEngine.Evaluate(snapshot)
  3. INSERT violations mới vào DB
  4. Nếu có violation critical/high → gửi alert qua Alertmanager webhook
  5. ACK message
```

---

## Violation Engine — Rule Set mặc định

| Rule ID | Severity | Điều kiện vi phạm |
|---|---|---|
| `password.min_length` | High | `min_length < 8` |
| `password.complexity` | High | `complexity_enabled = false` |
| `password.max_age` | Medium | `max_age_days > 180` hoặc `= 0` (no expiry) |
| `password.lockout` | High | `lockout_threshold = 0` (no lockout) |
| `audit.logon` | High | `logon_logoff` không bao gồm `Failure` |
| `firewall.disabled` | Critical | Bất kỳ profile nào `enabled = false` |
| `defender.realtime` | Critical | `real_time_protection = false` |
| `uac.disabled` | Critical | `uac.enabled = false` |
| `bitlocker.os_volume` | High | Volume C: không phải `FullyEncrypted` |
| `tls.weak_protocol` | High | `TLS_1_0 = true` hoặc `SSL_3_0 = true` |
| `rdp.nla_disabled` | High | `rdp.enabled = true` AND `nla_required = false` |
| `gpo.not_applied` | Medium | GPO expected nhưng không có trong applied list |
| `agent.offline` | Medium | `last_seen > 24h` (từ HostStatusService) |

Rules được load từ DB (bảng `policy_rules`) → có thể customize qua API/UI.

---

## Database Migration Strategy

```
Tool: DbUp (C# migration runner, không dùng EF Migrations)
Format: SQL files đánh số: 001_initial.sql, 002_add_violations.sql ...
Chạy migration: tự động khi Backend khởi động (idempotent)
Rollback: viết tay rollback script (không auto)
```

---

## Docker Compose (Dev)

```yaml
version: "3.9"
services:
  postgres:
    image: timescale/timescaledb:latest-pg16
    environment:
      POSTGRES_DB: policycollector
      POSTGRES_PASSWORD: devpassword
    ports: ["5432:5432"]
    volumes: ["pgdata:/var/lib/postgresql/data"]

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]

  backend:
    build: ./src/PolicyCollector.Backend
    environment:
      ConnectionStrings__Postgres: "Host=postgres;Database=policycollector;Password=devpassword"
      ConnectionStrings__Redis: "redis:6379"
      Backend__ApiKey: "dev-api-key-change-in-prod"
    ports: ["8080:8080"]
    depends_on: [postgres, redis]

  grafana:
    image: grafana/grafana:latest
    ports: ["3000:3000"]
    volumes: ["grafana_data:/var/lib/grafana"]

volumes:
  pgdata:
  grafana_data:
```
