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
| 20 | EndpointProtection | WMI `root\SecurityCenter2` + Registry `KasperskyLab` | User |
| 21 | WiFi | Process (`netsh.exe`) + PowerShell | Admin |

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

#### 2a. Security Options (Local Security Policy → Security Settings → Local Policies → Security Options)

Các setting quan trọng được đọc qua registry; tên hiển thị trong `secpol.msc` ghi kèm để đối chiếu:

| Registry key (HKLM) | Value name | Tên trong secpol.msc | Giá trị an toàn |
|---------------------|------------|----------------------|-----------------|
| `SYSTEM\CurrentControlSet\Control\Lsa` | `LmCompatibilityLevel` | Network security: LAN Manager authentication level | ≥ 3 (NTLMv2 only) |
| `SYSTEM\CurrentControlSet\Control\Lsa` | `NoLMHash` | Network security: Do not store LAN Manager hash value on next password change | 1 |
| `SYSTEM\CurrentControlSet\Control\Lsa` | `RestrictAnonymous` | Network access: Do not allow anonymous enumeration of SAM accounts and shares | 2 |
| `SYSTEM\CurrentControlSet\Control\Lsa` | `RestrictAnonymousSAM` | Network access: Do not allow anonymous enumeration of SAM accounts | 1 |
| `SYSTEM\CurrentControlSet\Control\Lsa` | `DisableDomainCreds` | Network access: Do not allow storage of passwords and credentials for network authentication | 1 |
| `SYSTEM\CurrentControlSet\Control\Lsa` | `RunAsPPL` | Local Security Authority (LSA) protection | 1 |
| `SYSTEM\CurrentControlSet\Control\Lsa` | `DisableRestrictedAdmin` | Remote Desktop Services: Restricted Admin mode | 0 (Restricted Admin enabled) |
| `SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest` | `UseLogonCredential` | WDigest Authentication (không cleartext) | 0 |
| `SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters` | `RequireSecuritySignature` | Microsoft network client: Digitally sign communications (always) | 1 |
| `SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters` | `EnableSecuritySignature` | Microsoft network client: Digitally sign communications (if server agrees) | 1 |
| `SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters` | `RequireSecuritySignature` | Microsoft network server: Digitally sign communications (always) | 1 |
| `SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters` | `EnableSecuritySignature` | Microsoft network server: Digitally sign communications (if client agrees) | 1 |
| `SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` | `EnableLUA` | User Account Control: Run all administrators in Admin Approval Mode | 1 |
| `SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` | `ConsentPromptBehaviorAdmin` | UAC: Behavior of elevation prompt for administrators | 2 (prompt for creds on secure desktop) |
| `SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` | `PromptOnSecureDesktop` | UAC: Switch to secure desktop when prompting for elevation | 1 |
| `SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` | `LocalAccountTokenFilterPolicy` | Remote UAC token filter | 0 (không bypass UAC từ xa) |
| `SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` | `AutoAdminLogon` | Interactive logon: auto-logon | 0 |
| `SYSTEM\CurrentControlSet\Control\DeviceGuard` | `EnableVirtualizationBasedSecurity` | Virtualization Based Security | 1 |

#### 2b. Cấu hình Audit quan trọng (Advanced Audit Policy)

Điều kiện tiên quyết: `HKLM\SYSTEM\CurrentControlSet\Control\Lsa\SCENoApplyLegacyAuditPolicy = 1` — bắt buộc để subcategory audit (auditpol) ghi đè category-level setting của Group Policy.

| Category | Subcategory | GUID | Cấu hình tối thiểu |
|----------|-------------|------|---------------------|
| Account Logon | Credential Validation | `{0CCE923F-…}` | Success + Failure |
| Account Logon | Kerberos Authentication Service | `{0CCE9242-…}` | Success + Failure |
| Logon/Logoff | Logon | `{0CCE9215-…}` | Success + Failure |
| Logon/Logoff | Account Lockout | `{0CCE9217-…}` | Failure |
| Logon/Logoff | Special Logon | `{0CCE921B-…}` | Success |
| Detailed Tracking | Process Creation (Event 4688) | `{0CCE922B-…}` | Success |
| Account Management | User Account Management | `{0CCE9235-…}` | Success + Failure |
| Account Management | Security Group Management | `{0CCE9237-…}` | Success + Failure |
| Account Management | Computer Account Management | `{0CCE9236-…}` | Success + Failure |
| Policy Change | Audit Policy Change | `{0CCE922F-…}` | Success |
| Policy Change | Authentication Policy Change | `{0CCE9230-…}` | Success |
| Privilege Use | Sensitive Privilege Use | `{0CCE9228-…}` | Success + Failure |
| System | Security State Change | `{0CCE9210-…}` | Success + Failure |
| System | Security System Extension | `{0CCE9211-…}` | Success |
| Object Access | File System | `{0CCE921D-…}` | Failure (nếu có file server) |

