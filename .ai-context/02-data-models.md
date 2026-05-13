# 02 — Data Models

## Payload JSON (Agent → Backend)

### Envelope chung

```jsonc
{
  // === Metadata ===
  "schema_version": "1.0",           // Semver — Backend từ chối nếu major khác
  "collection_id": "<uuid-v4>",       // UUID duy nhất mỗi lần collect
  "collected_at": "<ISO-8601-UTC>",   // Thời điểm BẮT ĐẦU collect
  "agent_version": "1.0.0",

  // === Host ===
  "host": { /* xem HostInfo */ },

  // === Modules (null nếu module bị tắt) ===
  "gpo":             { /* GpoResult    */ },
  "security_policy": { /* SecPolicyResult */ },
  "firewall":        { /* FirewallResult */ },
  "defender":        { /* DefenderResult */ },
  "bitlocker":       [ /* BitLockerVolume[] */ ],
  "applications":    [ /* AppEntry[]    */ ],
  "appx_packages":   [ /* AppxEntry[]   */ ],
  "services":        [ /* ServiceEntry[] */ ],
  "scheduled_tasks": [ /* TaskEntry[]   */ ],
  "startup_entries": [ /* StartupEntry[] */ ],
  "active_directory": { /* AdInfo — null nếu không join domain hoặc module tắt */ },
  "registry_audit":   { /* RegistryAuditResult */ }
}
```

### HostInfo

```jsonc
{
  "hostname":        "WS-FINANCE-01",
  "fqdn":            "WS-FINANCE-01.corp.local",
  "domain":          "corp.local",
  "domain_joined":   true,
  "workgroup":       null,
  "os_name":         "Windows 11 Pro",
  "os_version":      "10.0.22621",
  "os_build":        "22621.3296",
  "os_edition":      "Professional",
  "architecture":    "x64",
  "locale":          "vi-VN",
  "timezone":        "SE Asia Standard Time",
  "last_boot":       "2026-05-10T07:30:00Z",
  "ip_addresses":    ["192.168.1.101", "10.0.0.5"]
}
```

### GpoResult

```jsonc
{
  "last_refresh":    "2026-05-12T08:30:00Z",
  "refresh_status":  "Success",           // Success | Failed | Unknown
  "computer_gpos": [
    {
      "name":        "Default Domain Policy",
      "guid":        "{31B2F340-016D-11D2-945F-00C04FB984F9}",
      "link_path":   "DC=corp,DC=local",
      "link_order":  1,
      "applied":     true,
      "reason":      null,                // Lý do nếu không apply được
      "version_ad":  65536,
      "version_sysvol": 65536
    }
  ],
  "user_gpos":       [],
  "cse_results": [
    {
      "cse_name":    "Registry",
      "extension_guid": "{35378EAC-683F-11D2-A89A-00C04FBBCFA2}",
      "last_success":"2026-05-12T08:30:00Z",
      "last_error":  null,
      "error_code":  0
    }
  ]
}
```

### SecPolicyResult

```jsonc
{
  "password_policy": {
    "min_length":           12,
    "complexity_enabled":   true,
    "max_age_days":         90,
    "min_age_days":         1,
    "history_count":        24,
    "lockout_threshold":    5,
    "lockout_duration_min": 30,
    "lockout_window_min":   30,
    "reversible_encryption":false
  },
  "audit_policy": {
    "account_logon":        "Success,Failure",
    "account_management":   "Success,Failure",
    "detailed_tracking":    "Success",
    "ds_access":            "No Auditing",
    "logon_logoff":         "Success,Failure",
    "object_access":        "Failure",
    "policy_change":        "Success",
    "privilege_use":        "Failure",
    "system":               "Success,Failure"
  },
  "user_rights": {
    "SeDebugPrivilege":              ["Administrators"],
    "SeRemoteInteractiveLogonRight": ["Administrators", "Remote Desktop Users"],
    "SeShutdownPrivilege":           ["Administrators", "Users"]
  },
  "uac": {
    "enabled":              true,
    "consent_prompt_level": 2,    // 0=No prompt, 1=Prompt cred, 2=Prompt, 5=Default
    "secure_desktop":       true
  },
  "tls": {
    "protocols": {
      "SSL_2_0": false,
      "SSL_3_0": false,
      "TLS_1_0": false,
      "TLS_1_1": false,
      "TLS_1_2": true,
      "TLS_1_3": true
    }
  },
  "rdp": {
    "enabled":              true,
    "nla_required":         true,
    "port":                 3389,
    "session_timeout_min":  60,
    "disconnect_timeout_min": 0
  }
}
```

