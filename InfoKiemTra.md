# InfoKiemTra — Danh sách kiểm tra & script đang sử dụng

> Agent thu thập định kỳ (mặc định 60 phút). Mỗi collector chạy song song, có timeout riêng (mặc định 30s).  
> Yêu cầu quyền: **LocalSystem** hoặc thành viên **Administrators** để có đầy đủ data.  
> Máy không join AD: các collector AD trả về rỗng, không gây lỗi.

---

## Tổng quan nhanh

| # | Module | Cơ chế | Quyền tối thiểu |
|---|--------|--------|-----------------|
| 1 | GPO | Process (`gpresult.exe`) | Admin |
| 2 | SecurityPolicy | Process (`secedit`, `auditpol`) | Admin |
| 3 | Firewall | PowerShell (`Get-NetFirewallProfile/Rule`) | User |
| 4 | Defender | WMI `root\Microsoft\Windows\Defender` | User |
| 5 | BitLocker | PowerShell (`Get-BitLockerVolume`) | Admin |
| 6 | AppInventory | Registry `Uninstall` keys | User |
| 7 | AppxPackages | PowerShell (`Get-AppxPackage`) | Admin |
| 8 | Services | WMI `Win32_Service` | User |
| 9 | ScheduledTasks | Process (`schtasks.exe`) | User |
| 10 | StartupEntries | Registry `Run` / `RunOnce` keys | User |
| 11 | ActiveDirectory | Registry + Process (`nltest.exe`) | User |
| 12 | RegistryAudit | Registry (nhiều key bảo mật) | User |
| 13 | Patch | Registry WU + WMI `Win32_QuickFixEngineering` | User |
| 14 | LocalAccounts | WMI `Win32_UserAccount` + Process (`net.exe`) | User |
| 15 | SharedFolders | WMI `Win32_Share` + PowerShell (`Get-SmbShareAccess`) | User |
| 16 | HardwareSecurity | PowerShell (`Confirm-SecureBootUEFI`) + WMI TPM + Registry | User |
| 17 | EventLogSettings | PowerShell (`Get-WinEvent -ListLog`) + Registry WEF | Admin |
| 18 | RemoteAccess | WMI `Win32_Service` + Registry | User |
| 19 | LAPS | Registry | User |

---

## Chi tiết từng Collector

---

### 1. GPO — Group Policy Objects

**Mục tiêu:** Liệt kê các GPO đang áp dụng lên máy tính, trạng thái áp dụng.

**Script / Lệnh:**
```
gpresult.exe /X "<tempfile>.xml" /SCOPE COMPUTER /FORCE
```

**Cơ chế:** Chạy `gpresult.exe` → xuất XML vào file tạm → parse XML (namespace `http://www.microsoft.com/GroupPolicy/Rsop`) → xoá file tạm.

**Data thu thập:**
- Tên GPO, GUID, Link path, thứ tự áp dụng (LinkOrder)
- Applied = true/false, lý do bị filter

**Ghi chú:** ExitCode = 2 → Access denied (không phải Admin). Máy không join AD → GPO local only.

---

### 2. SecurityPolicy — Chính sách bảo mật hệ thống

**Mục tiêu:** Password policy, user rights, audit policy, UAC, TLS protocols, RDP config.

**Script / Lệnh:**

```
# Chạy song song:
secedit.exe /export /cfg "<tempfile>.inf" /areas SECURITYPOLICY USER_RIGHTS /quiet
auditpol.exe /get /category:* /r
```

**Cơ chế:**
- `secedit` → xuất `.inf` → parse section `[System Access]` (password) và `[Privilege Rights]` (user rights) → xoá file tạm
- `auditpol` → xuất CSV, format: `Machine,Category,Subcategory,GUID,InclusionSetting,ExclusionSetting`
- Registry HKLM (không cần admin): UAC, TLS protocols, RDP config

