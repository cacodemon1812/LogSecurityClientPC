# 03 — Tech Stack

## Agent (Endpoint Service)

| Thành phần | Lựa chọn | Lý do |
|---|---|---|
| **Ngôn ngữ** | C# 12 / .NET 8 | Native Windows API, COM, WMI, PowerShell Runspace. Self-contained publish = không cần runtime cài sẵn. |
| **Service host** | `IHostedService` + `WindowsServiceLifetime` | Tích hợp SCM (Start/Stop/Pause), logging structured, DI tích hợp sẵn. |
| **Scheduler** | `System.Threading.PeriodicTimer` | Zero-dependency, accurate, cancellable. Không dùng Quartz.NET (quá nặng cho usecase này). |
| **HTTP client** | `HttpClient` + `IHttpClientFactory` + Polly | Retry policy, circuit breaker, timeout — tất cả trong 1 stack. |
| **Local queue** | SQLite via `Microsoft.Data.Sqlite` | Nhỏ gọn, không cần service riêng, đủ cho offline buffer. |
| **Serialization** | `System.Text.Json` (source generation) | Native, nhanh, AOT compatible. Không dùng Newtonsoft.Json. |
| **Config** | `Microsoft.Extensions.Configuration` + `appsettings.json` | Standard .NET, hỗ trợ override qua env var và registry. |
| **Logging** | `Microsoft.Extensions.Logging` + `Serilog` (file sink) | Structured log, rolling file, không phụ thuộc external log server. |
| **Secrets** | Windows DPAPI / Credential Manager | Không lưu API key plain text trên disk. |
| **Installer** | WiX Toolset v4 (MSI) | GPO Software Deployment yêu cầu MSI. Inno Setup là fallback. |
| **PowerShell** | `System.Management.Automation` NuGet | Chạy PS cmdlet trong-process, không fork process. |
| **WMI** | `System.Management` (CIM/WMI) | Thu thập RSOP, Win32_Service, Win32_Product. |

### Dependencies Agent (chỉ thêm với ADR)

```xml
<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.Http.Polly" Version="8.*" />
<PackageReference Include="Microsoft.Data.Sqlite" Version="8.*" />
<PackageReference Include="Serilog.Extensions.Hosting" Version="8.*" />
<PackageReference Include="Serilog.Sinks.RollingFile" Version="3.*" />
<PackageReference Include="System.Management.Automation" Version="7.*" />
<PackageReference Include="System.Management" Version="8.*" />
```

---

## Backend

| Thành phần | Lựa chọn | Lý do |
|---|---|---|
| **Ngôn ngữ** | C# 12 / .NET 8 | Đồng nhất với Agent, tái sử dụng data models. |
| **Web framework** | ASP.NET Core 8 (Minimal API) | Nhẹ hơn MVC cho usecase ingest API. |
| **Database** | PostgreSQL 16 + TimescaleDB | JSONB cho flexible payload, TimescaleDB cho timeseries query hiệu quả. |
| **ORM / DB access** | Dapper (raw SQL) | Control query, performance predictable. Không dùng EF Core (payload JSONB phức tạp). |
| **Message Queue** | Redis Streams (default) / RabbitMQ (optional) | Redis Streams đủ dùng cho scale vừa, ít operational overhead. |
| **Caching** | Redis | Dùng chung instance với queue. |
| **Alerting** | Grafana Alerting | Tích hợp sẵn với dashboard, hỗ trợ email/Slack/PagerDuty. |
| **Dashboard** | Grafana OSS | PostgreSQL datasource, policy violation panels. |
| **Container** | Docker + Docker Compose (dev) / Kubernetes (prod) | Dev simple, prod scalable. |
| **Auth** | API Key (header) + optional mTLS | Đơn giản, không cần IdP riêng. |

### Dependencies Backend

```xml
<PackageReference Include="Dapper" Version="2.*" />
<PackageReference Include="Npgsql" Version="8.*" />
<PackageReference Include="StackExchange.Redis" Version="2.*" />
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />
```

---

## Alternatives đã xem xét và loại bỏ

| Phương án | Lý do loại bỏ |
|---|---|
| **Go agent** | WMI/COM integration phức tạp hơn nhiều; PowerShell runspace không native. |
| **PowerShell script + Task Scheduler** | Khó quản lý lifecycle, không có offline queue, dễ bị disable bởi user. |
| **WMI subscription (push)** | Latency không đều, khó debug, không collect toàn bộ modules. |
| **EF Core cho Backend** | JSONB query với EF Core có workaround phức tạp; Dapper rõ ràng hơn. |
| **InfluxDB / Prometheus** | Schema cứng không phù hợp với payload linh hoạt; JSONB PostgreSQL đủ. |
| **Elasticsearch** | Operational overhead quá lớn cho team nhỏ. |
| **gRPC transport** | Thêm phức tạp không cần thiết; HTTPS JSON đủ cho throughput hiện tại. |
| **Quartz.NET** | Quá nặng cho 1 scheduled job đơn giản. |

---

## Yêu cầu môi trường phát triển

```
Windows 10/11 hoặc Windows Server 2019+
.NET SDK 8.x
Visual Studio 2022 17.8+ hoặc VS Code + C# Dev Kit
Docker Desktop (cho Backend local)
PostgreSQL 16 (hoặc chạy qua Docker)
WiX Toolset v4 (cho build MSI)
```

## Build targets

```
Agent:   dotnet publish -c Release -r win-x64 --self-contained true
Backend: dotnet publish -c Release -r linux-x64 --self-contained true
MSI:     wix build installer/setup.wxs -o dist/PolicyCollector-x64.msi
```
