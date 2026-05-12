# P8 — Backend: Docker Compose + Grafana + Health Checks

**Phase:** 2 | **Phụ thuộc:** P5 | **Output:** Dev environment hoàn chỉnh chạy 1 lệnh

## Mục tiêu

Hoàn thiện Docker Compose dev environment, cấu hình Grafana dashboard, health check endpoints, và production Compose/Kubernetes basics. Sau P8, `docker compose up` cho ra full pipeline.

## Files cần tạo

```
docker/
  compose.dev.yml               (cập nhật từ P5 — thêm workers)
  compose.prod.yml
  Dockerfile
  Dockerfile.worker
  grafana/
    provisioning/
      datasources/
        postgres.yml
      dashboards/
        dashboard.yml
    dashboards/
      hosts-overview.json
      violations.json
      app-inventory.json
k8s/
  namespace.yaml
  backend-deployment.yaml
  storage-worker-deployment.yaml
  alert-worker-deployment.yaml
  backend-service.yaml
  backend-ingress.yaml
  secrets.yaml.example
  configmap.yaml
scripts/
  dev-start.ps1
  dev-stop.ps1
  seed-api-key.sh
```

---

## Chi tiết từng file

### [FILE] `docker/compose.dev.yml` (đầy đủ)

```yaml
version: "3.9"

services:
  # ──────────────────────────────────────
  # Infrastructure
  # ──────────────────────────────────────
  postgres:
    image: timescale/timescaledb:latest-pg16
    restart: unless-stopped
    environment:
      POSTGRES_DB: policycollector
      POSTGRES_USER: pcollector
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-devpassword}
    ports: ["5432:5432"]
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U pcollector -d policycollector"]
      interval: 5s
      timeout: 3s
      retries: 10
      start_period: 10s

  redis:
    image: redis:7-alpine
    restart: unless-stopped
    command: >
      redis-server
      --requirepass ${REDIS_PASSWORD:-devredis}
      --appendonly yes
    ports: ["6379:6379"]
    volumes:
      - redisdata:/data
    healthcheck:
      test: ["CMD", "redis-cli", "-a", "${REDIS_PASSWORD:-devredis}", "ping"]
      interval: 5s
      timeout: 3s
      retries: 10

  # ──────────────────────────────────────
  # Application
  # ──────────────────────────────────────
  backend:
    build:
      context: ../
      dockerfile: docker/Dockerfile
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ASPNETCORE_HTTP_PORTS: "8080"
      ConnectionStrings__Postgres: >
        Host=postgres;Port=5432;Database=policycollector;
        Username=pcollector;Password=${POSTGRES_PASSWORD:-devpassword}
      ConnectionStrings__Redis: >
        redis:6379,password=${REDIS_PASSWORD:-devredis},abortConnect=false
      Backend__ApiKey: ${BACKEND_API_KEY:-dev-api-key-minimum-32-chars}
      Backend__HmacSecret: ${HMAC_SECRET:-}
      Backend__HmacRequired: "false"
      Backend__SupportedSchemaVersion: "1"
      Backend__AlertWebhookUrl: ${ALERT_WEBHOOK_URL:-}
    ports: ["8080:8080"]
    depends_on:
      postgres: { condition: service_healthy }
      redis: { condition: service_healthy }
    healthcheck:
      test: ["CMD-SHELL", "curl -sf http://localhost:8080/health/ready || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 15s

  storage-worker:
    build:
      context: ../
      dockerfile: docker/Dockerfile.worker
    restart: unless-stopped
    environment:
      WORKER_TYPE: storage
      ConnectionStrings__Postgres: >
        Host=postgres;Port=5432;Database=policycollector;
        Username=pcollector;Password=${POSTGRES_PASSWORD:-devpassword}
      ConnectionStrings__Redis: >
        redis:6379,password=${REDIS_PASSWORD:-devredis},abortConnect=false
    depends_on:
      backend: { condition: service_healthy }

  alert-worker:
    build:
      context: ../
      dockerfile: docker/Dockerfile.worker
    restart: unless-stopped
    environment:
      WORKER_TYPE: alert
      ConnectionStrings__Postgres: >
        Host=postgres;Port=5432;Database=policycollector;
        Username=pcollector;Password=${POSTGRES_PASSWORD:-devpassword}
      ConnectionStrings__Redis: >
        redis:6379,password=${REDIS_PASSWORD:-devredis},abortConnect=false
      Backend__AlertWebhookUrl: ${ALERT_WEBHOOK_URL:-}
    depends_on:
      backend: { condition: service_healthy }

  # ──────────────────────────────────────
  # Observability
  # ──────────────────────────────────────
  grafana:
    image: grafana/grafana:10-ubuntu
    restart: unless-stopped
    environment:
      GF_SECURITY_ADMIN_USER: admin
      GF_SECURITY_ADMIN_PASSWORD: ${GRAFANA_PASSWORD:-devgrafana}
      GF_INSTALL_PLUGINS: ""
    ports: ["3000:3000"]
    volumes:
      - grafana_data:/var/lib/grafana
      - ./grafana/provisioning:/etc/grafana/provisioning:ro
      - ./grafana/dashboards:/var/lib/grafana/dashboards:ro
    depends_on:
      postgres: { condition: service_healthy }

volumes:
  pgdata:
  redisdata:
  grafana_data:
```