**Registry đọc thêm:**
| Key | Value | Mục đích |
|-----|-------|----------|
| `SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` | `EnableLUA`, `ConsentPromptBehaviorAdmin`, `PromptOnSecureDesktop` | UAC |
| `SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\<ver>\Server` | `Enabled` | TLS/SSL |
| `SYSTEM\CurrentControlSet\Control\Terminal Server` | `fDenyTSConnections` | RDP on/off |
| `SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp` | `SecurityLayer`, `PortNumber`, `MaxIdleTime`, `MaxDisconnectionTime` | RDP config |

**Data thu thập:**
- `PasswordPolicy`: MinLength, Complexity, MaxAge, MinAge, HistoryCount, Lockout
- `UserRights`: dictionary `privilege → list<SID/account>`
- `AuditPolicy`: category-level summary + `Subcategories` dictionary (toàn bộ subcategory → setting)
- `UacConfig`, `TlsConfig`, `RdpConfig`

**Ghi chú:** `secedit` và `auditpol` yêu cầu Admin — thiếu quyền → trả về object rỗng + log Warning.

---

### 3. Firewall — Windows Firewall

**Mục tiêu:** Trạng thái profile, danh sách rule đang enabled.

**Script / Lệnh (PowerShell):**
```powershell
Get-NetFirewallProfile | Select-Object Name, Enabled, DefaultInboundAction, DefaultOutboundAction | ConvertTo-Json -Compress

Get-NetFirewallRule | Where-Object { $_.Enabled -eq $true } |
    Select-Object -First 500 Name, DisplayName, Direction, Action, Enabled, Profile, Description |
    ConvertTo-Json -Compress

Get-NetFirewallRule | Measure-Object | Select-Object -ExpandProperty Count
```

**Data thu thập:**
- Profiles: Domain / Private / Public — Enabled, DefaultInbound/OutboundAction
- Rules: tối đa 500 rule đang enabled — Name, Direction, Action, Profile
- Tổng số rule (Total, Enabled, Inbound, Outbound)

---

### 4. Defender — Windows Defender / Microsoft Antimalware

**Mục tiêu:** Trạng thái antivirus, real-time protection, cloud protection, phiên bản signature.

**WMI queries:**
```
Namespace : root\Microsoft\Windows\Defender
Class     : MSFT_MpComputerStatus
Properties: AntivirusEnabled, RealTimeProtectionEnabled, NISEnabled,
            AntivirusSignatureVersion, AntispywareSignatureVersion

Class     : MSFT_MpPreference
Properties: MAPSReporting
```

**Data thu thập:** AntivirusEnabled, RealTimeProtection, CloudProtection (MAPS), SignatureVersion

---

### 5. BitLocker — Mã hoá ổ đĩa

**Mục tiêu:** Trạng thái mã hoá từng volume.

**Script / Lệnh (PowerShell):**
```powershell
Get-BitLockerVolume | Select-Object MountPoint, VolumeStatus, EncryptionMethod,
    ProtectionStatus, EncryptionPercentage | ConvertTo-Json -Compress
```

**Data thu thập:** Volume (drive letter), VolumeStatus, EncryptionMethod (AES128/256/XTS-AES...), ProtectionStatus

**Ghi chú:** Yêu cầu Admin. Máy không có BitLocker → collector trả về danh sách rỗng + log Warning.

---

### 6. AppInventory — Ứng dụng đã cài (Win32)

**Mục tiêu:** Liệt kê tất cả phần mềm installed (MSI & EXE), cả x64 và x86.

**Registry keys:**
```
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*              (x64)
HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\* (x86)
```

**Values đọc:** `DisplayName`, `DisplayVersion`, `Publisher`, `InstallDate`, `InstallLocation`, `UninstallString`

**Data thu thập:** Danh sách app, version, publisher, ngày cài, đường dẫn, loại (MSI/EXE)

---

### 7. AppxPackages — UWP / Store Apps

**Mục tiêu:** Liệt kê tất cả UWP package của mọi user trên máy.

