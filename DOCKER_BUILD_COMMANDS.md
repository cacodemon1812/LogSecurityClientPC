# Docker Build Commands - Hướng dẫn Chi tiết

Tài liệu này ghi chép lại các lệnh Docker dùng để build ứng dụng PolicyCollector và ý nghĩa của từng lệnh.

## 1. Cấu trúc Docker Compose

Dự án sử dụng **Docker Compose Overlay Pattern** - gộp nhiều file compose lại với nhau:

| File                | Mục đích                                                           |
| ------------------- | ------------------------------------------------------------------ |
| `compose.yml`       | Base compose - định nghĩa tất cả services với image sẵn có         |
| `compose.build.yml` | Build overlay - thêm build context cho backend, workers, dashboard |
| `compose.dev.yml`   | Dev overlay - expose ports, dev environment, restart policy        |
| `compose.prod.yml`  | Production overlay - biến env bắt buộc, restart always, no ports   |

---

## 2. Lệnh Docker Compose Chính

### 2.1 Development - Build từ Source

```bash
docker compose -f docker/compose.yml -f docker/compose.dev.yml -f docker/compose.build.yml up --build
```

**Ý nghĩa:**

- Kết hợp 3 file: base + dev overlay + build overlay
- `--build`: Build image từ source code (Dockerfile)
- Build các service: backend, storage-worker, alert-worker, dashboard
- Expose ports: 5432 (PostgreSQL), 6379 (Redis), 8080 (Backend)
- Restart policy: `unless-stopped` (tự động restart nếu lỗi)

**Khi nào dùng:** Phát triển cục bộ, test tính năng mới

---

### 2.2 Development - Dùng Prebuilt Images

```bash
docker compose -f docker/compose.yml -f docker/compose.dev.yml up
```

**Ý nghĩa:**

- Kết hợp base + dev overlay (không build overlay)
- Dùng image đã build sẵn trong registry hoặc local
- Nhanh hơn vì không cần compile
- Vẫn expose ports và environment dev

**Khi nào dùng:** Test nhanh, không thay đổi source code

---

### 2.3 Production

```bash
docker compose -f docker/compose.yml -f docker/compose.prod.yml up -d
```

**Ý nghĩa:**

- Kết hợp base + production overlay
- `-d`: Chạy ở background (daemon mode)
- Restart policy: `always` (tự động restart khi container crash)
- Không expose ports (dùng proxy/ingress)
- Tất cả biến env bắt buộc phải cung cấp (không có default)
- Tối ưu resource (Redis: `maxmemory 1gb`, `maxmemory-policy allkeys-lru`)

**Khi nào dùng:** Deploy lên production

**Cần thiết lập các biến trước:**

```bash
export POSTGRES_PASSWORD=xxx
export REDIS_PASSWORD=xxx
export BACKEND_API_KEY=xxx
export POSTGRES_CONNECTION_STRING=xxx
export DOCKER_REGISTRY=xxx
export VERSION=xxx
docker compose -f docker/compose.yml -f docker/compose.prod.yml up -d
```

---

## 3. Lệnh Docker Build Cho Từng Component

### 3.1 Build Backend

```bash
docker compose -f docker/compose.yml -f docker/compose.dev.yml -f docker/compose.build.yml build backend
```

**Dockerfile:** `docker/Dockerfile`