### FirewallResult

```jsonc
{
  "profiles": {
    "domain":  { "enabled": true,  "inbound": "Block", "outbound": "Allow" },
    "private": { "enabled": true,  "inbound": "Block", "outbound": "Allow" },
    "public":  { "enabled": true,  "inbound": "Block", "outbound": "Allow" }
  },
  "rules_summary": {
    "total": 142, "enabled": 98, "inbound": 60, "outbound": 82
  },
  "rules": [
    {
      "name":        "Allow RDP",
      "display_name":"Remote Desktop - User Mode (TCP-In)",
      "direction":   "Inbound",
      "action":      "Allow",
      "enabled":     true,
      "protocol":    "TCP",
      "local_port":  "3389",
      "remote_port": "Any",
      "profile":     "Domain,Private",
      "program":     "%SystemRoot%\\system32\\svchost.exe"
    }
  ]
}
```

### AppEntry (Registry Uninstall)

```jsonc
{
  "display_name":   "Google Chrome",
  "display_version":"124.0.6367.82",
  "publisher":      "Google LLC",
  "install_date":   "20240415",
  "install_location":"C:\\Program Files\\Google\\Chrome\\Application",
  "uninstall_string":"MsiExec.exe /X{...}",
  "architecture":   "x64",           // x64 | x86
  "source":         "MSI",           // MSI | EXE | Unknown
  "registry_hive":  "HKLM",          // HKLM | HKCU
  "registry_key":   "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{...}"
}
```

### ServiceEntry

```jsonc
{
  "name":           "WinRM",
  "display_name":   "Windows Remote Management (WS-Management)",
  "status":         "Running",        // Running | Stopped | Paused
  "startup_type":   "Automatic",      // Automatic | AutomaticDelayedStart | Manual | Disabled
  "account":        "NT AUTHORITY\\NetworkService",
  "binary_path":    "C:\\Windows\\System32\\svchost.exe -k RPCSS",
  "description":    "...",
  "pid":            1234
}
```

### TaskEntry

```jsonc
{
  "task_name":      "\\Microsoft\\Windows\\UpdateOrchestrator\\Schedule Scan",
  "state":          "Ready",          // Ready | Running | Disabled | Unknown
  "last_run_time":  "2026-05-12T06:00:00Z",
  "last_run_result":0,                // 0 = SUCCESS, HRESULT otherwise
  "next_run_time":  "2026-05-13T06:00:00Z",
  "run_as_user":    "SYSTEM",
  "actions": [
    { "type": "Exec", "path": "C:\\Windows\\System32\\UsoClient.exe", "args": "StartScan" }
  ],
  "triggers": [
    { "type": "Daily", "start_boundary": "2026-01-01T06:00:00" }
  ]
}
```

### AdInfo

```jsonc
{
  "domain_controller":  "DC01.corp.local",
  "site_name":          "MainSite",
  "ou_path":            "CN=WS-FINANCE-01,OU=Finance,OU=Workstations,DC=corp,DC=local",
  "kerberos_available": true
}
```

> `active_directory` là `null` khi máy không join domain.

### RegistryAuditResult

```jsonc
{
  "lsa": {
    "lm_compat_level":                 5,
    "no_lm_hash":                      true,
    "restrict_anonymous":              1,
    "restrict_anonymous_sam":          true,
    "disable_restricted_admin":        false,
    "run_as_ppl":                      true,
    "disable_domain_creds":            false,
    "local_account_token_filter_policy": false
  },
  "wdigest": {
    "use_logon_credential": false
  },
  "smb": {
    "smb1_enabled":           false,
    "smb1_driver_start":      4,       // 4=Disabled, 3=Manual, 2=Auto
    "server_signing_enabled": true,
    "client_signing_required":false
  },
  "powershell_policy": {
    "execution_policy":    "RemoteSigned",
    "script_block_logging":true,
    "transcription":       false
  },
  "winlogon": {
    "userinit":        "C:\\Windows\\system32\\userinit.exe,",
    "shell":           "explorer.exe",
    "auto_admin_logon":false
  },
  "credential_guard": {
    "vbs_enabled":  true,
    "lsa_cfg_flags": 1     // 0=disabled, 1=enabled, 2=enabled+UEFI lock
  },
  "dangerous_flags": [
    {
      "name":           "LSASS_PPL_Disabled",
      "registry_path":  "HKLM\\SYSTEM\\CurrentControlSet\\Control\\Lsa",
      "value_name":     "RunAsPPL",
      "actual_value":   "(not set)",
      "expected_value": "1",
      "severity":       "medium",
      "description":    "LSASS not running as Protected Process Light — credential dumping possible without driver"
    }
  ]
}
```