---

### [FILE] `docker/grafana/provisioning/datasources/postgres.yml`

```yaml
apiVersion: 1
datasources:
  - name: PolicyCollector-PG
    type: postgres
    url: postgres:5432
    database: policycollector
    user: pcollector
    secureJsonData:
      password: ${POSTGRES_PASSWORD}
    jsonData:
      sslmode: disable
      postgresVersion: 1600
      timescaledb: true
    isDefault: true
    editable: true
```

---

### [FILE] `docker/grafana/provisioning/dashboards/dashboard.yml`

```yaml
apiVersion: 1
providers:
  - name: PolicyCollector
    orgId: 1
    type: file
    disableDeletion: false
    updateIntervalSeconds: 30
    allowUiUpdates: true
    options:
      path: /var/lib/grafana/dashboards
```

---

### [FILE] `docker/grafana/dashboards/hosts-overview.json` (key panels)

```json
{
  "title": "PolicyCollector — Hosts Overview",
  "uid": "policycollector-hosts",
  "panels": [
    {
      "title": "Total Hosts",
      "type": "stat",
      "targets": [{
        "rawSql": "SELECT COUNT(*) FROM host_latest",
        "format": "table"
      }]
    },
    {
      "title": "Online Hosts (last 2h)",
      "type": "stat",
      "targets": [{
        "rawSql": "SELECT COUNT(*) FROM host_latest WHERE last_seen > NOW() - INTERVAL '2 hours'",
        "format": "table"
      }]
    },
    {
      "title": "Offline Hosts (> 24h)",
      "type": "stat",
      "fieldConfig": { "defaults": { "color": { "mode": "thresholds" },
        "thresholds": { "steps": [{"color": "green", "value": 0}, {"color": "red", "value": 1}] } } },
      "targets": [{
        "rawSql": "SELECT COUNT(*) FROM host_latest WHERE last_seen < NOW() - INTERVAL '24 hours'",
        "format": "table"
      }]
    },
    {
      "title": "Host List",
      "type": "table",
      "targets": [{
        "rawSql": "SELECT hostname, domain, os_version, agent_version, last_seen, status FROM host_latest ORDER BY last_seen DESC LIMIT 100",
        "format": "table"
      }]
    },
    {
      "title": "Agents Check-in Over Time",
      "type": "timeseries",
      "targets": [{
        "rawSql": "SELECT time_bucket('1 hour', collected_at) AS time, COUNT(*) AS check_ins FROM collection_snapshots WHERE $__timeFilter(collected_at) GROUP BY 1 ORDER BY 1",
        "format": "time_series"
      }]
    }
  ]
}
```

---

### [FILE] `docker/grafana/dashboards/violations.json` (key panels)

```json
{
  "title": "PolicyCollector — Policy Violations",
  "uid": "policycollector-violations",
  "panels": [
    {
      "title": "Open Critical Violations",
      "type": "stat",
      "fieldConfig": { "defaults": { "thresholds": {
        "steps": [{"color": "green", "value": 0}, {"color": "red", "value": 1}]
      }}},
      "targets": [{
        "rawSql": "SELECT COUNT(*) FROM policy_violations WHERE resolved = false AND severity = 'critical'",
        "format": "table"
      }]
    },
    {
      "title": "Violations by Severity",
      "type": "piechart",
      "targets": [{
        "rawSql": "SELECT severity, COUNT(*) AS count FROM policy_violations WHERE resolved = false GROUP BY severity",
        "format": "table"
      }]
    },
    {
      "title": "Top Violated Rules",
      "type": "barchart",
      "targets": [{
        "rawSql": "SELECT rule_id, COUNT(*) AS count FROM policy_violations WHERE resolved = false GROUP BY rule_id ORDER BY count DESC LIMIT 10",
        "format": "table"
      }]
    },
    {
      "title": "Violations List",
      "type": "table",
      "targets": [{
        "rawSql": "SELECT hostname, rule_id, severity, message, detected_at FROM policy_violations WHERE resolved = false ORDER BY detected_at DESC LIMIT 200",
        "format": "table"
      }]
    },
    {
      "title": "New Violations Over Time",
      "type": "timeseries",
      "targets": [{
        "rawSql": "SELECT time_bucket('1 hour', detected_at) AS time, COUNT(*) AS violations FROM policy_violations WHERE $__timeFilter(detected_at) GROUP BY 1 ORDER BY 1",
        "format": "time_series"
      }]
    }
  ]
}
```

---

### [FILE] `docker/grafana/dashboards/app-inventory.json` (key panels)

