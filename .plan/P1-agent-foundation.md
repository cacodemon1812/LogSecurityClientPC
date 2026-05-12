# P1 — Agent: Project Setup & Core Infrastructure

**Phase:** 1 | **Phụ thuộc:** — | **Output:** Solution scaffold + core abstractions hoạt động

## Mục tiêu

Tạo solution .NET 8, project Agent với DI, config, logging, Windows Service host, interface `ICollector`, và toàn bộ model classes. Sau P1, mọi collector có thể được viết độc lập.

## Cấu trúc file cần tạo

```
PolicyCollector.sln
src/
  PolicyCollector.Agent/
    PolicyCollector.Agent.csproj
    Program.cs
    appsettings.json
    appsettings.Development.json
    Config/
      AgentOptions.cs
      ModuleOptions.cs
      TransportOptions.cs
      LocalQueueOptions.cs
      SecretsProvider.cs
    Models/
      CollectionPayload.cs
      HostInfo.cs
      GpoResult.cs
      GpoEntry.cs
      CseResult.cs
      SecPolicyResult.cs
      PasswordPolicy.cs
      AuditPolicy.cs
      UacConfig.cs
      TlsConfig.cs
      RdpConfig.cs
      FirewallResult.cs
      FirewallProfile.cs
      FirewallRule.cs
      DefenderResult.cs
      BitLockerVolume.cs
      AppEntry.cs
      AppxEntry.cs
      ServiceEntry.cs
      TaskEntry.cs
      StartupEntry.cs
    Collectors/
      ICollector.cs
      CollectorResult.cs
    Infrastructure/
      RegistryReader.cs
      WmiQuery.cs
      PowerShellRunner.cs
      ProcessRunner.cs
tests/
  PolicyCollector.Agent.Tests/
    PolicyCollector.Agent.Tests.csproj
    Infrastructure/
      RegistryReaderTests.cs
```

---

## Chi tiết từng file

### [FILE] `PolicyCollector.sln`

```xml
<!-- dotnet new sln -n PolicyCollector -->
<!-- dotnet sln add src/PolicyCollector.Agent/PolicyCollector.Agent.csproj -->
<!-- dotnet sln add tests/PolicyCollector.Agent.Tests/PolicyCollector.Agent.Tests.csproj -->
```

---

### [FILE] `src/PolicyCollector.Agent/PolicyCollector.Agent.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
    <AssemblyName>PolicyCollector.Agent</AssemblyName>
    <RootNamespace>PolicyCollector.Agent</RootNamespace>
    <Version>0.1.0</Version>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <UseWindowsForms>false</UseWindowsForms>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.*" />
    <PackageReference Include="Microsoft.Extensions.Http.Polly" Version="8.*" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.*" />
    <PackageReference Include="Serilog.Extensions.Hosting" Version="8.*" />
    <PackageReference Include="Serilog.Sinks.File" Version="5.*" />
    <PackageReference Include="System.Management.Automation" Version="7.*" />
    <PackageReference Include="System.Management" Version="8.*" />
  </ItemGroup>
</Project>
```

---

### [FILE] `src/PolicyCollector.Agent/Program.cs`

```csharp
using PolicyCollector.Agent.Config;
using PolicyCollector.Agent.Collectors;
using PolicyCollector.Agent.Jobs;
using PolicyCollector.Agent.Scheduler;
using PolicyCollector.Agent.Transport;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
    options.ServiceName = "PolicyCollectorSvc");

// Config
builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection("Agent"));
builder.Services.Configure<TransportOptions>(
    builder.Configuration.GetSection("Transport"));
builder.Services.Configure<LocalQueueOptions>(
    builder.Configuration.GetSection("LocalQueue"));

// Serilog
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration));

// Infrastructure
builder.Services.AddSingleton<PowerShellRunner>();
builder.Services.AddSingleton<WmiQuery>();
builder.Services.AddSingleton<RegistryReader>();
builder.Services.AddSingleton<ProcessRunner>();

