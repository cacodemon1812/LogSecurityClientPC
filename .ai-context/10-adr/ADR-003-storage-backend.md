# ADR-003 — Backend Storage: PostgreSQL + TimescaleDB + JSONB

**Status:** Accepted  
**Date:** 2026-05-12

## Context

Backend cần lưu trữ:
1. Payload cấu hình — schema linh hoạt, thay đổi theo agent version
2. Timeseries queries — "cấu hình máy X lúc T?", "khi nào thay đổi?"
3. Aggregation — "bao nhiêu máy có Defender tắt?"
4. Full-text search qua app inventory

## Quyết định

**PostgreSQL 16 + TimescaleDB extension + JSONB column cho payload.**

## Lý do

- **JSONB:** Lưu payload nguyên vẹn, không cần migration khi thêm field mới. GIN index cho query nhanh vào nested JSON.
- **TimescaleDB:** Tự động partition theo time (hypertable), query `WHERE collected_at BETWEEN ...` cực nhanh mà không cần thêm complexity.
- **PostgreSQL JSONB + SQL:** Có thể mix structured query và JSON query trong 1 statement — không cần 2 DB.
- **Operational simplicity:** 1 database duy nhất, không cần learn Elasticsearch hay InfluxDB.
- **Dapper + raw SQL:** JSONB query phức tạp viết tốt hơn bằng SQL thẳng hơn ORM.

## Alternatives đã xem xét

| | PostgreSQL + JSONB | MongoDB | Elasticsearch | InfluxDB + PostgreSQL |
|---|---|---|---|---|
| Schema flexible | JSONB | Native | Native | Influx rigid, PG flexible |
| Timeseries | TimescaleDB plugin | Cần thêm | Cần thêm | Tốt (Influx) |
| SQL support | Full | Không | Giới hạn | Giới hạn |
| Operational | 1 service | 1 service | Complex cluster | 2 services |
| ACID | Full | Partial | Không | Không |
| Team familiar | Cao | Trung bình | Thấp | Thấp |

## Hệ quả

- Cần cài TimescaleDB extension: `CREATE EXTENSION IF NOT EXISTS timescaledb;`
- JSONB queries chậm hơn relational nếu không có GIN index — cần thiết lập index đúng.
- Payload lớn (>1 MB) nên cân nhắc TOAST compression (PostgreSQL tự xử lý).
- Nếu scale > 50.000 endpoints/ngày, cân nhắc partition thêm hoặc archiving.