**Cách đọc trong code:** `auditpol /get /category:* /r` → xuất CSV, cột `Inclusion Setting` = `Success`, `Failure`, `Success and Failure`, `No Auditing`. Parse vào dictionary `SubcategoryName → InclusionSetting`.

**Violation rule gợi ý:** `audit.credential_validation_not_audited` — khi `InclusionSetting` của Credential Validation không bao gồm `Failure`.

---

### 3. Firewall — Windows Firewall + Listening Ports + Risky Ports

**Mục tiêu:** Trạng thái profile, danh sách rule, port đang lắng nghe, và phát hiện port nguy hiểm đang bị phơi bày.

**Script / Lệnh (PowerShell):**

```powershell
# 1. Profiles — KHÔNG dùng ConvertTo-Json; đọc PSObject.Properties trực tiếp
Get-NetFirewallProfile | Select-Object Name, Enabled, DefaultInboundAction, DefaultOutboundAction

# 2. Rules — dùng ConvertTo-Json → JsonDocument.Parse; join với port/app filter
$rules = Get-NetFirewallRule | Where-Object { $_.Enabled -eq $true } |
    Select-Object -First 500 Name, DisplayName, Direction, Action, Enabled, Profile, Description, InstanceID |
    ConvertTo-Json -Compress

# 3. Port filter — join theo InstanceID lấy Protocol, LocalPort, RemotePort
Get-NetFirewallPortFilter | Select-Object InstanceID, Protocol, LocalPort, RemotePort | ConvertTo-Json -Compress

# 4. Application filter — join theo InstanceID lấy đường dẫn chương trình
Get-NetFirewallApplicationFilter | Select-Object InstanceID, Program | ConvertTo-Json -Compress

# 5. TCP listening ports
Get-NetTCPConnection -State Listen |
    Select-Object LocalAddress, LocalPort, OwningProcess | ConvertTo-Json -Compress

# 6. UDP listening ports
Get-NetUDPEndpoint |
    Select-Object LocalAddress, LocalPort, OwningProcess | ConvertTo-Json -Compress

# 7. Tổng số rule
Get-NetFirewallRule | Measure-Object | Select-Object -ExpandProperty Count
```

**Lưu ý kỹ thuật:** Profiles dùng `PSObject.Properties["Name"].Value` (không qua ConvertTo-Json). Rules, filters, ports dùng `ConvertTo-Json -Compress` rồi `JsonDocument.Parse` trên `BaseObject as string` — hai luồng xử lý phải khác nhau, không lẫn lộn.

**Risky Port Definitions (21 entries):**

| Port | Risk | Mô tả |
|------|------|--------|
| 21 | high | FTP – cleartext credentials |
| 22 | medium | SSH – brute force target |
| 23 | critical | Telnet – plaintext remote shell |
| 25 | high | SMTP relay – spam/phishing pivot |
| 53 | medium | DNS – poisoning / amplification |
| 80 | low | HTTP – unencrypted web service |
| 135 | high | RPC/DCOM – lateral movement vector |
| 137 | high | NetBIOS Name Service – recon/spoofing |
| 138 | high | NetBIOS Datagram – recon |
| 139 | high | NetBIOS Session – SMB legacy / Pass-the-Hash |
| 389 | medium | LDAP (cleartext) |
| 443 | low | HTTPS – web, thường chấp nhận |
| 445 | critical | SMB – EternalBlue / ransomware / lateral movement |
| 1433 | high | MSSQL – direct DB exposure |
| 1434 | high | MSSQL Browser – UDP enumeration |
| 3306 | high | MySQL – direct DB exposure |
| 3389 | high | RDP – brute force / BlueKeep |
| 4444 | critical | Metasploit default listener |
| 5985 | high | WinRM HTTP – PowerShell remoting |
| 5986 | medium | WinRM HTTPS |
| 8080 | low | HTTP alt – dev/proxy service |

