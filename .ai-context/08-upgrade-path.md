# 08 — Upgrade Path

## Versioning

### Agent

- **SemVer:** `MAJOR.MINOR.PATCH`
- MAJOR: Breaking change trong behavior hoặc config schema
- MINOR: Thêm collector mới hoặc field mới (backward compat)
- PATCH: Bug fix, performance
- Version ghi trong assembly và MSI `ProductVersion`
- Version gửi kèm mỗi payload (`agent_version` field)

### Payload Schema

- `schema_version`: `MAJOR.MINOR` (không có PATCH)
- **MINOR bump:** Thêm field mới → Backend bỏ qua unknown fields, Agent cũ vẫn hoạt động
- **MAJOR bump:** Breaking change → Backend từ chối payload cũ, trả `HTTP 422`
- Quy tắc: **không xóa field** trong cùng MAJOR version (chỉ deprecate + null)

### Backend API

- URL versioning: `/api/v1/...`, `/api/v2/...`
- Duy trì ít nhất 1 major version cũ trong 6 tháng sau khi release major mới
- Header `Sunset: <date>` trên endpoint sắp bị xóa

---

## Ma trận tương thích

| Agent Version | Schema | Backend Hỗ trợ |
|---|---|---|
| 0.x (dev) | 0.x | Dev backend only |
| 1.0.x | 1.0 | Backend ≥ 1.0 |
| 1.1.x | 1.1 | Backend ≥ 1.0 (MINOR compat) |
| 2.0.x | 2.0 | Backend ≥ 2.0 (MAJOR break) |

**Nguyên tắc:** Backend luôn deploy trước Agent khi có MINOR update. Với MAJOR update, cần deploy Backend mới song song và migration window.

---

## Quy trình Release

### Release thông thường (patch/minor)

```
1. Merge vào main
2. Tag: git tag v1.1.0
3. CI build: MSI + Backend container
4. Publish release notes (changelog)
5. Deploy Backend (rolling update, không downtime)
6. Staged rollout Agent (Pilot → Toàn bộ)
7. Monitor 48h (error rate, last_seen stats)
```

### MAJOR release (breaking)

```
1. Deploy Backend v2 (endpoint /api/v2/)
2. Backend v1 vẫn chạy (dual version, 6 tháng)
3. Rollout Agent 2.0 dần (Pilot → Toàn bộ, 4-8 tuần)
4. Theo dõi % agent đang dùng v1 vs v2
5. Khi v1 agent < 5% → schedule sunset
6. Thông báo trước 30 ngày → tắt v1 endpoint
```

---

## Migration Database

### Thêm field mới (non-breaking)

```sql
-- 005_add_tls_config.sql
ALTER TABLE collection_snapshots
  ADD COLUMN IF NOT EXISTS tls_config JSONB;

-- JSONB payload tự động chứa field mới từ agent v1.1+
-- Máy chạy agent cũ sẽ có tls_config = null trong jsonb
```

### Rename / restructure field (breaking → MAJOR)

```sql
-- 010_v2_schema.sql
-- 1. Thêm column mới
ALTER TABLE collection_snapshots ADD COLUMN security_v2 JSONB;

-- 2. Backfill từ payload cũ (background job)
UPDATE collection_snapshots
  SET security_v2 = payload->'security_policy'
  WHERE schema_version LIKE '1.%';

-- 3. Sau khi tất cả agent up v2, drop column cũ (separate migration)
```

---

## Upgrade Agent trên Endpoint

### Tự động (recommended)

```powershell
# Backend trả header trong response của /ingest:
X-Latest-Agent-Version: 1.2.0

# Agent so sánh với current version
# Nếu có bản mới → log "Update available: 1.2.0"
# Không tự update — chỉ notify (update vẫn qua GPO/Intune)
```

### Upgrade qua GPO

```
WiX MajorUpgrade element xử lý tự động:
- Detect version cũ qua UpgradeCode (GUID cố định)
- Uninstall silently
- Install phiên bản mới
- Giữ nguyên data trong ProgramData
```

### Kiểm tra tiến độ upgrade

```sql
-- Đếm agent theo version
SELECT agent_version, COUNT(*) as count
FROM host_latest
WHERE last_seen > NOW() - INTERVAL '7 days'
GROUP BY agent_version
ORDER BY count DESC;
```

---

## Deprecation Policy

| Thành phần | Thời gian hỗ trợ sau deprecation |
|---|---|
| Agent MAJOR version | 6 tháng |
| Backend API major version | 6 tháng |
| Payload schema MAJOR | 6 tháng |
| DB column deprecated | 3 tháng (null-fill) → drop |

---

## Changelog Template

```markdown
## [1.1.0] - 2026-MM-DD

### Added
- BitLockerCollector: thu thập trạng thái mã hoá từng volume
- Payload field: `bitlocker[]` array

### Changed
- SecurityPolicyCollector: thêm TLS protocol detection
- IntervalMinutes mặc định từ 30 → 60

### Fixed
- GpoCollector: xử lý đúng khi gpresult trả về exit code 1 trên workgroup

### Schema
- schema_version: 1.0 → 1.1 (MINOR, backward compat)

### Deployment
- Backend ≥ 1.0 tương thích, không cần update Backend
- Staged rollout qua GPO
```
