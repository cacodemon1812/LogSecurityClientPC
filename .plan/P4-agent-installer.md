# P4 — Agent: WiX MSI Installer + GPO ADMX

**Phase:** 1 | **Phụ thuộc:** P2, P3 | **Output:** MSI cài được silent, ADMX template

## Mục tiêu

Tạo MSI installer (WiX v4) với silent install support, Custom Action lưu API key vào Credential Manager, ACL đúng trên thư mục, và ADMX template để phân phối cấu hình qua GPO.

## Files cần tạo

```
installer/
  setup.wxs                    # WiX source chính
  CustomActions/
    PolicyCollector.CA.csproj  # Custom Action project
    CredentialAction.cs        # Lưu API key vào Credential Manager
  admx/
    PolicyCollector.admx       # GPO Administrative Template
    en-US/
      PolicyCollector.adml     # Language file (English)
    vi-VN/
      PolicyCollector.adml     # Language file (Vietnamese)
dist/                          # Build output (gitignored)
```

---

## Chi tiết từng file

### [FILE] `installer/setup.wxs`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">

  <Package Name="PolicyCollector"
           Manufacturer="Your Organization"
           Version="$(var.Version)"
           UpgradeCode="{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
           Scope="perMachine">

    <!-- Tự động uninstall version cũ khi upgrade -->
    <MajorUpgrade DowngradeErrorMessage="A newer version is already installed." />

    <!-- Properties có thể set khi silent install -->
    <Property Id="BACKEND_URL" Value="" />
    <Property Id="API_KEY" Secure="yes" />
    <Property Id="HMAC_SECRET" Secure="yes" />
    <Property Id="INTERVAL_MIN" Value="60" />

    <!-- Require Windows 10 1809+ -->
    <Launch Condition="VersionNT &gt;= 1810"
            Message="This application requires Windows 10 1809 or later." />

    <!-- Require x64 -->
    <Launch Condition="VersionNT64"
            Message="This application requires a 64-bit version of Windows." />

    <MediaTemplate EmbedCab="yes" />

    <Feature Id="ProductFeature" Title="PolicyCollector" Level="1">
      <ComponentGroupRef Id="AgentFiles" />
      <ComponentGroupRef Id="ConfigFiles" />
      <ComponentRef Id="ServiceComponent" />
    </Feature>

    <!-- Custom Actions -->
    <Binary Id="CustomActionCA"
            SourceFile="CustomActions/bin/Release/PolicyCollector.CA.dll" />

    <!-- Chạy sau InstallFinalize: lưu API key vào Credential Manager -->
    <CustomAction Id="SaveCredentials"
                  BinaryRef="CustomActionCA"
                  DllEntry="SaveCredentials"
                  Execute="deferred"
                  Impersonate="no"
                  Return="check">
      <CustomActionData>
        API_KEY=[API_KEY];HMAC_SECRET=[HMAC_SECRET];BACKEND_URL=[BACKEND_URL]
      </CustomActionData>
    </CustomAction>

    <!-- Cập nhật appsettings.json với BackendUrl và IntervalMinutes -->
    <CustomAction Id="WriteConfig"
                  BinaryRef="CustomActionCA"
                  DllEntry="WriteAgentConfig"
                  Execute="deferred"
                  Impersonate="no"
                  Return="check" />

    <!-- Cài Windows Service -->
    <CustomAction Id="InstallService"
                  BinaryRef="CustomActionCA"
                  DllEntry="InstallService"
                  Execute="deferred"
                  Impersonate="no"
                  Return="check" />

    <!-- Dừng và xóa service khi uninstall -->
    <CustomAction Id="UninstallService"
                  BinaryRef="CustomActionCA"
                  DllEntry="UninstallService"
                  Execute="deferred"
                  Impersonate="no"
                  Return="ignore" />

    <InstallExecuteSequence>
      <Custom Action="WriteConfig" Before="InstallFinalize">NOT Installed</Custom>
      <Custom Action="SaveCredentials" After="WriteConfig">NOT Installed AND API_KEY</Custom>
      <Custom Action="InstallService" After="SaveCredentials">NOT Installed</Custom>
      <Custom Action="UninstallService" Before="RemoveFiles">REMOVE="ALL"</Custom>
    </InstallExecuteSequence>
  </Package>

  <!-- ===== Directories ===== -->
  <Fragment>
    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="INSTALLDIR" Name="PolicyCollector" />
    </StandardDirectory>

    <StandardDirectory Id="CommonAppDataFolder">
      <Directory Id="DATAROOTDIR" Name="PolicyCollector">
        <Directory Id="LOGDIR" Name="logs" />
      </Directory>
    </StandardDirectory>
  </Fragment>

  <!-- ===== Files ===== -->
  <Fragment>
    <ComponentGroup Id="AgentFiles" Directory="INSTALLDIR">
      <Component Id="AgentExe" Guid="{...}">
        <File Id="AgentExeFile"
              Source="$(var.AgentBin)\PolicyCollector.Agent.exe"
              KeyPath="yes" />
      </Component>
    </ComponentGroup>

    <ComponentGroup Id="ConfigFiles" Directory="DATAROOTDIR">
      <Component Id="AppSettings" Guid="{...}">
        <!-- NeverOverwrite: giữ config nếu đã có -->
        <File Id="AppSettingsFile"
              Source="src/PolicyCollector.Agent/appsettings.json"
              NeverOverwrite="yes" />
      </Component>
    </ComponentGroup>
  </Fragment>

  <!-- ===== ACL ===== -->
  <Fragment>
    <!-- Install dir: Admins=FullControl, SYSTEM=FullControl, Users=ReadExecute -->
    <Component Id="InstallDirAcl" Directory="INSTALLDIR" Guid="{...}">
      <util:PermissionEx User="Administrators" GenericAll="yes" />
      <util:PermissionEx User="SYSTEM" GenericAll="yes" />
      <util:PermissionEx User="Users" GenericRead="yes" GenericExecute="yes" />
    </Component>

    <!-- Data dir: Admins=FullControl, SYSTEM=FullControl, Users=None -->
    <Component Id="DataDirAcl" Directory="DATAROOTDIR" Guid="{...}">
      <util:PermissionEx User="Administrators" GenericAll="yes" />
      <util:PermissionEx User="SYSTEM" GenericAll="yes" />
    </Component>
  </Fragment>

  <!-- ===== Service Component ===== -->
  <Fragment>
    <Component Id="ServiceComponent" Directory="INSTALLDIR" Guid="{...}">
      <!-- Service được cài bởi Custom Action thay vì WiX ServiceInstall
           để có full control over recovery options -->
      <RegistryValue Root="HKLM"
                     Key="SOFTWARE\PolicyCollector"
                     Name="InstalledVersion"
                     Type="string"
                     Value="$(var.Version)"
                     KeyPath="yes" />
    </Component>
  </Fragment>