// Secrets
builder.Services.AddSingleton<SecretsProvider>();

// Transport
builder.Services.AddSingleton<LocalQueue>();
builder.Services.AddHttpClient<ITransport, HttpTransport>()
    .AddPolicyHandler(/* Polly retry policy — xem P3 */);

// Collectors — đăng ký trong P2
// builder.Services.AddSingleton<ICollector<GpoResult>, GpoCollector>();
// ...

// Jobs
builder.Services.AddSingleton<CollectionJob>();
builder.Services.AddHostedService<CollectionScheduler>();
builder.Services.AddHostedService<RetryJob>();

var host = builder.Build();
host.Run();
```

---

### [FILE] `src/PolicyCollector.Agent/Config/AgentOptions.cs`

```csharp
namespace PolicyCollector.Agent.Config;

public sealed class AgentOptions
{
    public int IntervalMinutes { get; init; } = 60;
    public bool CollectOnStartup { get; init; } = true;
    public int CollectorTimeoutSeconds { get; init; } = 30;
    public ModuleOptions Modules { get; init; } = new();
}

public sealed class ModuleOptions
{
    public bool GPO { get; init; } = true;
    public bool SecurityPolicy { get; init; } = true;
    public bool Firewall { get; init; } = true;
    public bool Defender { get; init; } = true;
    public bool BitLocker { get; init; } = true;
    public bool AppInventory { get; init; } = true;
    public bool AppxPackages { get; init; } = false;
    public bool Services { get; init; } = true;
    public bool ScheduledTasks { get; init; } = true;
    public bool StartupEntries { get; init; } = true;
}
```

---

### [FILE] `src/PolicyCollector.Agent/Config/TransportOptions.cs`

```csharp
namespace PolicyCollector.Agent.Config;

public sealed class TransportOptions
{
    public string BackendUrl { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxRetries { get; init; } = 5;
    public int InitialRetryDelaySeconds { get; init; } = 10;
    public bool UseMtls { get; init; } = false;
    public string ClientCertStore { get; init; } = "LocalMachine";
    public string ClientCertThumbprint { get; init; } = string.Empty;
}
```

---

### [FILE] `src/PolicyCollector.Agent/Config/LocalQueueOptions.cs`

```csharp
namespace PolicyCollector.Agent.Config;

public sealed class LocalQueueOptions
{
    public int MaxAgeHours { get; init; } = 168;    // 7 days
    public int MaxEntries { get; init; } = 1000;
    public int RetryIntervalMinutes { get; init; } = 5;
}
```

---

### [FILE] `src/PolicyCollector.Agent/Config/SecretsProvider.cs`

```csharp
namespace PolicyCollector.Agent.Config;

// Đọc secrets từ Windows Credential Manager (DPAPI-encrypted)
// Credential Manager không có official .NET API — dùng P/Invoke AdvApi32
public sealed class SecretsProvider
{
    private const string ApiKeyTarget = "PolicyCollector/ApiKey";
    private const string HmacSecretTarget = "PolicyCollector/HmacSecret";

    // Trả về null nếu chưa được set
    public string? GetApiKey() => ReadCredential(ApiKeyTarget);
    public string? GetHmacSecret() => ReadCredential(HmacSecretTarget);

    public void SaveApiKey(string key) => WriteCredential(ApiKeyTarget, key);
    public void SaveHmacSecret(string secret) => WriteCredential(HmacSecretTarget, secret);

    // P/Invoke Windows Credential Manager API
    private static string? ReadCredential(string target) { ··· }
    private static void WriteCredential(string target, string value) { ··· }
    // Dùng CredRead / CredWrite từ Advapi32.dll
    // CREDENTIAL struct: TargetName, Type=1 (GENERIC), CredentialBlobSize, CredentialBlob
}
```

---

### [FILE] `src/PolicyCollector.Agent/Collectors/ICollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

// Interface bắt buộc cho mọi collector
// Không được throw exception — return CollectorResult với Success=false
public interface ICollector<TResult>
{
    string ModuleName { get; }
    Task<CollectorResult<TResult>> CollectAsync(CancellationToken ct);
}
```

---

### [FILE] `src/PolicyCollector.Agent/Collectors/CollectorResult.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class CollectorResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorDetail { get; init; }   // Exception.ToString() nếu có
    public TimeSpan Duration { get; init; }