**Logic phát hiện `RiskyPorts`:**
1. Thu thập tất cả TCP/UDP port đang LISTEN vào `ListeningPorts[]`
2. Với mỗi port trong `RiskyPortDefs`: kiểm tra xem có entry trong `ListeningPorts` không → `IsListening`
3. Kiểm tra inbound rules: có rule `Direction=Inbound, Action=Allow` với `LocalPort` khớp (hoặc `Any`) không → `HasInboundAllowRule`
4. Rule `LocalPort = "Any"` / rỗng → đánh dấu **tất cả** risky port là `HasInboundAllowRule = true`
5. Output vào `RiskyPorts[]`: Port, Protocol, RiskLevel, Description, IsListening, HasInboundAllowRule, ProcessName

**Violation rule:** `network.risky_port_exposed` — kích hoạt khi `IsListening = true AND HasInboundAllowRule = true`, severity theo RiskLevel (critical → critical, high → high).

**Data thu thập:**
- `Profiles[]`: Domain / Private / Public — Enabled, DefaultInbound/OutboundAction
- `Rules[]`: tối đa 500 rule đang enabled — Name, Direction, Action, Profile, Protocol, LocalPort, RemotePort, Program
- `TotalRules`, `EnabledRules`, `InboundRules`, `OutboundRules`
- `ListeningPorts[]`: Protocol, Address, Port, Pid, ProcessName
- `RiskyPorts[]`: Port, Protocol, RiskLevel, Description, IsListening, HasInboundAllowRule, ProcessName

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
Properties: Name, SID, Disabled, PasswordExpires, Description, AccountType
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

### 20. EndpointProtection — Antivirus / Endpoint Security (Kaspersky)

**Mục tiêu:** Phát hiện AV/FW đã đăng ký với Windows Security Center, và khai thác chi tiết Kaspersky Endpoint Security nếu có.

#### WMI — Windows Security Center (`root\SecurityCenter2`)

```
Namespace : root\SecurityCenter2
Class     : AntiVirusProduct
Properties: displayName, productState, timestamp

Class     : FirewallProduct
Properties: displayName, productState, timestamp
```

**Decode `productState` (24-bit integer):**

| Byte (hex) | Vị trí | Ý nghĩa | Giá trị |
|-----------|--------|---------|---------|
| Byte 2 (`(state >> 16) & 0xFF`) | Bit 12–15 | Enabled state | `0x10` = Enabled, `0x00` = Disabled |
| Byte 3 (`(state >> 8) & 0xFF`) | Bit 8–11 | Signature/Definition state | `0x00` = Up-to-date, `0x10` = Outdated |

```csharp
// Ví dụ: productState = 397568 = 0x061100
// byte2 = 0x11 = enabled, byte3 = 0x00 = up-to-date → Enabled=true, UpToDate=true
bool enabled  = ((productState >> 12) & 0xF) == 1;
bool upToDate = ((productState >> 4) & 0xF) == 0;
```

#### Registry — Kaspersky Endpoint Security (`HKLM\SOFTWARE\KasperskyLab\`)

Duyệt qua các subkey (ví dụ `KES`, `AVP21.3`, ...) để tìm version và trạng thái:

```
HKLM\SOFTWARE\KasperskyLab\<ProductKey>
  → ProductName         (string)
  → Version             (string, e.g. "21.3.10.391")
  → InstallPath         (string)

HKLM\SOFTWARE\KasperskyLab\<ProductKey>\settings
  → AvEnabled           (DWORD, 1 = AV enabled)
  → AvUpToDate          (DWORD, 1 = signatures current)
```

`FirewallRegistered`: lấy từ WMI — Kaspersky có đăng ký FirewallProduct trong SecurityCenter2 không.

#### Logic FirewallNote