**Script / Lệnh (PowerShell):**
```powershell
Get-AppxPackage -AllUsers | Select-Object Name, Version, Publisher, Architecture, InstallLocation | ConvertTo-Json -Compress
```

**Data thu thập:** Name, Version, Publisher, Architecture, InstallLocation

**Ghi chú:** Mặc định disabled (`AppxPackages: false`) — enable nếu cần audit Store apps.

---

### 8. Services — Windows Services

**Mục tiêu:** Liệt kê tất cả service và trạng thái.

**WMI query:**
```
Class     : Win32_Service
Properties: Name, DisplayName, State, StartMode, StartName, PathName, Description, ProcessId
```

**Data thu thập:** Name, DisplayName, Status (Running/Stopped...), StartupType (Auto/Manual/Disabled), Account (StartName), BinaryPath, PID

---

### 9. ScheduledTasks — Scheduled Tasks

**Mục tiêu:** Liệt kê scheduled tasks ngoài thư mục `\Microsoft\` (third-party / user-defined).

**Script / Lệnh:**
```
schtasks.exe /Query /FO CSV /V /NH
```

**Cơ chế:** Parse CSV output, bỏ qua task path bắt đầu bằng `\Microsoft\`.

**Data thu thập:** TaskName, TaskPath, State, RunAsUser, LastRunTime, LastRunResult (exit code), NextRunTime

---

### 10. StartupEntries — Startup Registry

**Mục tiêu:** Các chương trình tự khởi động qua registry Run keys.

**Registry keys:**
```
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce
HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run
```

**Data thu thập:** Name, Command (đường dẫn exe), Location (tên key), Enabled = true

**Lưu ý bảo mật:** Key này là điểm persistence phổ biến của malware — xem thêm `RegistryAudit.DangerousFlags`.

---

### 11. ActiveDirectory — Thông tin domain

**Mục tiêu:** Xác định máy có join AD không, DC hiện tại, site, OU path.

**Registry (không cần admin):**
```
HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters
  → Domain, NV Domain    (tên domain)

HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\State\Machine
  → Distinguished-Name   (OU path đầy đủ, e.g. CN=PC01,OU=Workstations,DC=corp,DC=local)
