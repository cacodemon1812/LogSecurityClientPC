# AI Context Index — PolicyCollector

> Entry point cho AI assistant. Đọc file này trước, sau đó tra cứu theo mục cần thiết.

## Dự án là gì?

**PolicyCollector** là Windows service chạy trên endpoint (máy trạm / máy chủ Windows), định kỳ thu thập cấu hình GPO, chính sách bảo mật, ứng dụng đã cài đặt rồi đẩy về hệ thống thu thập trung tâm.

## Cấu trúc Context

| File | Nội dung | Khi nào đọc |
|---|---|---|
| [00-overview.md](00-overview.md) | Mục tiêu, phạm vi, non-goals, success criteria | Trước mọi quyết định thiết kế |
| [01-architecture.md](01-architecture.md) | Sơ đồ hệ thống, luồng dữ liệu, thành phần | Trước khi viết code mới |
| [02-data-models.md](02-data-models.md) | JSON payload, DB schema, field catalog | Khi làm việc với dữ liệu |
| [03-tech-stack.md](03-tech-stack.md) | Công nghệ chọn + lý do, alternatives | Trước khi thêm dependency |
| [04-agent-design.md](04-agent-design.md) | Collector modules, scheduler, transport | Khi phát triển Agent |
| [05-backend-design.md](05-backend-design.md) | Ingest API, storage, dashboard | Khi phát triển Backend |
| [06-security-model.md](06-security-model.md) | Threat model, auth, secrets | Khi làm việc về bảo mật |
| [07-deployment.md](07-deployment.md) | Build, package, install, GPO/Intune rollout | Khi deploy hoặc packaging |
| [08-upgrade-path.md](08-upgrade-path.md) | Versioning, migration, backward compat | Khi release phiên bản mới |
| [09-roadmap.md](09-roadmap.md) | Phase, milestone, backlog | Khi lên kế hoạch sprint |
| [10-adr/](10-adr/) | Architecture Decision Records | Khi hiểu lý do quyết định |
| [11-glossary.md](11-glossary.md) | Thuật ngữ domain | Khi gặp từ không rõ |

## Quy tắc quan trọng cho AI

- **Ngôn ngữ chính:** C# .NET 8, self-contained exe, không cần runtime cài sẵn.
- **Không** thêm dependency ngoài danh sách trong [03-tech-stack.md](03-tech-stack.md) mà không ghi ADR.
- **Không** thay đổi payload schema mà không cập nhật [02-data-models.md](02-data-models.md).
- Mọi collector phải implement interface `ICollector` — xem [04-agent-design.md](04-agent-design.md).
- Transport layer là **duy nhất** điểm gọi HTTP ra ngoài.
- Không log giá trị nhạy cảm (API key, password hash, certificate private key).

## Trạng thái hiện tại

- **Phase:** 1 — Agent MVP (đang phát triển)
- **Phiên bản Agent:** 0.1.0-alpha
- **Backend:** Chưa bắt đầu
- **Cập nhật context lần cuối:** 2026-05-12