    public static CollectorResult<T> Ok(T data, TimeSpan duration) =>
        new() { Success = true, Data = data, Duration = duration };

    public static CollectorResult<T> Fail(string message, string? detail = null) =>
        new() { Success = false, ErrorMessage = message, ErrorDetail = detail };
}
```

---

### [FILE] `src/PolicyCollector.Agent/Models/CollectionPayload.cs`

```csharp
using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class CollectionPayload
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("collection_id")]
    public string CollectionId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("collected_at")]
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("agent_version")]
    public string AgentVersion { get; init; } = GetAgentVersion();

    [JsonPropertyName("host")]
    public HostInfo? Host { get; init; }

    [JsonPropertyName("gpo")]
    public GpoResult? Gpo { get; init; }

    [JsonPropertyName("security_policy")]
    public SecPolicyResult? SecurityPolicy { get; init; }

    [JsonPropertyName("firewall")]
    public FirewallResult? Firewall { get; init; }

    [JsonPropertyName("defender")]
    public DefenderResult? Defender { get; init; }

    [JsonPropertyName("bitlocker")]
    public List<BitLockerVolume>? BitLocker { get; init; }

    [JsonPropertyName("applications")]
    public List<AppEntry>? Applications { get; init; }

    [JsonPropertyName("appx_packages")]
    public List<AppxEntry>? AppxPackages { get; init; }

    [JsonPropertyName("services")]
    public List<ServiceEntry>? Services { get; init; }

    [JsonPropertyName("scheduled_tasks")]
    public List<TaskEntry>? ScheduledTasks { get; init; }

    [JsonPropertyName("startup_entries")]
    public List<StartupEntry>? StartupEntries { get; init; }

    private static string GetAgentVersion() =>
        typeof(CollectionPayload).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
```

---

### [FILE] `src/PolicyCollector.Agent/Models/HostInfo.cs`

```csharp
using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class HostInfo
{
    [JsonPropertyName("hostname")] public string Hostname { get; init; } = string.Empty;
    [JsonPropertyName("fqdn")] public string? Fqdn { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("domain_joined")] public bool DomainJoined { get; init; }
    [JsonPropertyName("workgroup")] public string? Workgroup { get; init; }
    [JsonPropertyName("os_name")] public string OsName { get; init; } = string.Empty;
    [JsonPropertyName("os_version")] public string OsVersion { get; init; } = string.Empty;
    [JsonPropertyName("os_build")] public string OsBuild { get; init; } = string.Empty;
    [JsonPropertyName("os_edition")] public string? OsEdition { get; init; }
    [JsonPropertyName("architecture")] public string Architecture { get; init; } = "x64";
    [JsonPropertyName("locale")] public string? Locale { get; init; }
    [JsonPropertyName("timezone")] public string? Timezone { get; init; }
    [JsonPropertyName("last_boot")] public DateTimeOffset? LastBoot { get; init; }
    [JsonPropertyName("ip_addresses")] public List<string> IpAddresses { get; init; } = [];
}
```

---

### [FILE] `src/PolicyCollector.Agent/Models/GpoResult.cs`

```csharp
using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class GpoResult
{
    [JsonPropertyName("last_refresh")] public DateTimeOffset? LastRefresh { get; init; }
    [JsonPropertyName("refresh_status")] public string RefreshStatus { get; init; } = "Unknown";
    [JsonPropertyName("computer_gpos")] public List<GpoEntry> ComputerGpos { get; init; } = [];
    [JsonPropertyName("user_gpos")] public List<GpoEntry> UserGpos { get; init; } = [];
    [JsonPropertyName("cse_results")] public List<CseResult> CseResults { get; init; } = [];
}

