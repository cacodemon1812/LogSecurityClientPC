# ADR-004 — Offline Buffer: SQLite LocalQueue

**Status:** Accepted  
**Date:** 2026-05-12

## Context

Agent phải hoạt động khi mất kết nối mạng đến Backend. Dữ liệu không được mất — phải buffer và retry khi mạng phục hồi.

## Quyết định

**SQLite database local trên endpoint cho offline queue (outbox pattern).**

## Lý do

- **Zero external dependency:** Không cần service, port, hoặc network để hoạt động.
- **Nhỏ gọn:** SQLite binary ~1.5 MB, in-process, không tiêu tài nguyên khi idle.
- **ACID:** Đảm bảo payload không bị mất khi machine shutdown đột ngột (WAL mode).
- **Đủ dùng:** Offline queue chỉ cần FIFO operations đơn giản — không cần Redis/RabbitMQ.
- **Giới hạn rõ ràng:** Max 1000 entries, drop oldest — tránh disk full trên endpoint.

## Alternatives đã xem xét

| | SQLite | File System (JSON files) | In-memory Queue | Windows MSMQ |
|---|---|---|---|---|
| Persistence qua restart | Đảm bảo (WAL) | Có (file) | Không | Có |
| Atomic operations | Full ACID | Không | N/A | Có |
| Size control | Dễ (SQL) | Phức tạp | N/A | Phức tạp |
| Dependencies | 0 (embedded) | 0 | 0 | Windows Feature |
| Query/ordering | SQL | Filesystem ordering | N/A | Limited |

## Hệ quả

- SQLite database tại: `C:\ProgramData\PolicyCollector\queue.db`
- File ACL: chỉ SYSTEM có write access.
- WAL mode bật mặc định để tránh corruption khi power loss.
- Retry interval: 5 phút, không exponential backoff (volume nhỏ).
- TTL: 7 ngày — payload quá cũ không còn ý nghĩa.