**Severity levels:** `critical` | `high` | `medium` | `low`

---

## Database Schema (PostgreSQL)

### Bảng chính

```sql
-- Mỗi lần agent check-in
CREATE TABLE collection_snapshots (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    collection_id   UUID NOT NULL UNIQUE,
    hostname        TEXT NOT NULL,
    domain          TEXT,
    os_version      TEXT,
    agent_version   TEXT,
    collected_at    TIMESTAMPTZ NOT NULL,
    received_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    payload         JSONB NOT NULL,           -- raw payload đầy đủ
    schema_version  TEXT NOT NULL DEFAULT '1.0'
);

CREATE INDEX idx_snapshots_hostname ON collection_snapshots(hostname);
CREATE INDEX idx_snapshots_collected_at ON collection_snapshots(collected_at DESC);
CREATE INDEX idx_snapshots_payload ON collection_snapshots USING GIN(payload);

-- Hypertable cho TimescaleDB (timeseries queries)
SELECT create_hypertable('collection_snapshots', 'collected_at');
```

```sql
-- Bảng denormalized để query nhanh
CREATE TABLE host_latest (
    hostname        TEXT PRIMARY KEY,
    domain          TEXT,
    last_seen       TIMESTAMPTZ,
    agent_version   TEXT,
    os_version      TEXT,
    snapshot_id     UUID REFERENCES collection_snapshots(id),
    status          TEXT DEFAULT 'online'  -- online | offline | unknown
);
```

```sql
-- Violations được phát hiện sau mỗi snapshot
CREATE TABLE policy_violations (
    id              BIGSERIAL PRIMARY KEY,
    snapshot_id     UUID REFERENCES collection_snapshots(id),
    hostname        TEXT NOT NULL,
    detected_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    rule_id         TEXT NOT NULL,         -- e.g. "password.min_length"
    severity        TEXT NOT NULL,         -- critical | high | medium | low
    message         TEXT NOT NULL,
    expected        TEXT,
    actual          TEXT,
    resolved        BOOLEAN DEFAULT FALSE,
    resolved_at     TIMESTAMPTZ
);

CREATE INDEX idx_violations_hostname ON policy_violations(hostname);
CREATE INDEX idx_violations_resolved ON policy_violations(resolved) WHERE resolved = FALSE;
```

```sql
-- Changelog giữa 2 snapshot liên tiếp
CREATE TABLE config_changes (
    id              BIGSERIAL PRIMARY KEY,
    hostname        TEXT NOT NULL,
    changed_at      TIMESTAMPTZ NOT NULL,
    field_path      TEXT NOT NULL,         -- e.g. "security_policy.password.min_length"
    old_value       TEXT,
    new_value       TEXT,
    snapshot_before UUID REFERENCES collection_snapshots(id),
    snapshot_after  UUID REFERENCES collection_snapshots(id)
);
```

---

## Validation Rules (Backend)

| Field | Rule |
|---|---|
| `schema_version` | Major version phải bằng Backend supported version |
| `collection_id` | UUID v4, unique trong DB |
| `collected_at` | Không vượt quá 24h trong tương lai, không quá 7 ngày trong quá khứ |
| `host.hostname` | Regex `^[a-zA-Z0-9\-\.]{1,255}$` |
| `agent_version` | Semver format |
| Payload size | Tối đa 10 MB |

---

## Versioning Schema

- `schema_version` dùng semver: `MAJOR.MINOR`
- **MINOR bump** — thêm field mới (backward compatible, Backend chấp nhận).
- **MAJOR bump** — thay đổi breaking structure — Backend từ chối và trả `HTTP 422` với hướng dẫn upgrade.
- Agent phải handle `HTTP 422` bằng cách log lỗi và không retry.
