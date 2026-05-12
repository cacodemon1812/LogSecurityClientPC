# ADR-002 — Transport Protocol: HTTPS JSON với HMAC

**Status:** Accepted  
**Date:** 2026-05-12

## Context

Agent cần gửi dữ liệu về Backend qua network, có thể traverse qua corporate proxy và firewall. Cần đảm bảo bảo mật và tương thích môi trường doanh nghiệp.

## Quyết định

**HTTPS JSON với API Key authentication + optional HMAC payload signing.**  
mTLS là optional add-on cho môi trường yêu cầu cao hơn.

## Lý do

- **HTTPS port 443** — luôn được allow qua corporate firewall, không cần open port mới.
- **JSON** — dễ debug, dễ inspect, readable trong Wireshark khi cần troubleshoot.
- **API Key** — đơn giản nhất, đủ cho threat model hiện tại (transport encrypted).
- **HMAC** — thêm payload integrity mà không cần PKI phức tạp.
- **mTLS optional** — có thể enable sau mà không breaking change, dùng khi compliance yêu cầu.
- **Corporate proxy compat** — HTTPS qua proxy transparent hoạt động tự nhiên, gRPC qua proxy phức tạp hơn.

## Alternatives đã xem xét

| | HTTPS JSON | gRPC | MQTT | Syslog over TLS |
|---|---|---|---|---|
| Firewall compat | Port 443 | Port thường bị block | Port 1883/8883 | Port 6514 |
| Proxy compat | Tốt | Cần HTTP/2 proxy | Không qua HTTP proxy | Không |
| Debug dễ | Cao (curl/Postman) | Thấp (cần grpcurl) | Trung bình | Thấp |
| Schema evolution | Manual version | Protobuf IDL | N/A | N/A |
| Offline buffer | Dễ (JSON file/SQLite) | Phức tạp | Native MQTT QoS | Không |

## Hệ quả

- Payload size lớn hơn so với binary protocol (Protobuf). Với dữ liệu cấu hình (~10-100 KB/lần), không phải vấn đề.
- gRPC có thể xem xét lại khi cần stream realtime hoặc > 10.000 endpoints.
