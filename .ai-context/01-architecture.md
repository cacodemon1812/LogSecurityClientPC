# 01 — Kiến trúc Hệ thống

## Sơ đồ tổng thể

```
╔══════════════════════════════════════════════════════════════════╗
║                        ENDPOINT ZONE                            ║
║                                                                  ║
║  ┌────────────────────────────────────────────────────────────┐  ║
║  │              PolicyCollector Service                       │  ║
║  │                                                            │  ║
║  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────┐ │  ║
║  │  │   GPO    │ │ SecPolicy│ │   Apps   │ │   Services   │ │  ║
║  │  │Collector │ │Collector │ │Collector │ │  Collector   │ │  ║
║  │  └────┬─────┘ └────┬─────┘ └────┬─────┘ └──────┬───────┘ │  ║
║  │       └────────────┴────────────┴───────────────┘         │  ║
║  │                             │                             │  ║
║  │                    ┌────────▼────────┐                    │  ║
║  │                    │  CollectionJob  │ ◄── Scheduler      │  ║
║  │                    └────────┬────────┘                    │  ║
║  │                             │                             │  ║
║  │                    ┌────────▼────────┐                    │  ║
║  │                    │  Payload Builder│                    │  ║
║  │                    └────────┬────────┘                    │  ║
║  │                             │                             │  ║
║  │              ┌──────────────▼──────────────┐              │  ║
║  │              │       HttpTransport          │              │  ║
║  │              │  Retry + Exponential Backoff │              │  ║
║  │              └──────────┬──────┬────────────┘              │  ║
║  │                         │      │ (on failure)              │  ║
║  │                      OK │   ┌──▼──────────┐               │  ║
║  │                         │   │ LocalQueue  │ (SQLite)       │  ║
║  │                         │   └─────────────┘               │  ║
║  └─────────────────────────┼────────────────────────────────┘  ║
║                             │ HTTPS/TLS 1.2+                   ║
╚═════════════════════════════╪════════════════════════════════════╝
                              │
╔═════════════════════════════╪════════════════════════════════════╗
║                   BACKEND ZONE                                   ║
║                             │                                    ║
║               ┌─────────────▼─────────────┐                     ║
║               │       Ingest API           │                     ║
║               │  Auth → Validate → Enqueue │                     ║
║               └─────────────┬─────────────┘                     ║
║                             │                                    ║
║               ┌─────────────▼─────────────┐                     ║
║               │     Message Queue          │                     ║
║               │  (Redis Streams / RabbitMQ)│                     ║
║               └──────┬──────────┬──────────┘                    ║
║                      │          │                                ║
║          ┌───────────▼──┐  ┌────▼───────────┐                  ║
║          │ Storage Worker│  │  Alert Worker  │                  ║
║          └───────────┬──┘  └────┬───────────┘                  ║
║                      │          │                                ║
║          ┌───────────▼──┐  ┌────▼───────────┐                  ║
║          │  PostgreSQL   │  │  Alertmanager  │                  ║
║          │  + Timescale  │  │  (email/slack) │                  ║
║          └───────────┬──┘  └────────────────┘                  ║
║                      │                                           ║
║          ┌───────────▼──────────┐                               ║
║          │  Grafana Dashboard   │                                ║
║          │  REST Query API      │                                ║
║          └──────────────────────┘                               ║
╚══════════════════════════════════════════════════════════════════╝
```

## Luồng dữ liệu chính

### Luồng thu thập bình thường
```
1. Scheduler tick (mỗi N phút, default 60)
2. CollectionJob khởi chạy tất cả Collector song song
3. Mỗi Collector trả về CollectorResult<T>
4. PayloadBuilder merge tất cả result + system metadata
5. HttpTransport POST payload lên Backend Ingest API
6. Backend xác thực → enqueue vào Message Queue
7. StorageWorker consumer lưu vào PostgreSQL
8. AlertWorker consumer kiểm tra violations → gửi alert nếu cần
```

### Luồng offline / retry
```
1. HttpTransport gọi Backend → timeout hoặc lỗi network
2. Payload được serialized lưu vào SQLite (LocalQueue)
3. BackgroundRetryJob poll LocalQueue mỗi 5 phút
4. Khi network phục hồi, retry gửi theo FIFO
5. Thành công → xóa khỏi LocalQueue
6. LocalQueue tối đa giữ 7 ngày dữ liệu
```

### Luồng cập nhật cấu hình Agent
```
1. Backend Config API trả về config mới (version > current)
2. Agent nhận response header X-Config-Version
3. Agent gọi GET /api/v1/config để lấy config mới
4. Áp dụng ngay (không cần restart service) cho các setting runtime
5. Setting cần restart (intervals, modules) → ghi file + schedule restart
```

## Thành phần và trách nhiệm

| Thành phần | Trách nhiệm | Ranh giới |
|---|---|---|
| `ICollector<T>` | Thu thập một loại dữ liệu | Chỉ đọc, không ghi |
| `CollectionJob` | Orchestrate collectors, build payload | Không gọi HTTP trực tiếp |
| `Scheduler` | Trigger CollectionJob theo thời gian | Không biết logic thu thập |
| `HttpTransport` | Gửi HTTP, retry, backoff | Không biết nội dung payload |
| `LocalQueue` | Buffer SQLite khi offline | Không biết logic retry |
| `AgentConfig` | Đọc/validate cấu hình | Immutable sau khi load |
| `Ingest API` | Nhận, xác thực, enqueue | Không lưu DB trực tiếp |
| `StorageWorker` | Consumer queue → lưu DB | Không có logic alert |
| `AlertWorker` | Consumer queue → kiểm tra violations | Không lưu DB |

## Quyết định thiết kế quan trọng

- **Agent chạy dưới SYSTEM account** — cần để đọc GPO, secedit, WMI RSOP.
- **Collector song song** — mỗi collector độc lập, lỗi một cái không ảnh hưởng cái khác.
- **Payload gửi nguyên bản** — không lọc hay transform trên agent, logic đó thuộc Backend.
- **SQLite cho offline queue** — không phụ thuộc external service, đủ nhỏ cho endpoint.
- **Ingest API stateless** — scale horizontal dễ, không giữ session.

Chi tiết từng quyết định: xem [10-adr/](10-adr/).