public sealed class GpoEntry
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("guid")] public string? Guid { get; init; }
    [JsonPropertyName("link_path")] public string? LinkPath { get; init; }
    [JsonPropertyName("link_order")] public int LinkOrder { get; init; }
    [JsonPropertyName("applied")] public bool Applied { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("version_ad")] public int? VersionAd { get; init; }
    [JsonPropertyName("version_sysvol")] public int? VersionSysvol { get; init; }
}

public sealed class CseResult
{
    [JsonPropertyName("cse_name")] public string CseName { get; init; } = string.Empty;
    [JsonPropertyName("extension_guid")] public string? ExtensionGuid { get; init; }
    [JsonPropertyName("last_success")] public DateTimeOffset? LastSuccess { get; init; }
    [JsonPropertyName("last_error")] public string? LastError { get; init; }
    [JsonPropertyName("error_code")] public int ErrorCode { get; init; }
}
```

---

### [FILE] `src/PolicyCollector.Agent/Models/SecPolicyResult.cs`

```csharp
using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class SecPolicyResult
{
    [JsonPropertyName("password_policy")] public PasswordPolicy? PasswordPolicy { get; init; }
    [JsonPropertyName("audit_policy")] public AuditPolicy? AuditPolicy { get; init; }
    [JsonPropertyName("user_rights")] public Dictionary<string, List<string>>? UserRights { get; init; }
    [JsonPropertyName("uac")] public UacConfig? Uac { get; init; }
    [JsonPropertyName("tls")] public TlsConfig? Tls { get; init; }
    [JsonPropertyName("rdp")] public RdpConfig? Rdp { get; init; }
}

public sealed class PasswordPolicy
{
    [JsonPropertyName("min_length")] public int MinLength { get; init; }
    [JsonPropertyName("complexity_enabled")] public bool ComplexityEnabled { get; init; }
    [JsonPropertyName("max_age_days")] public int MaxAgeDays { get; init; }
    [JsonPropertyName("min_age_days")] public int MinAgeDays { get; init; }
    [JsonPropertyName("history_count")] public int HistoryCount { get; init; }
    [JsonPropertyName("lockout_threshold")] public int LockoutThreshold { get; init; }
    [JsonPropertyName("lockout_duration_min")] public int LockoutDurationMin { get; init; }
    [JsonPropertyName("lockout_window_min")] public int LockoutWindowMin { get; init; }
    [JsonPropertyName("reversible_encryption")] public bool ReversibleEncryption { get; init; }
}

public sealed class AuditPolicy
{
    [JsonPropertyName("account_logon")] public string? AccountLogon { get; init; }
    [JsonPropertyName("account_management")] public string? AccountManagement { get; init; }
    [JsonPropertyName("detailed_tracking")] public string? DetailedTracking { get; init; }
    [JsonPropertyName("ds_access")] public string? DsAccess { get; init; }
    [JsonPropertyName("logon_logoff")] public string? LogonLogoff { get; init; }
    [JsonPropertyName("object_access")] public string? ObjectAccess { get; init; }
    [JsonPropertyName("policy_change")] public string? PolicyChange { get; init; }
    [JsonPropertyName("privilege_use")] public string? PrivilegeUse { get; init; }
    [JsonPropertyName("system")] public string? System { get; init; }
}

public sealed class UacConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("consent_prompt_level")] public int ConsentPromptLevel { get; init; }
    [JsonPropertyName("secure_desktop")] public bool SecureDesktop { get; init; }
}

public sealed class TlsConfig
{
    [JsonPropertyName("protocols")] public TlsProtocols Protocols { get; init; } = new();
}

