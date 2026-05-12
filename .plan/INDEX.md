# .plan — Kế hoạch Triển khai PolicyCollector

> Mỗi file plan = 1 sprint nhỏ, độc lập, gen-code được ngay.
> Đọc [../.ai-context/CONTEXT.md](../.ai-context/CONTEXT.md) trước khi bắt đầu bất kỳ plan nào.

## Thứ tự thực hiện (dependency order)

```
P1 → P2 → P3 → P4          (Agent — phải làm theo thứ tự)
          P3 → P5 → P6 → P7 → P8   (Backend — bắt đầu sau P3)
P4 + P8 → P9                (Hardening — sau khi cả 2 sides xong)
```

## Danh sách Plan

| File | Tên | Phase | Phụ thuộc | Ước tính |
|---|---|---|---|---|
| [P1-agent-foundation.md](P1-agent-foundation.md) | Agent: Project + Core Infrastructure | 1 | — | 2 ngày |
| [P2-agent-collectors.md](P2-agent-collectors.md) | Agent: Collectors (GPO, SecPolicy, Apps, Services) | 1 | P1 | 3 ngày |
| [P3-agent-transport.md](P3-agent-transport.md) | Agent: Transport + LocalQueue + Scheduler | 1 | P1 | 2 ngày |
| [P4-agent-installer.md](P4-agent-installer.md) | Agent: WiX MSI Installer + ADMX | 1 | P2, P3 | 1 ngày |
| [P5-backend-ingest.md](P5-backend-ingest.md) | Backend: Project + Ingest API + DB Migration | 2 | P1 (models) | 2 ngày |
| [P6-backend-workers.md](P6-backend-workers.md) | Backend: StorageWorker + AlertWorker + ViolationEngine | 2 | P5 | 2 ngày |
| [P7-backend-query-api.md](P7-backend-query-api.md) | Backend: Query Endpoints + Diff + Inventory | 2 | P6 | 2 ngày |
| [P8-backend-infra.md](P8-backend-infra.md) | Backend: Docker Compose + Grafana + Health | 2 | P5 | 1 ngày |
| [P9-hardening.md](P9-hardening.md) | Hardening: mTLS, Collectors nâng cao, GPO ADMX | 3 | P4, P8 | 3 ngày |

## Quy ước trong plan files

```
[FILE] path/to/File.cs          → File cần tạo mới
[EDIT] path/to/File.cs          → File cần sửa (đã có)
[TEST] path/to/FileTests.cs     → File test
[SQL]  path/to/001_name.sql     → SQL migration
[CONF] path/to/file.json        → Config / static file

▸ public interface / class signature  → Ký hiệu phải implement chính xác
  ··· body                            → Phần body cần gen
```

## Trạng thái

| Plan | Status | Ghi chú |
|---|---|---|
| P1 | ⬜ Chưa bắt đầu | |
| P2 | ⬜ Chưa bắt đầu | |
| P3 | ⬜ Chưa bắt đầu | |
| P4 | ⬜ Chưa bắt đầu | |
| P5 | ⬜ Chưa bắt đầu | |
| P6 | ⬜ Chưa bắt đầu | |
| P7 | ⬜ Chưa bắt đầu | |
| P8 | ⬜ Chưa bắt đầu | |
| P9 | ⬜ Chưa bắt đầu | |