```

**Script / Lệnh:**
```
nltest.exe /dsgetdc:<domain>
```
Parse output: `DC: \\<dcname>` và `Our Site Name: <site>`

**Data thu thập:** DomainController (FQDN của DC), SiteName, OuPath (Distinguished Name), KerberosAvailable

**Ghi chú:** Máy không join AD → trả về `{ kerberos_available: false }`, không có lỗi.

---

### 12. RegistryAudit — Registry bảo mật nguy hiểm

**Mục tiêu:** Đọc các registry key liên quan đến bảo mật, phát hiện cấu hình nguy hiểm (DangerousFlags).

**Registry keys (tất cả HKLM, không cần admin):**

| Key | Values đọc | Mục đích |
|-----|-----------|---------|
| `SYSTEM\CurrentControlSet\Control\Lsa` | `LmCompatibilityLevel`, `NoLMHash`, `RestrictAnonymous`, `RestrictAnonymousSAM`, `DisableRestrictedAdmin`, `RunAsPPL`, `DisableDomainCreds`, `LsaCfgFlags` | LSA / NTLM / LSASS |
| `SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest` | `UseLogonCredential` | WDigest cleartext |
| `SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters` | `SMB1`, `EnableSecuritySignature` | SMBv1, signing |
| `SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters` | `RequireSecuritySignature` | SMB client signing |
| `SYSTEM\CurrentControlSet\Services\mrxsmb10` | `Start` | SMBv1 driver |
| `SOFTWARE\Policies\Microsoft\Windows\PowerShell` | `ExecutionPolicy` | PS policy |
| `SOFTWARE\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging` | `EnableScriptBlockLogging` | PS logging |
| `SOFTWARE\Policies\Microsoft\Windows\PowerShell\Transcription` | `EnableTranscripting` | PS transcript |
| `SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` | `Userinit`, `Shell`, `AutoAdminLogon` | Persistence, autologon |
| `SYSTEM\CurrentControlSet\Control\DeviceGuard` | `EnableVirtualizationBasedSecurity` | VBS / Cred Guard |
| `SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` | `LocalAccountTokenFilterPolicy` | Remote UAC |

**DangerousFlags được phát hiện tự động:**

| Flag | Severity | Điều kiện | Mô tả |
|------|----------|-----------|-------|
| `WDigest_PlaintextCredentials` | **critical** | `UseLogonCredential = 1` | WDigest lưu cleartext trong LSASS — Mimikatz dump dễ dàng |
| `Winlogon_Shell_Modified` | **critical** | `Shell ≠ explorer.exe` | Shell bị thay — persistence backdoor |
| `Winlogon_Userinit_Modified` | **critical** | `Userinit` có thêm exe | Userinit bị sửa — exe chạy mỗi lần logon |
| `NTLM_LmCompatLevel_Weak` | **critical/high** | `LmCompatLevel < 3` | NTLMv1/LM chấp nhận — relay/crack được |
| `LmHash_Stored` | **high** | `NoLMHash = 0` | LM hash trong SAM — rainbow table crack |
| `RDP_RestrictedAdmin_Disabled` | **high** | `DisableRestrictedAdmin = 1` | Pass-the-hash qua RDP |
| `SMBv1_Enabled` | **high** | `SMB1 = 1` | EternalBlue / WannaCry |
| `SMBv1_Driver_Active` | **high** | `mrxsmb10\Start ≠ 4` | SMBv1 driver loadable |
| `AutoAdminLogon_Enabled` | **high** | `AutoAdminLogon = 1` | Credentials cleartext trong registry |
| `LocalAccountUAC_Bypass` | **high** | `LocalAccountTokenFilterPolicy = 1` | Pass-the-hash lateral movement |
| `LSASS_PPL_Disabled` | **medium** | `RunAsPPL ≠ 1` | Credential dump không cần driver |
| `Anonymous_Access_Unrestricted` | **medium** | `RestrictAnonymous = 0` | Enum SAM/shares ẩn danh |
| `PowerShell_ExecutionPolicy_Open` | **medium** | Policy = Unrestricted/Bypass | Script độc hại chạy không bị chặn |

---

### 13. Patch — Windows Update & Hotfix

**Mục tiêu:** Cấu hình Windows Update, WSUS, danh sách hotfix đã cài.

**Registry (HKLM):**
```
SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate
  → WUServer           (WSUS server URL nếu có)

SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU
  → AUOptions          (0=thông báo, 2=tải+thông báo, 3=tải+cài tự động, 4=cài tự động theo lịch)
  → NoAutoUpdate       (1 = tắt hoàn toàn Windows Update)

SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install
  → LastSuccessTime    (lần cài update thành công cuối)

SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Detect
  → LastSuccessTime    (lần check update thành công cuối)
```

**WMI query:**
```
Class     : Win32_QuickFixEngineering
Properties: HotFixID, Description, InstalledOn
```

**Data thu thập:** AutoUpdateOptions, NoAutoUpdate, WsusServer, LastSuccessInstall, LastSuccessDetect, danh sách Hotfixes (KB ID, mô tả, ngày cài)

---

### 14. LocalAccounts — Tài khoản local

**Mục tiêu:** Liệt kê tài khoản local và thành viên nhóm Administrators.

**WMI query:**
```
Class     : Win32_UserAccount
Condition : LocalAccount=True
Properties: Name, SID, Disabled, PasswordExpires, LastLogon, Description, AccountType
```

**Script / Lệnh:**
```
net.exe localgroup Administrators
```
Parse output: bỏ header và dòng separator `---`, lấy từng tên, detect domain account qua dấu `\`.

**Data thu thập:**
- `Accounts[]`: Name, SID, Enabled, PasswordExpires, LastLogon, IsBuiltinAdmin (AccountType = 512)
- `Administrators[]`: Name, Type (Local/Domain), IsDomain

---

### 15. SharedFolders — Thư mục chia sẻ (SMB)

**Mục tiêu:** Danh sách share và ACL của từng share.

**WMI query:**
```
Class     : Win32_Share
Properties: Name, Path, Description, Type, MaximumAllowed
```

**Script / Lệnh (PowerShell):**
```powershell
Get-SmbShareAccess -ErrorAction SilentlyContinue |
    Select-Object Name, AccountName, AccessControlType, AccessRight