public sealed class TlsProtocols
{
    [JsonPropertyName("SSL_2_0")] public bool Ssl20 { get; init; }
    [JsonPropertyName("SSL_3_0")] public bool Ssl30 { get; init; }
    [JsonPropertyName("TLS_1_0")] public bool Tls10 { get; init; }
    [JsonPropertyName("TLS_1_1")] public bool Tls11 { get; init; }
    [JsonPropertyName("TLS_1_2")] public bool Tls12 { get; init; }
    [JsonPropertyName("TLS_1_3")] public bool Tls13 { get; init; }
}

public sealed class RdpConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("nla_required")] public bool NlaRequired { get; init; }
    [JsonPropertyName("port")] public int Port { get; init; } = 3389;
    [JsonPropertyName("session_timeout_min")] public int SessionTimeoutMin { get; init; }
    [JsonPropertyName("disconnect_timeout_min")] public int DisconnectTimeoutMin { get; init; }
}
```

---

### [FILE] `src/PolicyCollector.Agent/Models/` — Các models còn lại

```csharp
// FirewallResult.cs — tương tự SecPolicyResult, theo schema trong 02-data-models.md
// DefenderResult.cs  → fields: RealTimeProtection, CloudProtection, SignatureVersion, ...
// BitLockerVolume.cs → fields: Volume, Status, EncryptionMethod, ProtectionStatus
// AppEntry.cs        → fields: DisplayName, DisplayVersion, Publisher, InstallDate, ...
// AppxEntry.cs       → fields: Name, Version, Publisher, Architecture, InstallLocation
// ServiceEntry.cs    → fields: Name, DisplayName, Status, StartupType, Account, BinaryPath
// TaskEntry.cs       → fields: TaskName, State, LastRunTime, LastRunResult, RunAsUser, ...
// StartupEntry.cs    → fields: Name, Command, Location (RunKey/StartupFolder), Enabled
```

Mỗi file theo đúng JSON schema đã định nghĩa trong [02-data-models.md](../.ai-context/02-data-models.md).

---

### [FILE] `src/PolicyCollector.Agent/Infrastructure/RegistryReader.cs`

```csharp
namespace PolicyCollector.Agent.Infrastructure;

// Safe wrapper — không throw, trả về null/default nếu key không tồn tại
public sealed class RegistryReader
{
    // Đọc string value
    public string? GetString(RegistryHive hive, string keyPath, string valueName) { ··· }

    // Đọc DWORD
    public int? GetDword(RegistryHive hive, string keyPath, string valueName) { ··· }

    // Đọc QWORD
    public long? GetQword(RegistryHive hive, string keyPath, string valueName) { ··· }

    // Enumerate subkeys
    public IReadOnlyList<string> GetSubKeys(RegistryHive hive, string keyPath) { ··· }

    // Enumerate all values trong 1 key
    public IReadOnlyDictionary<string, object?> GetAllValues(RegistryHive hive, string keyPath) { ··· }

    // Check key exists
    public bool KeyExists(RegistryHive hive, string keyPath) { ··· }
}
// Dùng Microsoft.Win32.Registry — không cần P/Invoke
// Open với RegistryView.Registry64 để không bị redirect WOW6432Node
```

---

### [FILE] `src/PolicyCollector.Agent/Infrastructure/WmiQuery.cs`

```csharp
namespace PolicyCollector.Agent.Infrastructure;

// CIM/WMI query helper — mọi query đều async-safe (chạy trên ThreadPool)
public sealed class WmiQuery
{
    // Query WMI class, trả về list dictionary (property → value)
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string wmiClass,
        string? condition = null,
        string[]? properties = null,
        string namespacePath = @"root\cimv2",
        CancellationToken ct = default) { ··· }
    // Dùng ManagementObjectSearcher / CimSession
    // Chạy trong Task.Run để không block event loop
}
```

---

### [FILE] `src/PolicyCollector.Agent/Infrastructure/PowerShellRunner.cs`

```csharp
namespace PolicyCollector.Agent.Infrastructure;