</Wix>
```

---

### [FILE] `installer/CustomActions/CredentialAction.cs`

```csharp
// WiX Managed Custom Action (DTF)
// Cần reference: WixToolset.Dtf.WindowsInstaller

namespace PolicyCollector.Installer.CustomActions;

public static class CustomActions
{
    [CustomAction]
    public static ActionResult SaveCredentials(Session session)
    {
        // Parse CustomActionData
        var data = new CustomActionData(session["CustomActionData"]);
        var apiKey = data["API_KEY"];
        var hmacSecret = data["HMAC_SECRET"];
        var backendUrl = data["BACKEND_URL"];

        try
        {
            if (!string.IsNullOrEmpty(apiKey))
                WriteCredential("PolicyCollector/ApiKey", apiKey);

            if (!string.IsNullOrEmpty(hmacSecret))
                WriteCredential("PolicyCollector/HmacSecret", hmacSecret);

            session.Log("Credentials saved to Windows Credential Manager");
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"Failed to save credentials: {ex.Message}");
            return ActionResult.Failure;
        }
    }

    [CustomAction]
    public static ActionResult WriteAgentConfig(Session session)
    {
        var data = new CustomActionData(session["CustomActionData"]);
        var backendUrl = data["BACKEND_URL"];
        var intervalMin = int.TryParse(data["INTERVAL_MIN"], out var m) ? m : 60;

        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PolicyCollector", "appsettings.json");

        try
        {
            // Đọc template config từ install dir, merge với install-time values
            var installDir = session["INSTALLDIR"];
            var templatePath = Path.Combine(installDir, "appsettings.json");

            var json = File.ReadAllText(templatePath);
            // Thay thế placeholder values
            json = json.Replace("\"BackendUrl\": \"\"", $"\"BackendUrl\": \"{backendUrl}\"");
            // ... interval, etc.

            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            if (!File.Exists(configPath))  // NeverOverwrite logic
                File.WriteAllText(configPath, json);

            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"Failed to write config: {ex.Message}");
            return ActionResult.Failure;
        }
    }

    [CustomAction]
    public static ActionResult InstallService(Session session)
    {
        var installDir = session["INSTALLDIR"];
        var exePath = Path.Combine(installDir, "PolicyCollector.Agent.exe");

        try
        {
            // sc create
            RunSc($"create PolicyCollectorSvc binPath= \"{exePath}\" start= delayed-auto obj= LocalSystem");
            RunSc("description PolicyCollectorSvc \"Collects system configuration and sends to central management\"");
            RunSc("failure PolicyCollectorSvc reset= 86400 actions= restart/60000/restart/60000/restart/60000");
            RunSc("start PolicyCollectorSvc");

            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"Failed to install service: {ex.Message}");
            return ActionResult.Failure;
        }
    }

    [CustomAction]
    public static ActionResult UninstallService(Session session)
    {
        try
        {
            RunSc("stop PolicyCollectorSvc");
            Thread.Sleep(2000);
            RunSc("delete PolicyCollectorSvc");
            return ActionResult.Success;
        }
        catch { return ActionResult.Success; }  // Ignore — service may not exist
    }

    private static void RunSc(string arguments) { ··· }  // Process.Start("sc.exe", arguments)

    // P/Invoke CredWrite
    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite([In] ref CREDENTIAL credential, [In] uint flags);

    private static void WriteCredential(string target, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var cred = new CREDENTIAL
        {
            TargetName = target,
            CredentialBlobSize = (uint)blob.Length,
            CredentialBlob = Marshal.AllocHGlobal(blob.Length),
            Persist = 2,  // LOCAL_MACHINE
            Type = 1      // GENERIC
        };
        Marshal.Copy(blob, 0, cred.CredentialBlob, blob.Length);
        try
        {
            if (!CredWrite(ref cred, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(cred.CredentialBlob); }
    }
}
```

---

### [FILE] `installer/admx/PolicyCollector.admx`

```xml
<?xml version="1.0" encoding="utf-8"?>
<policyDefinitions xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                   xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                   xmlns="http://schemas.microsoft.com/GroupPolicy/2006/07/PolicyDefinitions"
                   revision="1.0" schemaVersion="1.0">

  <policyNamespaces>
    <target prefix="policycollector"
            namespace="PolicyCollector.Policies" />
  </policyNamespaces>

  <resources minRequiredRevision="1.0" />

  <categories>
    <category name="PolicyCollector" displayName="$(string.CategoryName)" />
  </categories>

  <policies>

    <!-- Backend URL -->
    <policy name="BackendUrl"
            class="Machine"
            displayName="$(string.BackendUrl)"
            explainText="$(string.BackendUrl_Explain)"
            key="SOFTWARE\Policies\PolicyCollector"
            valueName="BackendUrl">
      <parentCategory ref="PolicyCollector" />
      <supportedOn ref="windows:SUPPORTED_Windows10" />
      <elements>
        <text id="BackendUrl_Value" valueName="BackendUrl" required="true" />
      </elements>
    </policy>

    <!-- Collection Interval -->
    <policy name="IntervalMinutes"
            class="Machine"
            displayName="$(string.IntervalMinutes)"
            explainText="$(string.IntervalMinutes_Explain)"
            key="SOFTWARE\Policies\PolicyCollector"
            valueName="IntervalMinutes">
      <parentCategory ref="PolicyCollector" />
      <supportedOn ref="windows:SUPPORTED_Windows10" />
      <elements>
        <decimal id="Interval_Value" valueName="IntervalMinutes"
                 minValue="15" maxValue="1440" />
      </elements>
    </policy>

    <!-- Enable/Disable Modules -->
    <policy name="EnableGPO"
            class="Machine"
            displayName="$(string.EnableGPO)"
            explainText="$(string.EnableGPO_Explain)"
            key="SOFTWARE\Policies\PolicyCollector\Modules"
            valueName="GPO">
      <parentCategory ref="PolicyCollector" />
      <supportedOn ref="windows:SUPPORTED_Windows10" />
      <enabledValue><decimal value="1" /></enabledValue>
      <disabledValue><decimal value="0" /></disabledValue>
    </policy>

    <!-- ... tương tự cho SecurityPolicy, Firewall, Defender, BitLocker, AppInventory, Services, ScheduledTasks -->

  </policies>
</policyDefinitions>
```

---

### [FILE] `installer/admx/en-US/PolicyCollector.adml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<policyDefinitionResources xmlns="http://schemas.microsoft.com/GroupPolicy/2006/07/PolicyDefinitions"
                            revision="1.0" schemaVersion="1.0">
  <displayName>PolicyCollector</displayName>
  <description>Settings for the PolicyCollector agent service</description>
  <resources>
    <stringTable>
      <string id="CategoryName">PolicyCollector</string>
      <string id="BackendUrl">Backend Collection URL</string>
      <string id="BackendUrl_Explain">
        The HTTPS URL of the PolicyCollector backend ingest endpoint.
        Example: https://collector.corp.local/api/v1/ingest
        If not configured, the agent will use the value from appsettings.json.
      </string>
      <string id="IntervalMinutes">Collection Interval (minutes)</string>
      <string id="IntervalMinutes_Explain">
        How often the agent collects and sends data, in minutes.
        Minimum: 15. Maximum: 1440 (24 hours). Default: 60.
      </string>
      <string id="EnableGPO">Enable GPO Collection Module</string>
      <string id="EnableGPO_Explain">
        When enabled, the agent collects Group Policy Object information.
      </string>
    </stringTable>
  </resources>
</policyDefinitionResources>
```

---

## Build & Distribution Script

### [FILE] `scripts/Build-Installer.ps1`

```powershell
param(
    [string]$Version = "1.0.0",
    [string]$OutputDir = "dist"
)

$ErrorActionPreference = "Stop"

# 1. Build Agent (self-contained)
Write-Host "Building agent..."
dotnet publish src/PolicyCollector.Agent `
    -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -o "installer/bin/agent"

# 2. Build Custom Action DLL
Write-Host "Building custom actions..."
dotnet build installer/CustomActions/PolicyCollector.CA.csproj `
    -c Release

# 3. Build MSI
Write-Host "Building MSI..."
wix build installer/setup.wxs `
    -d Version=$Version `
    -d AgentBin="installer/bin/agent" `
    -o "$OutputDir/PolicyCollector-$Version-x64.msi"

Write-Host "Done: $OutputDir/PolicyCollector-$Version-x64.msi"
```

### Silent install command

```batch
msiexec /i "PolicyCollector-1.0.0-x64.msi" /qn /l*v "%TEMP%\PC-Install.log" ^
  BACKEND_URL="https://collector.corp.local/api/v1/ingest" ^
  API_KEY="your-32-char-minimum-api-key-here" ^
  HMAC_SECRET="your-base64-encoded-hmac-secret" ^
  INTERVAL_MIN="60"
```

---

## Acceptance Criteria

- [ ] `wix build setup.wxs` thành công (0 error)
- [ ] Silent install: `msiexec /i ... /qn BACKEND_URL=... API_KEY=...` hoàn tất không dialog
- [ ] Service `PolicyCollectorSvc` tồn tại và `Running` sau install
- [ ] `C:\Program Files\PolicyCollector\` ACL: Users chỉ Read+Execute
- [ ] `C:\ProgramData\PolicyCollector\` ACL: Users không có quyền
- [ ] API key có trong Windows Credential Manager sau install
- [ ] Uninstall: service bị xóa, exe bị xóa, ProgramData giữ nguyên
- [ ] Upgrade (install version mới): version cũ tự uninstall, service tiếp tục sau upgrade
- [ ] ADMX file valid: load được vào GPMC mà không lỗi
- [ ] GPO policy `BackendUrl` override được `appsettings.json`