| Kịch bản | FirewallNote |
|----------|-------------|
| Không có Kaspersky nào | "Kaspersky không phát hiện — kiểm tra thủ công firewall qua Windows Defender Firewall" |
| Kaspersky có nhưng không đăng ký Firewall với SecurityCenter2 | "Kaspersky Endpoint Security phát hiện nhưng chưa đăng ký firewall component — kiểm tra policy KES" |
| Kaspersky có và đã đăng ký Firewall | "Kaspersky Endpoint Security firewall đang hoạt động" |

**Data thu thập (`EndpointProtectionResult`):**
- `AntivirusProducts[]`: SecurityProduct (Name, Enabled, UpToDate, StateHex, Timestamp)
- `FirewallProducts[]`: SecurityProduct
- `KasperskyDetected` (bool)
- `Kaspersky`: KasperskyDetail (ProductName, Version, InstallPath, AvEnabled, AvUpToDate, FirewallRegistered, FirewallEnabled)
- `FirewallNote` (string)

---

### 21. WiFi — Kết nối mạng không dây

**Mục tiêu:** Liệt kê tất cả WiFi profile đã từng lưu, kiểm tra phương thức mã hoá, phát hiện kết nối không có mã hoá (WEP, Open) theo tiêu chuẩn bảo mật tối thiểu WPA2.

#### Script / Lệnh

```
# Bước 1 — Lấy danh sách profile name đã lưu
netsh wlan show profiles

# Bước 2 — Chi tiết từng profile (kể cả password nếu agent chạy SYSTEM)
netsh wlan show profile name="<SSID>" key=clear
```

Parse output `netsh wlan show profiles` → lấy dòng `All User Profile` → trích SSID.

Parse output `netsh wlan show profile name="..." key=clear`:
- `Authentication` → auth type (WPA2-Personal, WPA3-Personal, WEP, Open, ...)
- `Cipher` → cipher (CCMP, TKIP, WEP, None)
- `Key Content` → password (nếu key=clear và chạy SYSTEM)

```powershell
# Bước 3 — Kết nối WiFi đang active
Get-NetConnectionProfile |
    Select-Object Name, InterfaceAlias, NetworkCategory, IPv4Connectivity, IPv6Connectivity |
    ConvertTo-Json -Compress
```

**Bảng phân loại rủi ro:**

| Authentication | Cipher | Risk Level | Mô tả |
|---------------|--------|------------|-------|
| Open | None | **critical** | Không có mã hoá — traffic bị nghe lén hoàn toàn |
| WEP | WEP | **critical** | WEP bị crack trong vài phút bằng aircrack-ng |
| WPA-Personal | TKIP | **high** | TKIP có lỗ hổng; WPA(v1) không còn được khuyến nghị |
| WPA2-Personal | TKIP | **medium** | WPA2 nhưng dùng TKIP thay vì CCMP |
| WPA2-Personal | CCMP | safe | Tối thiểu được chấp nhận |
| WPA3-Personal | GCMP | safe | Khuyến nghị cho môi trường hiện đại |
| WPA2-Enterprise | CCMP | safe | Enterprise với 802.1X — tốt nhất |

**Tiêu chuẩn tối thiểu:** WPA2 + CCMP (AES). Bất kỳ profile nào dùng `Open`, `WEP`, hoặc `WPA (v1)` đều tạo violation `wifi.insecure_profile`.

**Violation rule:** `wifi.insecure_profile` — severity `critical` nếu Open/WEP, `high` nếu WPA(v1).

**Data thu thập (`WiFiResult`):**
- `Profiles[]`: SSID, Authentication, Cipher, RiskLevel, IsCurrentlyConnected
- `ActiveConnections[]`: InterfaceAlias, NetworkName, NetworkCategory
- `HasInsecureProfile` (bool)
- `InsecureProfiles[]`: danh sách SSID vi phạm

**Ghi chú:** `netsh wlan show profile name="..." key=clear` yêu cầu chạy dưới **SYSTEM** hoặc **Local Admin** để lấy `Key Content`. Thiếu quyền → vẫn lấy được auth/cipher, chỉ thiếu password.

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
| EndpointProtection | ✅ | ✅ | ✅ |
| WiFi | ✅ Full (incl. key) | ⚠️ Auth/cipher only | ⚠️ Auth/cipher only |

> ⚠️ = có thể thiếu data một số trường. ❌ = collector trả về empty/fail, ghi log Warning.
