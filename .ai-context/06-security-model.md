# 06 — Security Model

## Threat Model

### Tài sản cần bảo vệ

| Tài sản | Mức độ nhạy cảm | Lý do |
|---|---|---|
| Payload cấu hình | Cao | Tiết lộ attack surface: firewall holes, weak policy, installed apps |
| API Key | Cao | Cho phép inject dữ liệu giả vào hệ thống |
| Client Certificate (mTLS) | Cao | Private key cho phép impersonate endpoint |
| Log agent | Trung bình | Có thể chứa hostname, error message |
| Config appsettings.json | Trung bình | Chứa Backend URL, có thể chứa API key nếu cấu hình sai |

### Threat Actors

| Actor | Khả năng | Mối đe dọa |
|---|---|---|
| Người dùng cục bộ (non-admin) | Thấp | Đọc log nếu ACL sai |
| Admin cục bộ (không phải Domain Admin) | Trung bình | Đọc config, chặn service, gửi payload giả nếu biết API key |
| Attacker có Local SYSTEM | Cao | Đọc DPAPI secrets, modify agent binary |
| Man-in-the-middle (network) | Trung bình | Intercept payload, replay attack |
| Insider (IT Admin) | Trung bình | Abuse Backend API, query dữ liệu nhạy cảm |

---

## Lớp bảo mật Transport

### Tầng 1 — Mã hoá kênh (TLS)

```
- TLS 1.2 tối thiểu (TLS 1.3 preferred)
- Certificate chain phải valid (không self-signed trên production)
- Agent check certificate revocation (OCSP)
- Không cho phép TLS downgrade
- HttpClient config:
    handler.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    handler.CheckCertificateRevocationList = true;
```

### Tầng 2 — Xác thực Agent (API Key)

```
Header: X-Api-Key: <key>

Yêu cầu:
- Độ dài tối thiểu 32 bytes entropy (256-bit)
- Lưu trên Backend: bcrypt hash, không plain text
- Lưu trên Agent: Windows Credential Manager (DPAPI-encrypted)
- Rotation: mỗi 90 ngày hoặc khi suspected compromise
- Một API key per environment (dev/staging/prod) — không share

Backend validate:
  1. Trim whitespace
  2. Length check (≥ 32 chars)
  3. Timing-safe compare với stored hash
  4. Rate limit: 60 requests/minute/key
```

### Tầng 3 — Toàn vẹn Payload (HMAC)

```
Header: X-Hmac-SHA256: <base64(HMAC-SHA256(body_bytes, hmac_secret))>

Agent:
  hmac_secret lưu trong Windows Credential Manager (khác với API key)
  HMAC tính trên raw body bytes trước khi compress/encode

Backend:
  Tính HMAC độc lập
  Dùng CryptographicOperations.FixedTimeEquals() để compare
  Reject nếu không khớp → log warning, return 401

Khi bật (HMAC_REQUIRED=true):
  Backend reject payload không có header này
```

### Tầng 4 — mTLS (optional, prod recommended)

```
Agent mang client certificate để Backend xác minh
Client cert cấp bởi Internal CA (Microsoft CA / HashiCorp Vault PKI)
Backend check:
  - Certificate trusted by configured CA
  - CN hoặc SAN matches hostname pattern
  - Certificate không expired
  - Certificate không revoked (CRL check)

Certificate lifecycle:
  - Validity: 1 năm
  - Renewal: 30 ngày trước hết hạn (auto qua GPO autoenrollment)
  - Revocation: qua CRL/OCSP khi máy decommission
```

---

## Lưu trữ Secrets trên Agent

### Windows Credential Manager

```csharp
// Lưu API key lúc cài đặt (Installer hoặc first-run setup)
var credential = new Credential {
    Target = "PolicyCollector/ApiKey",
    UserName = "agent",
    Password = apiKey,
    Type = CredentialType.Generic,
    PersistanceType = PersistanceType.LocalMachine
};
credential.Save();

// Đọc tại runtime
var cred = Credential.Load("PolicyCollector/ApiKey");
```

### Không được làm

- Không lưu API key trong appsettings.json (bị committed vào git, dễ đọc).
- Không log giá trị API key dù một phần (first N chars).
- Không để API key trong environment variable (dễ bị leak qua process listing).
- Không pass qua command line arguments (visible trong Task Manager).

---

## Bảo mật Backend

### API Key Management

```
- Lưu trong DB: bcrypt(key, cost=12) hoặc Argon2id
- Key prefix dùng cho lookup nhanh: "pc_" + first 8 chars (plain) → index
- Không log full key, chỉ log prefix khi auth fail
- Admin API để rotate key mà không downtime:
    POST /api/v1/admin/keys/rotate → tạo key mới, key cũ vẫn valid 24h
```

### Input Validation

```
- Validate Content-Type: application/json
- Reject payload > 10 MB
- Sanitize hostname field (regex whitelist)
- Validate collected_at không quá 24h tương lai / 7 ngày quá khứ
- Không thực thi query từ payload (dùng parameterized query)
- JSONB lưu as-is, không parse lại khi query
```

### Rate Limiting

```
Nginx/Caddy config:
  /api/v1/ingest: 60 req/min/IP, 120 req/min/key
  /api/v1/hosts:  30 req/min/IP (dashboard queries)
  /api/v1/admin:  10 req/min/IP
```

### Audit Log

```
Mọi ingest request log:
  - Timestamp, hostname, collection_id, IP, API key prefix, status
  - Không log payload body

Mọi admin action log:
  - Timestamp, admin user, action, target, result
```

---

## ACL trên Agent

```
Thư mục cài đặt: C:\Program Files\PolicyCollector\
  - Administrators: Full Control
  - SYSTEM: Full Control
  - Users: Read & Execute (chỉ chạy binary, không đọc config)

Config directory: C:\ProgramData\PolicyCollector\
  - Administrators: Full Control
  - SYSTEM: Full Control
  - Users: No Access

Log directory: C:\ProgramData\PolicyCollector\logs\
  - Administrators: Full Control
  - SYSTEM: Full Control
  - Users: No Access
```

---

## Checklist bảo mật trước khi deploy

- [ ] TLS certificate valid và từ trusted CA
- [ ] API key ≥ 32 bytes entropy, không phải default value
- [ ] API key lưu trong Credential Manager, không trong config file
- [ ] HMAC secret khác với API key
- [ ] Backend URL là HTTPS, không HTTP
- [ ] Log directory không accessible bởi regular users
- [ ] Backend behind reverse proxy (Nginx/Caddy), không expose trực tiếp
- [ ] Database không accessible từ ngoài (chỉ localhost/VPC)
- [ ] Redis không có AUTH disabled (set password)
- [ ] Grafana admin password thay đổi khỏi default