```

**Data thu thập:** Share name, path, description, type (0=disk, 1=print...), MaxConnections, ACL per share (Account, AccessControlType: Allow/Deny, AccessRight: Full/Change/Read)

---

### 16. HardwareSecurity — Bảo mật phần cứng

**Mục tiêu:** Secure Boot, UEFI, TPM, VBS, HVCI.

**Script / Lệnh (PowerShell):**
```powershell
try { Confirm-SecureBootUEFI -ErrorAction Stop } catch { $null }
# Trả về $true (UEFI+SecureBoot enabled), $false (disabled), exception (BIOS/non-UEFI)
```

**WMI query:**
```
Namespace : root\cimv2\Security\MicrosoftTpm
Class     : Win32_Tpm
Properties: IsPresent, IsEnabled_InitialValue, IsActivated_InitialValue, SpecVersion
```

**Registry (HKLM):**
```
SYSTEM\CurrentControlSet\Control\DeviceGuard
  → EnableVirtualizationBasedSecurity   (VBS: 0=off, 1=on)

SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity
  → Enabled                             (HVCI: 0=off, 1=on)
```

**Data thu thập:** SecureBootEnabled (null = không xác định / BIOS), UefiMode, TpmPresent, TpmEnabled, TpmActivated, TpmVersion (SpecVersion), VbsStatus, HvciStatus

---

### 17. EventLogSettings — Cấu hình Event Log

**Mục tiêu:** Kích thước, trạng thái các log quan trọng; có bật Windows Event Forwarding không.

**Script / Lệnh (PowerShell):**
```powershell
$names = @('Security', 'System', 'Application',
           'Microsoft-Windows-PowerShell/Operational',
           'Microsoft-Windows-Sysmon/Operational',
           'Microsoft-Windows-TaskScheduler/Operational',
           'Microsoft-Windows-TerminalServices-LocalSessionManager/Operational',
           'Microsoft-Windows-Windows Defender/Operational')

Get-WinEvent -ListLog * -ErrorAction SilentlyContinue |
    Where-Object { $names -contains $_.LogName } |
    Select-Object LogName, IsEnabled, MaximumSizeInBytes, LogMode, RecordCount
```

**Registry (WEF):**
```
HKLM\SOFTWARE\Policies\Microsoft\Windows\EventLog\EventForwarding\SubscriptionManager
  → Bất kỳ value nào tồn tại → EventForwardingEnabled = true
```

**Data thu thập:** Với mỗi log: Name, Enabled, MaxSizeMb, LogMode (Circular/Retain/AutoBackup), RecordCount. EventForwardingEnabled (bool).

**Ghi chú:** Collector cố tình chỉ check 8 log cụ thể thay vì toàn bộ để tránh data quá lớn.

---

### 18. RemoteAccess — WinRM / SSH / Telnet

**Mục tiêu:** Trạng thái các service remote access và cấu hình bảo mật.

**WMI query:**
```
Class     : Win32_Service
Condition : Name='WinRM' OR Name='sshd' OR Name='TlntSvr'
Properties: Name, State
```

**Registry (HKLM):**
```
SOFTWARE\Policies\Microsoft\Windows\WinRM\Client   → AllowBasic, AllowUnencrypted
SOFTWARE\Policies\Microsoft\Windows\WinRM\Service  → AllowBasic, AllowUnencrypted
SOFTWARE\Policies\Microsoft\Windows\WinRM\Service\WinRS → AllowRemoteShellAccess
SOFTWARE\OpenSSH → DefaultShell
SYSTEM\CurrentControlSet\Services\TlntSvr → Start
```

**Data thu thập:**
- **WinRM**: ServiceStatus, AllowBasicAuth, AllowUnencrypted, AllowRemoteShellAccess
- **OpenSSH**: Installed, ServiceStatus, DefaultShell
- **TelnetServer**: true nếu service Start < 4 hoặc đang Running

---

### 19. LAPS — Local Administrator Password Solution

**Mục tiêu:** Phát hiện LAPS có được triển khai không (Windows LAPS built-in hoặc Legacy LAPS MSI).

**Registry (HKLM, không cần admin):**

**Windows LAPS** (Win 11 22H2+ / Server 2025):
```
SOFTWARE\Microsoft\Policies\LAPS
  → BackupDirectory       (0=disabled, 1=AD, 2=Azure AD)
  → PasswordAgeDays
  → AdministratorAccountName
  → PasswordExpirationProtectionEnabled