// PS Runspace pool — tái sử dụng runspace, thread-safe
public sealed class PowerShellRunner : IDisposable
{
    private readonly RunspacePool _pool;

    public PowerShellRunner()
    {
        // Min=1, Max=3 runspaces
        _pool = RunspaceFactory.CreateRunspacePool(1, 3);
        _pool.Open();
    }

    // Chạy PS script, trả về collection of PSObject
    public Task<IReadOnlyList<PSObject>> RunScriptAsync(
        string script,
        CancellationToken ct = default) { ··· }

    // Chạy cmdlet với parameters
    public Task<IReadOnlyList<PSObject>> RunCmdletAsync(
        string cmdlet,
        Dictionary<string, object?>? parameters = null,
        CancellationToken ct = default) { ··· }

    public void Dispose() => _pool.Dispose();
}
```

---

### [FILE] `src/PolicyCollector.Agent/Infrastructure/ProcessRunner.cs`

```csharp
namespace PolicyCollector.Agent.Infrastructure;

// Chạy external process, capture stdout/stderr, enforce timeout
public sealed class ProcessRunner
{
    public record ProcessResult(int ExitCode, string Stdout, string Stderr);

    public async Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        TimeSpan timeout,
        CancellationToken ct = default) { ··· }
    // Dùng Process.StartAsync() — redirect stdout, stderr
    // Enforce timeout qua CancellationToken + WaitForExitAsync
    // Kill process nếu timeout
}
```

---

### [FILE] `src/PolicyCollector.Agent/appsettings.json`

```json
{
  "Agent": {
    "IntervalMinutes": 60,
    "CollectOnStartup": true,
    "CollectorTimeoutSeconds": 30,
    "Modules": {
      "GPO": true,
      "SecurityPolicy": true,
      "Firewall": true,
      "Defender": true,
      "BitLocker": true,
      "AppInventory": true,
      "AppxPackages": false,
      "Services": true,
      "ScheduledTasks": true,
      "StartupEntries": true
    }
  },
  "Transport": {
    "BackendUrl": "",
    "TimeoutSeconds": 30,
    "MaxRetries": 5,
    "InitialRetryDelaySeconds": 10,
    "UseMtls": false,
    "ClientCertStore": "LocalMachine",
    "ClientCertThumbprint": ""
  },
  "LocalQueue": {
    "MaxAgeHours": 168,
    "MaxEntries": 1000,
    "RetryIntervalMinutes": 5
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft": "Warning" }
    },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "C:\\ProgramData\\PolicyCollector\\logs\\agent-.log",
          "rollingInterval": "Day",
          "fileSizeLimitBytes": 10485760,
          "retainedFileCountLimit": 7,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      },
      { "Name": "Console" }
    ]
  }
}
```

---

### [FILE] `tests/PolicyCollector.Agent.Tests/PolicyCollector.Agent.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="FluentAssertions" Version="6.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
    <ProjectReference Include="..\..\src\PolicyCollector.Agent\PolicyCollector.Agent.csproj" />
  </ItemGroup>
</Project>
```

---

## Acceptance Criteria

- [ ] `dotnet build` thành công, 0 error, 0 warning (nullable enabled)
- [ ] `dotnet test` thành công
- [ ] `Program.cs` khởi động được dưới `dotnet run` (không crash dù collectors chưa đăng ký)
- [ ] `RegistryReader.GetString()` trả về null thay vì throw khi key không tồn tại
- [ ] `CollectorResult<T>.Fail()` không bao giờ throw
- [ ] Không có hardcoded string nào ngoài appsettings.json

## Lệnh khởi tạo nhanh

```powershell
dotnet new sln -n PolicyCollector
mkdir src, tests
dotnet new worker -n PolicyCollector.Agent -o src/PolicyCollector.Agent
dotnet new xunit -n PolicyCollector.Agent.Tests -o tests/PolicyCollector.Agent.Tests
dotnet sln add src/PolicyCollector.Agent
dotnet sln add tests/PolicyCollector.Agent.Tests
```