**Ý nghĩa:** Build image backend từ source code (.NET/C#)

---

### 3.2 Build Storage Worker

```bash
docker compose -f docker/compose.yml -f docker/compose.dev.yml -f docker/compose.build.yml build storage-worker
```

**Dockerfile:** `docker/Dockerfile.worker`

**Ý nghĩa:** Build image worker để xử lý lưu trữ dữ liệu

---

### 3.3 Build Alert Worker

```bash
docker compose -f docker/compose.yml -f docker/compose.dev.yml -f docker/compose.build.yml build alert-worker
```

**Dockerfile:** `docker/Dockerfile.worker`

**Ý nghĩa:** Build image worker để xử lý cảnh báo

---

### 3.4 Build Dashboard

```bash
docker compose -f docker/compose.yml -f docker/compose.dev.yml -f docker/compose.build.yml build dashboard
```

**Dockerfile:** `docker/Dockerfile.dashboard`

**Ý nghĩa:** Build image Dashboard (Next.js)

---

### 3.5 Build Tất Cả Service

```bash
docker compose -f docker/compose.yml -f docker/compose.dev.yml -f docker/compose.build.yml build
```

**Ý nghĩa:** Build lại tất cả image (backend, workers, dashboard)

**Không build:** redis, postgres - dùng image sẵn từ registry

---

## 4. Lệnh Docker Images - Quản Lý Image

### 4.1 Xem tất cả images

```bash
docker compose -f docker/compose.yml -f docker/compose.dev.yml -f docker/compose.build.yml config --images
```

**Ý nghĩa:** Liệt kê tất cả image được sử dụng trong compose

**Output example:**

```
policycollector-local/policycollector-backend:dev
policycollector-local/policycollector-dashboard:dev
policycollector-local/policycollector-storage-worker:dev
policycollector-local/policycollector-alert-worker:dev
redis:7-alpine
timescale/timescaledb:latest-pg16
```

---

### 4.2 Pull Docker Images (từ Registry)

```bash
docker pull redis:7-alpine
docker pull timescale/timescaledb:latest-pg16
```

**Ý nghĩa:** Tải image infrastructure từ Docker Hub

---

### 4.3 Save Docker Images (Export)

```bash
docker save -o policycollector-all-images-dev.tar <image1> <image2> ...
```

**Ý nghĩa:** Xuất tất cả images thành 1 file tar (để transfer/backup)

---

## 5. PowerShell Build Scripts

Dự án cung cấp các script PowerShell để tự động hóa việc build và export:

### 5.1 Build Tất Cả Images

**File:** `scripts/Build-Export-AllImages.ps1`

```powershell
.\scripts\Build-Export-AllImages.ps1 -Version "1.0" -Registry "policycollector-local"
```

**Tác vụ:**

1. Build app images: backend, storage-worker, alert-worker, dashboard
2. Pull infra images: redis, postgres
3. Resolve danh sách image từ compose
4. Save tất cả image thành 1 tar file
5. Output: `docker/exports/policycollector-all-images-1.0.tar`

**Parameters:**

- `-Version`: Version tag (default: "dev")
- `-Registry`: Docker registry name (default: "policycollector-local")
- `-OutputDir`: Thư mục xuất (default: "docker/exports")
- `-SkipBuild`: Bỏ qua build, chỉ export image sẵn
- `-SkipPull`: Bỏ qua pull infra images

---

### 5.2 Build Backend

**File:** `scripts/Build-Export-Backend.ps1`

```powershell
.\scripts\Build-Export-Backend.ps1 -Version "1.0"
```

**Tác vụ:**

1. Build backend image
2. Export thành tar file
3. Output: `docker/exports/components/policycollector-backend-1.0.tar`

---

### 5.3 Build Dashboard

**File:** `scripts/Build-Export-Dashboard.ps1`

```powershell
.\scripts\Build-Export-Dashboard.ps1 -Version "1.0"
```

---

### 5.4 Build Workers (Storage, Alert)

**Files:**

- `scripts/Build-Export-StorageWorker.ps1`
- `scripts/Build-Export-AlertWorker.ps1`

```powershell
.\scripts\Build-Export-StorageWorker.ps1 -Version "1.0"
.\scripts\Build-Export-AlertWorker.ps1 -Version "1.0"
```

---

## 6. Dev Start/Stop Scripts

### 6.1 Khởi động Dev Environment

**File:** `scripts/dev-start.ps1`

```powershell
# Chế độ 1: Dùng prebuilt images
.\scripts\dev-start.ps1

# Chế độ 2: Build từ source
.\scripts\dev-start.ps1 -Build

# Chế độ 3: Custom credentials
.\scripts\dev-start.ps1 `
  -PostgresPassword "custom_pg_pass" `
  -RedisPassword "custom_redis_pass" `
  -BackendApiKey "custom-api-key-min-32-chars-required-here" `
  -Build
```

**Tác vụ:**

1. Thiết lập biến env (credentials mặc định cho dev)
2. Chạy docker compose up -d (hoặc up -d --build)
3. Chờ services healthy
4. Báo lỗi nếu container exit

**Default Credentials (Dev Only):**

```
POSTGRES_PASSWORD: devpassword
REDIS_PASSWORD: devredis
BACKEND_API_KEY: dev-api-key-minimum-32-chars-here!!
```

---

### 6.2 Dừng Dev Environment

**File:** `scripts/dev-stop.ps1`

```powershell
.\scripts\dev-stop.ps1
```

**Tác vụ:**

1. Stop và remove containers
2. Remove networks
3. Giữ lại volumes (dữ liệu database)

---

## 7. Import Scripts - Tải Image từ File Tar

### 7.1 Import Backend Image

**File:** `scripts/Import-Backend.ps1`

```powershell
.\scripts\Import-Backend.ps1 -ImageFile "docker/exports/components/policycollector-backend-1.0.tar"
```

---

### 7.2 Import All Images

Các script khác:

- `Import-Dashboard.ps1`
- `Import-StorageWorker.ps1`
- `Import-AlertWorker.ps1`
- `Import-Postgres.ps1`
- `Import-Redis.ps1`

---

## 8. Image Naming Convention

```
{DOCKER_REGISTRY}/policycollector-{component}:{VERSION}
```

**Ví dụ:**

- `policycollector-local/policycollector-backend:dev`
- `policycollector-local/policycollector-backend:1.0`
- `myregistry.azurecr.io/policycollector-backend:1.0`

**Environment Variables:**

- `DOCKER_REGISTRY`: Registry name (default: "policycollector-local")
- `VERSION`: Version tag (default: "dev")

---

## 9. Quy Trình Build và Deploy

### Development Workflow

```bash
# 1. Build từ source
.\scripts\dev-start.ps1 -Build

# 2. Thay đổi code

# 3. Rebuild cụ thể component
docker compose -f docker/compose.yml -f docker/compose.dev.yml -f docker/compose.build.yml build backend

# 4. Restart service
docker compose -f docker/compose.yml -f docker/compose.dev.yml restart backend

# 5. Stop
.\scripts\dev-stop.ps1
```

### Production Deployment

```bash
# 1. Build tất cả images với version
.\scripts\Build-Export-AllImages.ps1 -Version "1.0" -Registry "myregistry.azurecr.io"

# 2. Upload images lên registry

# 3. Deploy
export POSTGRES_PASSWORD=xxx
export REDIS_PASSWORD=xxx
export BACKEND_API_KEY=xxx
export POSTGRES_CONNECTION_STRING="..."
export DOCKER_REGISTRY="myregistry.azurecr.io"
export VERSION="1.0"

docker compose -f docker/compose.yml -f docker/compose.prod.yml up -d

# 4. Verify
docker compose -f docker/compose.yml -f docker/compose.prod.yml ps
docker compose -f docker/compose.yml -f docker/compose.prod.yml logs backend
```

---

## 10. Troubleshooting

### Build fails

```bash
# Xóa image cũ và build lại
docker compose -f docker/compose.yml -f docker/compose.dev.yml -f docker/compose.build.yml build --no-cache backend

# Xem build logs chi tiết
docker compose -f docker/compose.yml -f docker/compose.dev.yml -f docker/compose.build.yml build --verbose backend
```

### Services not starting

```bash
# Kiểm tra status
docker compose -f docker/compose.yml -f docker/compose.dev.yml ps

# Xem logs
docker compose -f docker/compose.yml -f docker/compose.dev.yml logs -f backend
docker compose -f docker/compose.yml -f docker/compose.dev.yml logs -f postgres
```

### Remove everything and start fresh

```bash
# Dừng containers
docker compose -f docker/compose.yml -f docker/compose.dev.yml down

# Xóa volumes (cẩn thận - mất dữ liệu!)
docker compose -f docker/compose.yml -f docker/compose.dev.yml down -v

# Xóa images
docker rmi policycollector-local/policycollector-backend:dev
docker rmi policycollector-local/policycollector-dashboard:dev

# Start lại
.\scripts\dev-start.ps1 -Build
```

---

## 11. Quick Reference

| Mục đích             | Lệnh                                                  |
| -------------------- | ----------------------------------------------------- |
| Start dev (prebuilt) | `.\scripts\dev-start.ps1`                             |
| Start dev (build)    | `.\scripts\dev-start.ps1 -Build`                      |
| Stop dev             | `.\scripts\dev-stop.ps1`                              |
| Build backend        | `docker compose ... build backend`                    |
| Build all            | `docker compose ... build`                            |
| Export all images    | `.\scripts\Build-Export-AllImages.ps1 -Version "1.0"` |
| View compose config  | `docker compose ... config --images`                  |
| Stop containers      | `docker compose ... stop`                             |
| View logs            | `docker compose ... logs -f backend`                  |
| Remove everything    | `docker compose ... down -v`                          |