```

**Legacy LAPS** (MSI riêng):
```
SOFTWARE\Policies\Microsoft Services\AdmPwd
  → AdmPwdEnabled         (1 = enabled)
  → PasswordAgeDays
  → AdminAccountName
```

**Logic:** Kiểm tra Windows LAPS trước; nếu không có thì kiểm Legacy LAPS; nếu cả hai không có → `PolicyConfigured = false`.

**Data thu thập:** Type (WindowsLAPS/LegacyLAPS/null), PolicyConfigured, BackupDirectory (AD/AAD/null), PasswordExpiryDays, AdminAccountName

---

## Infrastructure

### Process Runner (`ProcessRunner`)
Chạy executable ngoài với timeout, capture stdout/stderr, trả về `ProcessResult(ExitCode, Stdout, Stderr)`.

### PowerShell Runner (`PowerShellRunner`)
Chạy PS script trong process hiện tại (không spawn powershell.exe mới), trả về `IReadOnlyList<PSObject>`.

### WMI Query (`WmiQuery`)
Wrapper quanh `System.Management.ManagementObjectSearcher`, hỗ trợ `condition` (WHERE clause) và `namespacePath` (mặc định `root\cimv2`).

### Registry Reader (`RegistryReader`)
Đọc registry 64-bit. Methods: `GetString`, `GetDword`, `GetQword`, `GetSubKeys`, `GetAllValues`, `KeyExists`.

---

## Quyền cần thiết theo collector

| Collector | LocalSystem | NetworkService | Domain Non-Admin |
|-----------|:-----------:|:--------------:|:----------------:|
| GPO | ✅ Full | ⚠️ Partial | ⚠️ Partial |
| SecurityPolicy (secedit+auditpol) | ✅ Full | ❌ Empty | ❌ Empty |
| Firewall | ✅ | ✅ | ✅ |
| Defender | ✅ | ✅ | ✅ |
| BitLocker | ✅ | ❌ May fail | ❌ May fail |
| AppInventory | ✅ | ✅ | ✅ |
| AppxPackages | ✅ | ⚠️ | ⚠️ |
| Services | ✅ | ✅ | ✅ |
| ScheduledTasks | ✅ | ✅ | ✅ |
| StartupEntries | ✅ | ✅ | ✅ |
| ActiveDirectory | ✅ | ✅ | ✅ |
| RegistryAudit | ✅ | ✅ | ✅ |
| Patch | ✅ | ✅ | ✅ |
| LocalAccounts | ✅ | ✅ | ✅ |
| SharedFolders | ✅ | ✅ | ✅ |
| HardwareSecurity | ✅ | ✅ | ✅ |
| EventLogSettings | ✅ | ⚠️ | ⚠️ |
| RemoteAccess | ✅ | ✅ | ✅ |
| LAPS | ✅ | ✅ | ✅ |

> ⚠️ = có thể thiếu data một số trường. ❌ = collector trả về empty/fail, ghi log Warning.