```json
{
  "title": "PolicyCollector — Application Inventory",
  "uid": "policycollector-apps",
  "panels": [
    {
      "title": "Top Installed Applications",
      "type": "table",
      "targets": [{
        "rawSql": "WITH latest AS (SELECT DISTINCT ON (hostname) hostname, payload FROM collection_snapshots ORDER BY hostname, collected_at DESC), apps AS (SELECT hostname, app->>'display_name' AS name, app->>'display_version' AS version, app->>'publisher' AS publisher FROM latest, jsonb_array_elements(payload->'applications') AS app WHERE payload->'applications' IS NOT NULL) SELECT name, MAX(version) AS latest_version, publisher, COUNT(DISTINCT hostname) AS machines FROM apps GROUP BY name, publisher ORDER BY machines DESC LIMIT 50",
        "format": "table"
      }]
    }
  ]
}
```

---

### [FILE] `k8s/backend-deployment.yaml`

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: policycollector-backend
  namespace: policycollector
spec:
  replicas: 2
  selector:
    matchLabels: { app: backend }
  template:
    metadata:
      labels: { app: backend }
    spec:
      containers:
        - name: backend
          image: your-registry/policycollector-backend:1.0.0
          ports: [{ containerPort: 8080 }]
          env:
            - name: ASPNETCORE_HTTP_PORTS
              value: "8080"
            - name: ConnectionStrings__Postgres
              valueFrom:
                secretKeyRef: { name: pc-secrets, key: postgres-connection-string }
            - name: ConnectionStrings__Redis
              valueFrom:
                secretKeyRef: { name: pc-secrets, key: redis-connection-string }
            - name: Backend__ApiKey
              valueFrom:
                secretKeyRef: { name: pc-secrets, key: api-key }
          readinessProbe:
            httpGet: { path: /health/ready, port: 8080 }
            initialDelaySeconds: 10
            periodSeconds: 5
          livenessProbe:
            httpGet: { path: /health/live, port: 8080 }
            initialDelaySeconds: 15
            periodSeconds: 10
          resources:
            requests: { cpu: 100m, memory: 128Mi }
            limits: { cpu: 500m, memory: 512Mi }
```

---

### [FILE] `scripts/dev-start.ps1`

```powershell
# Khởi động toàn bộ dev environment
$ErrorActionPreference = "Stop"

$env:POSTGRES_PASSWORD = "devpassword"
$env:REDIS_PASSWORD = "devredis"
$env:BACKEND_API_KEY = "dev-api-key-minimum-32-chars-here!!"
$env:GRAFANA_PASSWORD = "devgrafana"

Write-Host "Starting PolicyCollector dev environment..."
docker compose -f docker/compose.dev.yml up -d

Write-Host "Waiting for services to be healthy..."
$timeout = 60
$elapsed = 0
do {
    Start-Sleep -Seconds 3
    $elapsed += 3
    $health = docker compose -f docker/compose.dev.yml ps --format json |
        ConvertFrom-Json | Where-Object { $_.Health -ne "healthy" -and $_.Health -ne "" }
} while ($health.Count -gt 0 -and $elapsed -lt $timeout)

Write-Host ""
Write-Host "Services ready:"
Write-Host "  Backend API:  http://localhost:8080"
Write-Host "  Grafana:      http://localhost:3000  (admin/devgrafana)"
Write-Host "  PostgreSQL:   localhost:5432"
Write-Host "  Redis:        localhost:6379"
Write-Host ""
Write-Host "Test ingest:"
Write-Host "  curl -X POST http://localhost:8080/api/v1/ingest -H 'X-Api-Key: dev-api-key-minimum-32-chars-here!!' -H 'Content-Type: application/json' -d @test/sample-payload.json"
```

---

### [FILE] `src/PolicyCollector.Backend/Infrastructure/DatabaseHealthCheck.cs`

```csharp
namespace PolicyCollector.Backend.Infrastructure;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDbConnectionFactory _db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteScalarAsync<int>("SELECT 1");
            return HealthCheckResult.Healthy("PostgreSQL connection OK");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection failed", ex);
        }
    }
}

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.PingAsync();
            return HealthCheckResult.Healthy("Redis connection OK");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis connection failed", ex);
        }
    }
}
```

---

## Acceptance Criteria

- [ ] `powershell scripts/dev-start.ps1` → tất cả services healthy trong 60s
- [ ] `curl http://localhost:8080/health` → `{"status":"Healthy","entries":{...}}`
- [ ] `curl http://localhost:8080/health/ready` → 503 khi Postgres down, 200 khi up
- [ ] Grafana `http://localhost:3000` → đăng nhập được với admin/devgrafana
- [ ] Dashboard "Hosts Overview" load không lỗi (0 data = OK, query không lỗi)
- [ ] Dashboard "Violations" load không lỗi
- [ ] `docker compose down && docker compose up` → data persist (volumes không mất)
- [ ] Kubernetes YAML valid: `kubectl apply --dry-run=client -f k8s/` không lỗi
- [ ] `docker compose logs backend` không có ERROR sau khởi động
