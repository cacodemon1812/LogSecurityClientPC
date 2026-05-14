"use client";

import { useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import {
  ArrowLeft, RefreshCw, Monitor, AlertTriangle, GitCompare,
  Shield, Cpu, HardDrive, Users, Package, Settings,
  Network, Key, FileText, CheckCircle2, XCircle, MinusCircle,
} from "lucide-react";
import { hostsApi, violationsApi } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import type { Violation, DiffChange } from "@/types/api";
import { formatDate, timeAgo } from "@/lib/utils";

// ── Types ────────────────────────────────────────────────────────────────────

interface CollectionPayload {
  schema_version: string;
  collection_id: string;
  collected_at: string;
  agent_version: string;
  host?: { hostname?: string; domain?: string; os_version?: string; [k: string]: unknown };
  defender?: { antivirus_enabled?: boolean; real_time_protection?: boolean; cloud_protection?: boolean; signature_version?: string };
  bitlocker?: Array<{ volume?: string; status?: string; encryption_method?: string; protection_status?: string }>;
  security_policy?: {
    password_policy?: {
      min_length: number; complexity_enabled: boolean; max_age_days: number;
      min_age_days: number; history_count: number; lockout_threshold: number;
      lockout_duration_min: number; lockout_window_min: number; reversible_encryption: boolean;
    };
    audit_policy?: { subcategories?: Record<string, string> };
    user_rights?: Record<string, string[]>;
    uac?: { enabled: boolean; consent_prompt_level: number; secure_desktop: boolean };
    tls?: { protocols?: { SSL_2_0?: boolean; SSL_3_0?: boolean; TLS_1_0?: boolean; TLS_1_1?: boolean; TLS_1_2?: boolean; TLS_1_3?: boolean } };
    rdp?: { enabled: boolean; nla_required: boolean; port: number; session_timeout_min: number; disconnect_timeout_min: number };
  };
  firewall?: {
    profiles?: Record<string, { enabled: boolean; inbound?: string; outbound?: string }>;
    rules_summary?: { total: number; enabled: number; inbound: number; outbound: number };
    rules?: Array<{
      name?: string; display_name?: string; direction?: string; action?: string;
      enabled?: boolean; protocol?: string; local_port?: string; remote_port?: string;
      profile?: string; program?: string;
    }>;
    listening_ports?: Array<{
      protocol?: string; address?: string; port: number; pid?: number; process_name?: string;
    }>;
    risky_ports?: Array<{
      port: number; protocol?: string; risk_level: string; description?: string;
      is_listening: boolean; has_inbound_allow_rule: boolean; process_name?: string;
    }>;
  };
  hardware_security?: { uefi_mode?: boolean; vbs_status?: number; hvci_status?: number; tpm_enabled?: boolean; tpm_present?: boolean; tpm_version?: string; tpm_activated?: boolean; secure_boot_enabled?: boolean };
  patch?: { hotfixes?: Array<{ hotfix_id: string; description?: string; installed_on?: string }>; wsus_server?: string; hotfix_count?: number; no_auto_update?: boolean; auto_update_options?: number; last_success_detect?: string; last_success_install?: string };
  local_accounts?: { accounts?: Array<{ name?: string; type?: string; is_domain?: boolean; enabled?: boolean; last_logon?: string }>; administrators?: Array<{ name?: string; type?: string; is_domain?: boolean }> };
  scheduled_tasks?: Array<{ task_name?: string; task_path?: string; state?: string; last_run_time?: string; last_run_result?: number; next_run_time?: string; run_as_user?: string }>;
  startup_entries?: Array<{ name?: string; command?: string; location?: string; enabled?: boolean }>;
  gpo?: { user_gpos?: unknown[]; computer_gpos?: unknown[]; cse_results?: unknown[]; last_refresh?: string; refresh_status?: string };
  remote_access?: {
    winrm?: { service_status?: string; allow_basic_auth?: boolean; allow_unencrypted?: boolean; allow_remote_shell_access?: boolean };
    openssh?: { installed?: boolean; default_shell?: string; service_status?: string };
    telnet_server?: boolean;
  };
  shared_folders?: { shares?: Array<{ name?: string; path?: string; description?: string; type?: number; max_connections?: number }> };
  event_log_settings?: { logs?: Array<{ log_name?: string; max_size_kb?: number; retention?: string; enabled?: boolean }>; event_forwarding_enabled?: boolean };
  laps?: { type?: string; backup_directory?: string; policy_configured?: boolean; admin_account_name?: string; password_expiry_days?: number };
  active_directory?: { ou_path?: string; site_name?: string; domain_controller?: string; kerberos_available?: boolean };
  registry_audit?: {
    lsa?: Record<string, unknown>;
    wdigest?: { use_logon_credential?: boolean };
    smb?: { smb1_enabled?: boolean; smb1_driver_start?: number; server_signing_enabled?: boolean; client_signing_required?: boolean };
    powershell_policy?: { execution_policy?: string; script_block_logging?: boolean; transcription?: boolean };
    winlogon?: { userinit?: string; shell?: string; auto_admin_logon?: boolean };
    credential_guard?: { vbs_enabled?: boolean; lsa_cfg_flags?: number };
    dangerous_flags?: Array<{ name: string; registry_path: string; value_name: string; actual_value?: string; expected_value?: string; severity: string; description?: string }>;
  };
  applications?: Array<{ display_name?: string; display_version?: string; publisher?: string; install_date?: string; architecture?: string; source?: string }>;
  services?: Array<{ name?: string; display_name?: string; status?: string; startup_type?: string; account?: string; binary_path?: string; pid?: number }>;
}

interface DiffResult {
  hostname: string;
  fromSnapshotId: string | null;
  toSnapshotId: string;
  fromTime: string | null;
  toTime: string;
  changes: DiffChange[];
}

// ── Small helpers ─────────────────────────────────────────────────────────────

function BoolIcon({ value, invert = false }: { value?: boolean | null; invert?: boolean }) {
  const ok = invert ? value === false : value === true;
  const bad = invert ? value === true : value === false;
  if (ok) return <CheckCircle2 className="h-4 w-4 text-green-600 shrink-0" />;
  if (bad) return <XCircle className="h-4 w-4 text-destructive shrink-0" />;
  return <MinusCircle className="h-4 w-4 text-muted-foreground shrink-0" />;
}

function InfoRow({ label, value, mono = false }: { label: string; value: React.ReactNode; mono?: boolean }) {
  return (
    <div className="flex items-start justify-between gap-4 py-2 border-b last:border-0 text-sm">
      <span className="text-muted-foreground shrink-0 min-w-[160px]">{label}</span>
      <span className={mono ? "font-mono text-xs text-right" : "text-right"}>{value ?? "—"}</span>
    </div>
  );
}

function EmptyMsg({ msg = "Không có dữ liệu" }: { msg?: string }) {
  return <p className="text-sm text-muted-foreground text-center py-6">{msg}</p>;
}

const severityVariant: Record<string, "critical" | "high" | "medium" | "low"> = {
  critical: "critical", high: "high", medium: "medium", low: "low",
};

// ── Tab: Tổng quan ────────────────────────────────────────────────────────────

function OverviewTab({ payload, violations }: { payload: CollectionPayload; violations: Violation[] }) {
  return (
    <div className="space-y-4">
      {violations.length === 0 ? (
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center gap-2 text-sm text-green-600">
              <CheckCircle2 className="h-4 w-4" />
              Không có vi phạm chính sách nào
            </div>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 text-destructive" />
              Vi phạm chưa xử lý ({violations.length})
            </CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <div className="divide-y max-h-96 overflow-y-auto">
              {violations.map((v) => (
                <div key={v.id} className="px-4 py-3 flex items-start gap-3">
                  <Badge variant={severityVariant[v.severity] ?? "secondary"} className="mt-0.5 shrink-0">
                    {v.severity}
                  </Badge>
                  <div className="min-w-0">
                    <p className="text-sm font-medium font-mono">{v.ruleId}</p>
                    <p className="text-xs text-muted-foreground">{v.message}</p>
                    {(v.expected || v.actual) && (
                      <p className="text-xs text-muted-foreground mt-0.5">
                        Mong đợi: <span className="text-foreground">{v.expected ?? "—"}</span>
                        {" "}· Thực tế: <span className="text-foreground">{v.actual ?? "—"}</span>
                      </p>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Thông tin hệ thống</CardTitle>
        </CardHeader>
        <CardContent>
          <InfoRow label="Hostname" value={<span className="font-mono">{payload.host?.hostname}</span>} />
          <InfoRow label="Domain" value={payload.host?.domain} />
          <InfoRow label="OS" value={payload.host?.os_version} />
          <InfoRow label="Agent version" value={<span className="font-mono">{payload.agent_version}</span>} />
          <InfoRow label="Schema version" value={payload.schema_version} />
          <InfoRow label="Collection ID" value={<span className="font-mono text-xs">{payload.collection_id}</span>} />
          <InfoRow label="Thu thập lúc" value={formatDate(payload.collected_at)} />
        </CardContent>
      </Card>
    </div>
  );
}

// ── Tab: Antivirus & BitLocker ────────────────────────────────────────────────

function AntivirusTab({ payload }: { payload: CollectionPayload }) {
  const d = payload.defender;
  const bl = payload.bitlocker ?? [];
  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base flex items-center gap-2">
            <Shield className="h-4 w-4" />Windows Defender
          </CardTitle>
        </CardHeader>
        <CardContent>
          {!d ? <EmptyMsg /> : (
            <>
              <InfoRow label="Antivirus bật" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={d.antivirus_enabled} />{d.antivirus_enabled ? "Có" : "Không"}</span>} />
              <InfoRow label="Real-time protection" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={d.real_time_protection} />{d.real_time_protection ? "Có" : "Không"}</span>} />
              <InfoRow label="Cloud protection" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={d.cloud_protection} />{d.cloud_protection ? "Có" : "Không"}</span>} />
              <InfoRow label="Phiên bản signature" value={<span className="font-mono">{d.signature_version}</span>} />
            </>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base flex items-center gap-2">
            <HardDrive className="h-4 w-4" />BitLocker
          </CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {bl.length === 0 ? (
            <div className="px-4 pb-4 pt-2">
              <EmptyMsg msg="Không có volume BitLocker nào được mã hóa" />
            </div>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b">
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Volume</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Trạng thái</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Mã hóa</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Bảo vệ</th>
                </tr>
              </thead>
              <tbody>
                {bl.map((v, i) => (
                  <tr key={i} className="border-b last:border-0">
                    <td className="px-4 py-2 font-mono">{v.volume ?? "—"}</td>
                    <td className="px-4 py-2">{v.status ?? "—"}</td>
                    <td className="px-4 py-2">{v.encryption_method ?? "Không mã hóa"}</td>
                    <td className="px-4 py-2">
                      <span className="flex items-center gap-1">
                        <BoolIcon value={v.protection_status === "Protected" || v.protection_status === "On"} />
                        {v.protection_status ?? "—"}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

// ── Tab: Hardware Security ────────────────────────────────────────────────────

function HardwareTab({ payload }: { payload: CollectionPayload }) {
  const h = payload.hardware_security;
  const laps = payload.laps;
  const ad = payload.active_directory;
  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base flex items-center gap-2">
            <Cpu className="h-4 w-4" />Bảo mật phần cứng
          </CardTitle>
        </CardHeader>
        <CardContent>
          {!h ? <EmptyMsg /> : (
            <>
              <InfoRow label="Chế độ UEFI" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={h.uefi_mode} />{h.uefi_mode ? "UEFI" : "Legacy BIOS"}</span>} />
              <InfoRow label="Secure Boot" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={h.secure_boot_enabled} />{h.secure_boot_enabled == null ? "Không xác định" : h.secure_boot_enabled ? "Bật" : "Tắt"}</span>} />
              <InfoRow label="TPM có mặt" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={h.tpm_present} />{h.tpm_present ? "Có" : "Không"}</span>} />
              <InfoRow label="TPM được bật" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={h.tpm_enabled} />{h.tpm_enabled ? "Có" : "Không"}</span>} />
              <InfoRow label="TPM được kích hoạt" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={h.tpm_activated} />{h.tpm_activated ? "Có" : "Không"}</span>} />
              <InfoRow label="Phiên bản TPM" value={h.tpm_version} />
              <InfoRow label="VBS status" value={h.vbs_status != null ? String(h.vbs_status) : undefined} />
              <InfoRow label="HVCI status" value={h.hvci_status != null ? String(h.hvci_status) : undefined} />
            </>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">LAPS (Local Admin Password Solution)</CardTitle>
        </CardHeader>
        <CardContent>
          {!laps ? <EmptyMsg /> : (
            <>
              <InfoRow label="Chính sách được cấu hình" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={laps.policy_configured} />{laps.policy_configured ? "Có" : "Không"}</span>} />
              <InfoRow label="Loại" value={laps.type} />
              <InfoRow label="Backup directory" value={laps.backup_directory} mono />
              <InfoRow label="Tên tài khoản admin" value={laps.admin_account_name} />
              <InfoRow label="Hết hạn mật khẩu (ngày)" value={laps.password_expiry_days != null ? String(laps.password_expiry_days) : undefined} />
            </>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Active Directory</CardTitle>
        </CardHeader>
        <CardContent>
          {!ad ? <EmptyMsg /> : (
            <>
              <InfoRow label="Domain Controller" value={ad.domain_controller} mono />
              <InfoRow label="OU Path" value={ad.ou_path} mono />
              <InfoRow label="Site" value={ad.site_name} />
              <InfoRow label="Kerberos khả dụng" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={ad.kerberos_available} />{ad.kerberos_available ? "Có" : "Không"}</span>} />
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

// ── Tab: Security Policy ──────────────────────────────────────────────────────

function PolicyTab({ payload }: { payload: CollectionPayload }) {
  const sp = payload.security_policy;
  const pp = sp?.password_policy;
  const uac = sp?.uac;
  const tls = sp?.tls?.protocols;
  const audit = sp?.audit_policy?.subcategories;
  const [showAllAudit, setShowAllAudit] = useState(false);
  const [showAllRights, setShowAllRights] = useState(false);

  const auditEntries = audit ? Object.entries(audit) : [];
  const visibleAudit = showAllAudit ? auditEntries : auditEntries.slice(0, 15);

  const rights = sp?.user_rights ? Object.entries(sp.user_rights) : [];
  const visibleRights = showAllRights ? rights : rights.slice(0, 10);

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Chính sách mật khẩu</CardTitle>
        </CardHeader>
        <CardContent>
          {!pp ? <EmptyMsg /> : (
            <>
              <InfoRow label="Độ dài tối thiểu" value={<span className={pp.min_length < 8 ? "text-destructive font-semibold" : ""}>{pp.min_length} ký tự</span>} />
              <InfoRow label="Độ phức tạp" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={pp.complexity_enabled} />{pp.complexity_enabled ? "Bật" : "Tắt"}</span>} />
              <InfoRow label="Tuổi tối đa (ngày)" value={pp.max_age_days === 0 ? <span className="text-destructive">Không hết hạn (0)</span> : String(pp.max_age_days)} />
              <InfoRow label="Tuổi tối thiểu (ngày)" value={String(pp.min_age_days)} />
              <InfoRow label="Lịch sử mật khẩu" value={pp.history_count === 0 ? <span className="text-destructive">Không lưu (0)</span> : String(pp.history_count)} />
              <InfoRow label="Ngưỡng khóa tài khoản" value={pp.lockout_threshold === 0 ? <span className="text-destructive">Không khóa (0)</span> : String(pp.lockout_threshold)} />
              <InfoRow label="Thời gian khóa (phút)" value={String(pp.lockout_duration_min)} />
              <InfoRow label="Mã hóa đảo ngược" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={pp.reversible_encryption} invert />{pp.reversible_encryption ? <span className="text-destructive">Bật (nguy hiểm)</span> : "Tắt"}</span>} />
            </>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">UAC (User Account Control)</CardTitle>
        </CardHeader>
        <CardContent>
          {!uac ? <EmptyMsg /> : (
            <>
              <InfoRow label="UAC bật" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={uac.enabled} />{uac.enabled ? "Có" : "Không"}</span>} />
              <InfoRow label="Mức độ xác nhận" value={String(uac.consent_prompt_level)} />
              <InfoRow label="Desktop bảo mật" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={uac.secure_desktop} />{uac.secure_desktop ? "Có" : "Không"}</span>} />
            </>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">TLS Protocols</CardTitle>
        </CardHeader>
        <CardContent>
          {!tls ? <EmptyMsg /> : (
            <>
              {(["SSL_2_0", "SSL_3_0", "TLS_1_0", "TLS_1_1", "TLS_1_2", "TLS_1_3"] as const).map((proto) => {
                const val = tls[proto];
                const isLegacy = ["SSL_2_0", "SSL_3_0", "TLS_1_0", "TLS_1_1"].includes(proto);
                return (
                  <InfoRow key={proto} label={proto.replace(/_/g, ".")} value={
                    <span className="flex items-center gap-1 justify-end">
                      <BoolIcon value={val} invert={isLegacy} />
                      {val == null ? "—" : val
                        ? <span className={isLegacy ? "text-destructive" : "text-green-600"}>Bật</span>
                        : <span className={!isLegacy ? "text-destructive" : "text-green-600"}>Tắt</span>}
                    </span>
                  } />
                );
              })}
            </>
          )}
        </CardContent>
      </Card>

      {audit && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Chính sách kiểm toán</CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b">
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Danh mục</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Cài đặt</th>
                </tr>
              </thead>
              <tbody>
                {visibleAudit.map(([cat, val]) => (
                  <tr key={cat} className="border-b last:border-0">
                    <td className="px-4 py-2 text-muted-foreground">{cat}</td>
                    <td className="px-4 py-2">
                      <span className={val === "No Auditing" ? "text-muted-foreground" : "text-foreground"}>{val}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {auditEntries.length > 15 && (
              <div className="px-4 py-2">
                <button onClick={() => setShowAllAudit(v => !v)} className="text-xs text-primary hover:underline">
                  {showAllAudit ? "Ẩn bớt" : `Xem thêm ${auditEntries.length - 15} mục`}
                </button>
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {rights.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">User Rights Assignment</CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b">
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Quyền</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Tài khoản được gán</th>
                </tr>
              </thead>
              <tbody>
                {visibleRights.map(([right, accounts]) => (
                  <tr key={right} className="border-b last:border-0">
                    <td className="px-4 py-2 font-mono text-xs">{right}</td>
                    <td className="px-4 py-2 text-xs text-muted-foreground">{accounts.join(", ")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {rights.length > 10 && (
              <div className="px-4 py-2">
                <button onClick={() => setShowAllRights(v => !v)} className="text-xs text-primary hover:underline">
                  {showAllRights ? "Ẩn bớt" : `Xem thêm ${rights.length - 10} quyền`}
                </button>
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}

// ── Tab: Firewall ─────────────────────────────────────────────────────────────

const RISK_STYLE: Record<string, string> = {
  critical: "bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400 border border-red-300 dark:border-red-700",
  high:     "bg-orange-100 text-orange-800 dark:bg-orange-900/30 dark:text-orange-400 border border-orange-300 dark:border-orange-700",
  medium:   "bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-400 border border-yellow-300 dark:border-yellow-700",
};

function RiskBadge({ level }: { level: string }) {
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-semibold uppercase ${RISK_STYLE[level] ?? "bg-muted text-muted-foreground"}`}>
      {level}
    </span>
  );
}

function FirewallTab({ payload }: { payload: CollectionPayload }) {
  const fw = payload.firewall;
  const profiles    = fw?.profiles ? Object.entries(fw.profiles) : [];
  const summary     = fw?.rules_summary;
  const rules       = fw?.rules ?? [];
  const listening   = fw?.listening_ports ?? [];
  const riskyPorts  = fw?.risky_ports ?? [];
  const [showRules,      setShowRules]      = useState(false);
  const [showListening,  setShowListening]  = useState(false);

  const criticalOrHigh = riskyPorts.filter(p => p.risk_level === "critical" || p.risk_level === "high");
  const exposedCount   = riskyPorts.filter(p => p.is_listening && p.has_inbound_allow_rule).length;

  return (
    <div className="space-y-4">

      {/* ── Stat strip ── */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <Card><CardContent className="pt-4 pb-3">
          <p className="text-xs text-muted-foreground uppercase tracking-wide">Tổng quy tắc</p>
          <p className="text-2xl font-bold mt-1">{summary?.total ?? "—"}</p>
        </CardContent></Card>
        <Card><CardContent className="pt-4 pb-3">
          <p className="text-xs text-muted-foreground uppercase tracking-wide">Đang bật</p>
          <p className="text-2xl font-bold mt-1 text-green-600">{summary?.enabled ?? "—"}</p>
        </CardContent></Card>
        <Card><CardContent className="pt-4 pb-3">
          <p className="text-xs text-muted-foreground uppercase tracking-wide">Listening ports</p>
          <p className="text-2xl font-bold mt-1">{listening.length || "—"}</p>
        </CardContent></Card>
        <Card><CardContent className="pt-4 pb-3">
          <p className="text-xs text-muted-foreground uppercase tracking-wide">Cổng nguy hiểm lộ</p>
          <p className={`text-2xl font-bold mt-1 ${exposedCount > 0 ? "text-destructive" : "text-green-600"}`}>
            {exposedCount}
          </p>
        </CardContent></Card>
      </div>

      {/* ── Risky ports — shown first, always visible if any exist ── */}
      {riskyPorts.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 text-orange-500" />
              Cổng có nguy cơ cao ({riskyPorts.length})
              {exposedCount > 0 && (
                <span className="ml-2 text-xs text-destructive font-normal">
                  ⚠ {exposedCount} cổng đang lộ ra mạng
                </span>
              )}
            </CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <div className="overflow-x-auto">
              <table className="w-full text-sm min-w-[680px]">
                <thead><tr className="border-b bg-muted/30">
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Port</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Proto</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Mức độ</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Mô tả rủi ro</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Đang lắng nghe</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Inbound Allow</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Process</th>
                </tr></thead>
                <tbody>
                  {riskyPorts.map((p) => {
                    const exposed = p.is_listening && p.has_inbound_allow_rule;
                    return (
                      <tr key={`${p.protocol}-${p.port}`}
                          className={`border-b last:border-0 ${exposed ? "bg-red-50/50 dark:bg-red-950/20" : ""}`}>
                        <td className="px-4 py-2 font-mono font-bold">{p.port}</td>
                        <td className="px-4 py-2 font-mono text-xs text-muted-foreground">{p.protocol ?? "—"}</td>
                        <td className="px-4 py-2"><RiskBadge level={p.risk_level} /></td>
                        <td className="px-4 py-2 text-xs text-muted-foreground max-w-[240px]">{p.description ?? "—"}</td>
                        <td className="px-4 py-2"><BoolIcon value={p.is_listening} /></td>
                        <td className="px-4 py-2">
                          {p.has_inbound_allow_rule
                            ? <span className="inline-flex items-center gap-1 text-destructive text-xs font-medium"><XCircle className="h-3 w-3" />Có rule Allow</span>
                            : <span className="inline-flex items-center gap-1 text-green-600 text-xs"><CheckCircle2 className="h-3 w-3" />Không có</span>}
                        </td>
                        <td className="px-4 py-2 font-mono text-xs">{p.process_name ?? "—"}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
      )}

      {/* ── Firewall Profiles ── */}
      {profiles.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Firewall Profiles</CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <table className="w-full text-sm">
              <thead><tr className="border-b bg-muted/30">
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Profile</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Trạng thái</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Inbound mặc định</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Outbound mặc định</th>
              </tr></thead>
              <tbody>
                {profiles.map(([name, p]) => (
                  <tr key={name} className="border-b last:border-0">
                    <td className="px-4 py-2 font-medium">{name}</td>
                    <td className="px-4 py-2">
                      <span className="flex items-center gap-1">
                        <BoolIcon value={p.enabled} />
                        {p.enabled ? "Bật" : <span className="text-destructive font-medium">Tắt</span>}
                      </span>
                    </td>
                    <td className="px-4 py-2 text-muted-foreground">{p.inbound ?? "—"}</td>
                    <td className="px-4 py-2 text-muted-foreground">{p.outbound ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </CardContent>
        </Card>
      )}

      {/* ── Listening ports (collapsible) ── */}
      {listening.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <div className="flex items-center justify-between">
              <CardTitle className="text-base">Listening Ports ({listening.length})</CardTitle>
              <button onClick={() => setShowListening(v => !v)} className="text-xs text-primary hover:underline">
                {showListening ? "Ẩn" : "Xem tất cả"}
              </button>
            </div>
          </CardHeader>
          {showListening && (
            <CardContent className="p-0">
              <div className="overflow-x-auto max-h-72 overflow-y-auto">
                <table className="w-full text-xs min-w-[500px]">
                  <thead><tr className="border-b sticky top-0 bg-card">
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Port</th>
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Proto</th>
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Address</th>
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Process</th>
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">PID</th>
                  </tr></thead>
                  <tbody>
                    {listening.map((lp, i) => {
                      const isRisky = riskyPorts.some(r => r.port === lp.port);
                      return (
                        <tr key={i} className={`border-b last:border-0 ${isRisky ? "bg-orange-50/50 dark:bg-orange-950/10" : ""}`}>
                          <td className={`px-3 py-1.5 font-mono font-bold ${isRisky ? "text-orange-600" : ""}`}>{lp.port}</td>
                          <td className="px-3 py-1.5 font-mono text-muted-foreground">{lp.protocol ?? "—"}</td>
                          <td className="px-3 py-1.5 font-mono text-muted-foreground">{lp.address ?? "—"}</td>
                          <td className="px-3 py-1.5 font-mono">{lp.process_name ?? "—"}</td>
                          <td className="px-3 py-1.5 text-muted-foreground">{lp.pid ?? "—"}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </CardContent>
          )}
        </Card>
      )}

      {/* ── Firewall Rules (collapsible) ── */}
      {rules.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <div className="flex items-center justify-between">
              <CardTitle className="text-base">
                Firewall Rules — Inbound Allow ({rules.filter(r => r.direction?.includes("Inbound") && r.action?.includes("Allow")).length} / {rules.length} enabled)
              </CardTitle>
              <button onClick={() => setShowRules(v => !v)} className="text-xs text-primary hover:underline">
                {showRules ? "Ẩn" : "Xem tất cả"}
              </button>
            </div>
          </CardHeader>
          {showRules && (
            <CardContent className="p-0">
              <div className="overflow-x-auto max-h-96 overflow-y-auto">
                <table className="w-full text-xs min-w-[820px]">
                  <thead><tr className="border-b sticky top-0 bg-card">
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Tên</th>
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Chiều</th>
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Hành động</th>
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Protocol</th>
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Local Port</th>
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Remote Port</th>
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Program</th>
                    <th className="px-3 py-2 text-left font-medium text-muted-foreground">Profile</th>
                  </tr></thead>
                  <tbody>
                    {rules.map((r, i) => {
                      const isInboundAllow = r.direction?.includes("Inbound") && r.action?.includes("Allow");
                      return (
                        <tr key={i} className={`border-b last:border-0 ${isInboundAllow ? "bg-orange-50/30 dark:bg-orange-950/10" : ""}`}>
                          <td className="px-3 py-1.5 max-w-[200px] truncate" title={r.display_name ?? r.name ?? ""}>{r.display_name ?? r.name ?? "—"}</td>
                          <td className="px-3 py-1.5">
                            <span className={`text-xs font-medium ${r.direction?.includes("Inbound") ? "text-orange-600" : "text-blue-600"}`}>
                              {r.direction ?? "—"}
                            </span>
                          </td>
                          <td className="px-3 py-1.5">
                            <span className={`text-xs font-medium ${r.action?.includes("Allow") ? "text-green-600" : "text-destructive"}`}>
                              {r.action ?? "—"}
                            </span>
                          </td>
                          <td className="px-3 py-1.5 font-mono text-muted-foreground">{r.protocol ?? "Any"}</td>
                          <td className="px-3 py-1.5 font-mono">{r.local_port ?? "Any"}</td>
                          <td className="px-3 py-1.5 font-mono text-muted-foreground">{r.remote_port ?? "Any"}</td>
                          <td className="px-3 py-1.5 font-mono text-muted-foreground max-w-[140px] truncate" title={r.program ?? ""}>{r.program ?? "—"}</td>
                          <td className="px-3 py-1.5 text-muted-foreground">{r.profile ?? "—"}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </CardContent>
          )}
        </Card>
      )}

      {!fw && <EmptyMsg />}
    </div>
  );
}

// ── Tab: Remote Access ────────────────────────────────────────────────────────

function RemoteAccessTab({ payload }: { payload: CollectionPayload }) {
  const ra = payload.remote_access;
  const rdp = payload.security_policy?.rdp;
  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">RDP (Remote Desktop)</CardTitle>
        </CardHeader>
        <CardContent>
          {!rdp ? <EmptyMsg /> : (
            <>
              <InfoRow label="Bật" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={rdp.enabled} />{rdp.enabled ? "Có" : "Không"}</span>} />
              <InfoRow label="NLA bắt buộc" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={rdp.nla_required} />{rdp.nla_required ? "Có" : "Không"}</span>} />
              <InfoRow label="Port" value={<span className="font-mono">{rdp.port}</span>} />
              <InfoRow label="Session timeout (phút)" value={rdp.session_timeout_min === 0 ? "Không giới hạn" : String(rdp.session_timeout_min)} />
              <InfoRow label="Disconnect timeout (phút)" value={rdp.disconnect_timeout_min === 0 ? "Không giới hạn" : String(rdp.disconnect_timeout_min)} />
            </>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">WinRM (Windows Remote Management)</CardTitle>
        </CardHeader>
        <CardContent>
          {!ra?.winrm ? <EmptyMsg /> : (
            <>
              <InfoRow label="Trạng thái dịch vụ" value={ra.winrm.service_status} />
              <InfoRow label="Cho phép Basic Auth" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={ra.winrm.allow_basic_auth} invert />{ra.winrm.allow_basic_auth ? <span className="text-destructive">Có (nguy hiểm)</span> : "Không"}</span>} />
              <InfoRow label="Cho phép không mã hóa" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={ra.winrm.allow_unencrypted} invert />{ra.winrm.allow_unencrypted ? <span className="text-destructive">Có (nguy hiểm)</span> : "Không"}</span>} />
              <InfoRow label="Remote Shell" value={ra.winrm.allow_remote_shell_access ? "Bật" : "Tắt"} />
            </>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">OpenSSH</CardTitle>
        </CardHeader>
        <CardContent>
          {!ra?.openssh ? <EmptyMsg /> : (
            <>
              <InfoRow label="Đã cài đặt" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={ra.openssh.installed} />{ra.openssh.installed ? "Có" : "Không"}</span>} />
              <InfoRow label="Trạng thái dịch vụ" value={ra.openssh.service_status} />
              <InfoRow label="Default shell" value={ra.openssh.default_shell} mono />
            </>
          )}
        </CardContent>
      </Card>
      {ra != null && (
        <Card>
          <CardContent className="pt-4">
            <InfoRow label="Telnet Server" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={ra.telnet_server} invert />{ra.telnet_server ? <span className="text-destructive">Bật (nguy hiểm)</span> : "Tắt"}</span>} />
          </CardContent>
        </Card>
      )}
    </div>
  );
}

// ── Tab: Accounts ─────────────────────────────────────────────────────────────

function AccountsTab({ payload }: { payload: CollectionPayload }) {
  const la = payload.local_accounts;
  const admins = la?.administrators ?? [];
  const accounts = la?.accounts ?? [];
  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base flex items-center gap-2">
            <Users className="h-4 w-4" />Administrators ({admins.length})
          </CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {admins.length === 0 ? (
            <div className="px-4 pb-4 pt-2"><EmptyMsg /></div>
          ) : (
            <table className="w-full text-sm">
              <thead><tr className="border-b">
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Tên</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Loại</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Domain?</th>
              </tr></thead>
              <tbody>
                {admins.map((a, i) => (
                  <tr key={i} className="border-b last:border-0">
                    <td className="px-4 py-2 font-medium">{a.name ?? "—"}</td>
                    <td className="px-4 py-2 text-muted-foreground">{a.type ?? "—"}</td>
                    <td className="px-4 py-2">{a.is_domain ? <span className="text-amber-600">Domain</span> : "Local"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Tài khoản local ({accounts.length})</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {accounts.length === 0 ? (
            <div className="px-4 pb-4 pt-2"><EmptyMsg msg="Không thu thập được danh sách tài khoản local" /></div>
          ) : (
            <table className="w-full text-sm">
              <thead><tr className="border-b">
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Tên</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Loại</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Bật</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Đăng nhập cuối</th>
              </tr></thead>
              <tbody>
                {accounts.map((a, i) => (
                  <tr key={i} className="border-b last:border-0">
                    <td className="px-4 py-2 font-medium">{a.name ?? "—"}</td>
                    <td className="px-4 py-2 text-muted-foreground">{a.type ?? "—"}</td>
                    <td className="px-4 py-2"><BoolIcon value={a.enabled} /></td>
                    <td className="px-4 py-2 text-muted-foreground text-xs">{a.last_logon ? formatDate(a.last_logon) : "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

// ── Tab: Applications ─────────────────────────────────────────────────────────

function AppsTab({ payload }: { payload: CollectionPayload }) {
  const [search, setSearch] = useState("");
  const apps = payload.applications ?? [];
  const filtered = apps.filter(a => !search || (a.display_name ?? "").toLowerCase().includes(search.toLowerCase()) || (a.publisher ?? "").toLowerCase().includes(search.toLowerCase()));
  const visible = filtered.slice(0, 100);

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-center justify-between gap-4">
          <CardTitle className="text-base flex items-center gap-2">
            <Package className="h-4 w-4" />Phần mềm đã cài ({apps.length})
          </CardTitle>
          <input
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="Tìm theo tên..."
            className="h-8 rounded-md border bg-background px-3 text-sm w-48 focus:outline-none focus:ring-1 focus:ring-ring"
          />
        </div>
      </CardHeader>
      <CardContent className="p-0">
        {apps.length === 0 ? <div className="px-4 pb-4"><EmptyMsg /></div> : (
          <>
            <div className="overflow-auto max-h-[500px]">
              <table className="w-full text-sm">
                <thead><tr className="border-b sticky top-0 bg-card">
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Tên</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Phiên bản</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Nhà cung cấp</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Ngày cài</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Kiến trúc</th>
                </tr></thead>
                <tbody>
                  {visible.map((a, i) => (
                    <tr key={i} className="border-b last:border-0">
                      <td className="px-4 py-2 font-medium">{a.display_name ?? "—"}</td>
                      <td className="px-4 py-2 text-muted-foreground font-mono text-xs">{a.display_version ?? "—"}</td>
                      <td className="px-4 py-2 text-muted-foreground text-xs">{a.publisher ?? "—"}</td>
                      <td className="px-4 py-2 text-muted-foreground text-xs">{a.install_date ?? "—"}</td>
                      <td className="px-4 py-2 text-muted-foreground text-xs">{a.architecture ?? "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {filtered.length > 100 && (
              <div className="px-4 py-2 text-xs text-muted-foreground">Hiển thị 100 / {filtered.length} kết quả</div>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}

// ── Tab: Services ─────────────────────────────────────────────────────────────

function ServicesTab({ payload }: { payload: CollectionPayload }) {
  const [filter, setFilter] = useState("all");
  const [search, setSearch] = useState("");
  const services = payload.services ?? [];

  const filtered = services.filter(s => {
    if (filter === "running" && s.status !== "Running") return false;
    if (filter === "stopped" && s.status !== "Stopped") return false;
    if (filter === "autostart" && s.startup_type !== "Automatic") return false;
    if (search && !(s.display_name ?? "").toLowerCase().includes(search.toLowerCase()) && !(s.name ?? "").toLowerCase().includes(search.toLowerCase())) return false;
    return true;
  });

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-center justify-between gap-4 flex-wrap">
          <CardTitle className="text-base flex items-center gap-2">
            <Settings className="h-4 w-4" />Dịch vụ ({services.length})
          </CardTitle>
          <div className="flex gap-2">
            <input
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Tìm kiếm..."
              className="h-8 rounded-md border bg-background px-3 text-sm w-40 focus:outline-none focus:ring-1 focus:ring-ring"
            />
            <select value={filter} onChange={e => setFilter(e.target.value)}
              className="h-8 rounded-md border bg-background px-2 text-sm focus:outline-none focus:ring-1 focus:ring-ring">
              <option value="all">Tất cả</option>
              <option value="running">Đang chạy</option>
              <option value="stopped">Đã dừng</option>
              <option value="autostart">Tự khởi động</option>
            </select>
          </div>
        </div>
      </CardHeader>
      <CardContent className="p-0">
        {services.length === 0 ? <div className="px-4 pb-4"><EmptyMsg /></div> : (
          <div className="overflow-auto max-h-[500px]">
            <table className="w-full text-sm">
              <thead><tr className="border-b sticky top-0 bg-card">
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Tên</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Trạng thái</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Khởi động</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Tài khoản</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">PID</th>
              </tr></thead>
              <tbody>
                {filtered.slice(0, 200).map((s, i) => (
                  <tr key={i} className="border-b last:border-0">
                    <td className="px-4 py-2">
                      <p className="font-medium">{s.display_name ?? s.name ?? "—"}</p>
                      {s.display_name && <p className="text-xs text-muted-foreground font-mono">{s.name}</p>}
                    </td>
                    <td className="px-4 py-2">
                      <span className={s.status === "Running" ? "text-green-600" : s.status === "Stopped" ? "text-muted-foreground" : ""}>
                        {s.status ?? "—"}
                      </span>
                    </td>
                    <td className="px-4 py-2 text-muted-foreground text-xs">{s.startup_type ?? "—"}</td>
                    <td className="px-4 py-2 text-muted-foreground text-xs">{s.account ?? "—"}</td>
                    <td className="px-4 py-2 text-muted-foreground text-xs">{s.pid ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {filtered.length > 200 && (
              <div className="px-4 py-2 text-xs text-muted-foreground">Hiển thị 200 / {filtered.length} kết quả</div>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

// ── Tab: System (Tasks / Startup / GPO / Shares / Event Logs) ────────────────

function SystemTab({ payload }: { payload: CollectionPayload }) {
  const tasks = payload.scheduled_tasks ?? [];
  const startup = payload.startup_entries ?? [];
  const shares = payload.shared_folders?.shares ?? [];
  const gpo = payload.gpo;
  const evtLogs = payload.event_log_settings;

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Scheduled Tasks ({tasks.length})</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {tasks.length === 0 ? <div className="px-4 pb-4 pt-2"><EmptyMsg /></div> : (
            <div className="overflow-auto max-h-64">
              <table className="w-full text-sm">
                <thead><tr className="border-b sticky top-0 bg-card">
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Tên</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Trạng thái</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Chạy với</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Lần chạy cuối</th>
                  <th className="px-4 py-2 text-left font-medium text-muted-foreground">Kết quả</th>
                </tr></thead>
                <tbody>
                  {tasks.map((t, i) => (
                    <tr key={i} className="border-b last:border-0">
                      <td className="px-4 py-2">
                        <p className="font-medium text-xs">{t.task_name ?? "—"}</p>
                        {t.task_path && t.task_path !== "\\" && <p className="text-xs text-muted-foreground font-mono">{t.task_path}</p>}
                      </td>
                      <td className="px-4 py-2 text-xs">{t.state ?? "—"}</td>
                      <td className="px-4 py-2 text-xs text-muted-foreground">{t.run_as_user ?? "—"}</td>
                      <td className="px-4 py-2 text-xs text-muted-foreground">{t.last_run_time ? formatDate(t.last_run_time) : "—"}</td>
                      <td className="px-4 py-2 text-xs font-mono">
                        <span className={t.last_run_result === 0 ? "text-green-600" : t.last_run_result != null ? "text-destructive" : ""}>
                          {t.last_run_result != null ? `0x${t.last_run_result.toString(16).toUpperCase()}` : "—"}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Startup Entries ({startup.length})</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {startup.length === 0 ? <div className="px-4 pb-4 pt-2"><EmptyMsg /></div> : (
            <table className="w-full text-sm">
              <thead><tr className="border-b">
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Tên</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Lệnh</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Vị trí</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Bật</th>
              </tr></thead>
              <tbody>
                {startup.map((s, i) => (
                  <tr key={i} className="border-b last:border-0">
                    <td className="px-4 py-2 font-medium text-xs">{s.name ?? "—"}</td>
                    <td className="px-4 py-2 font-mono text-xs text-muted-foreground max-w-[300px] truncate" title={s.command}>{s.command ?? "—"}</td>
                    <td className="px-4 py-2 font-mono text-xs text-muted-foreground">{s.location ?? "—"}</td>
                    <td className="px-4 py-2"><BoolIcon value={s.enabled} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Thư mục chia sẻ ({shares.length})</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {shares.length === 0 ? <div className="px-4 pb-4 pt-2"><EmptyMsg /></div> : (
            <table className="w-full text-sm">
              <thead><tr className="border-b">
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Tên chia sẻ</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Đường dẫn</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Mô tả</th>
              </tr></thead>
              <tbody>
                {shares.map((s, i) => (
                  <tr key={i} className="border-b last:border-0">
                    <td className="px-4 py-2 font-mono font-medium">{s.name ?? "—"}</td>
                    <td className="px-4 py-2 font-mono text-xs text-muted-foreground">{s.path || "—"}</td>
                    <td className="px-4 py-2 text-xs text-muted-foreground">{s.description || "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">GPO</CardTitle>
        </CardHeader>
        <CardContent>
          {!gpo ? <EmptyMsg /> : (
            <>
              <InfoRow label="Trạng thái làm mới" value={gpo.refresh_status} />
              <InfoRow label="Làm mới lần cuối" value={gpo.last_refresh ? formatDate(gpo.last_refresh) : undefined} />
              <InfoRow label="GPO máy tính" value={String(gpo.computer_gpos?.length ?? 0)} />
              <InfoRow label="GPO người dùng" value={String(gpo.user_gpos?.length ?? 0)} />
            </>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Event Log Settings</CardTitle>
        </CardHeader>
        <CardContent>
          {!evtLogs ? <EmptyMsg /> : (
            <>
              <InfoRow label="Event forwarding" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={evtLogs.event_forwarding_enabled} />{evtLogs.event_forwarding_enabled ? "Bật" : "Tắt"}</span>} />
              {(evtLogs.logs ?? []).length > 0 && (
                <div className="mt-3">
                  <table className="w-full text-sm">
                    <thead><tr className="border-b">
                      <th className="py-2 text-left font-medium text-muted-foreground">Log</th>
                      <th className="py-2 text-left font-medium text-muted-foreground">Max Size</th>
                      <th className="py-2 text-left font-medium text-muted-foreground">Bật</th>
                    </tr></thead>
                    <tbody>
                      {evtLogs.logs!.map((l, i) => (
                        <tr key={i} className="border-b last:border-0">
                          <td className="py-2 font-mono text-xs">{l.log_name ?? "—"}</td>
                          <td className="py-2 text-xs text-muted-foreground">{l.max_size_kb ? `${l.max_size_kb} KB` : "—"}</td>
                          <td className="py-2"><BoolIcon value={l.enabled} /></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

// ── Tab: Registry ─────────────────────────────────────────────────────────────

function RegistryTab({ payload }: { payload: CollectionPayload }) {
  const ra = payload.registry_audit;
  const flags = ra?.dangerous_flags ?? [];
  return (
    <div className="space-y-4">
      {flags.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 text-destructive" />
              Cờ nguy hiểm ({flags.length})
            </CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <table className="w-full text-sm">
              <thead><tr className="border-b">
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Tên</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Mức độ</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Giá trị thực tế</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Mong đợi</th>
              </tr></thead>
              <tbody>
                {flags.map((f, i) => (
                  <tr key={i} className="border-b last:border-0">
                    <td className="px-4 py-2">
                      <p className="font-medium">{f.name}</p>
                      <p className="text-xs text-muted-foreground font-mono">{f.registry_path}\\{f.value_name}</p>
                      {f.description && <p className="text-xs text-muted-foreground mt-0.5">{f.description}</p>}
                    </td>
                    <td className="px-4 py-2">
                      <Badge variant={(severityVariant[f.severity] ?? "secondary") as "critical" | "high" | "medium" | "low"}>{f.severity}</Badge>
                    </td>
                    <td className="px-4 py-2 font-mono text-xs text-destructive">{f.actual_value ?? "—"}</td>
                    <td className="px-4 py-2 font-mono text-xs text-green-600">{f.expected_value ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader className="pb-3"><CardTitle className="text-base">LSA Settings</CardTitle></CardHeader>
        <CardContent>
          {!ra?.lsa ? <EmptyMsg /> : (
            <>
              {Object.entries(ra.lsa).map(([k, v]) => (
                <InfoRow key={k} label={k} value={typeof v === "boolean"
                  ? <span className="flex items-center gap-1 justify-end"><BoolIcon value={v} />{v ? "true" : "false"}</span>
                  : String(v ?? "—")} />
              ))}
            </>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-3"><CardTitle className="text-base">SMB</CardTitle></CardHeader>
        <CardContent>
          {!ra?.smb ? <EmptyMsg /> : (
            <>
              <InfoRow label="SMBv1 bật" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={ra.smb.smb1_enabled} invert />{ra.smb.smb1_enabled ? <span className="text-destructive">Có (nguy hiểm)</span> : "Không"}</span>} />
              <InfoRow label="Server signing" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={ra.smb.server_signing_enabled} />{ra.smb.server_signing_enabled ? "Bật" : "Tắt"}</span>} />
              <InfoRow label="Client signing bắt buộc" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={ra.smb.client_signing_required} />{ra.smb.client_signing_required ? "Có" : "Không"}</span>} />
            </>
          )}
        </CardContent>
      </Card>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <Card>
          <CardHeader className="pb-3"><CardTitle className="text-base">PowerShell Policy</CardTitle></CardHeader>
          <CardContent>
            {!ra?.powershell_policy ? <EmptyMsg /> : (
              <>
                <InfoRow label="Execution policy" value={ra.powershell_policy.execution_policy} />
                <InfoRow label="Script block logging" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={ra.powershell_policy.script_block_logging} />{ra.powershell_policy.script_block_logging ? "Bật" : "Tắt"}</span>} />
                <InfoRow label="Transcription" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={ra.powershell_policy.transcription} />{ra.powershell_policy.transcription ? "Bật" : "Tắt"}</span>} />
              </>
            )}
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-3"><CardTitle className="text-base">WDigest & Credential Guard</CardTitle></CardHeader>
          <CardContent>
            <InfoRow label="WDigest plaintext" value={
              <span className="flex items-center gap-1 justify-end">
                <BoolIcon value={ra?.wdigest?.use_logon_credential} invert />
                {ra?.wdigest?.use_logon_credential ? <span className="text-destructive">Bật (nguy hiểm)</span> : "Tắt"}
              </span>
            } />
            <InfoRow label="VBS enabled" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={ra?.credential_guard?.vbs_enabled} />{ra?.credential_guard?.vbs_enabled ? "Có" : "Không"}</span>} />
            <InfoRow label="LSA CFG flags" value={ra?.credential_guard?.lsa_cfg_flags != null ? String(ra.credential_guard.lsa_cfg_flags) : undefined} />
          </CardContent>
        </Card>
      </div>

      {flags.length === 0 && !ra && <EmptyMsg />}
    </div>
  );
}

// ── Tab: Diff ─────────────────────────────────────────────────────────────────

function DiffTab({ hostId }: { hostId: string }) {
  const [diffPeriod, setDiffPeriod] = useState("24");

  const diffFrom = (() => {
    const now = new Date();
    now.setHours(now.getHours() - parseInt(diffPeriod));
    return now.toISOString();
  })();

  const { data: diff, isLoading } = useQuery<DiffResult>({
    queryKey: ["host-diff", hostId, diffPeriod],
    queryFn: () => hostsApi.getDiff(hostId, diffFrom).then(r => r.data),
  });

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-center justify-between">
          <CardTitle className="text-base flex items-center gap-2">
            <GitCompare className="h-4 w-4" />Thay đổi cấu hình
          </CardTitle>
          <Select value={diffPeriod} onValueChange={setDiffPeriod}>
            <SelectTrigger className="h-8 w-28"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="1">1 giờ</SelectItem>
              <SelectItem value="6">6 giờ</SelectItem>
              <SelectItem value="24">24 giờ</SelectItem>
              <SelectItem value="72">3 ngày</SelectItem>
              <SelectItem value="168">7 ngày</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </CardHeader>
      <CardContent className="p-0">
        {isLoading ? (
          <div className="px-4 py-6 text-sm text-muted-foreground text-center">Đang tải...</div>
        ) : !diff || diff.changes.length === 0 ? (
          <div className="px-4 py-6 text-sm text-muted-foreground text-center">Không có thay đổi</div>
        ) : (
          <div className="divide-y">
            {diff.changes.map((c, i) => (
              <div key={i} className="px-4 py-3">
                <p className="text-xs font-mono font-medium text-foreground mb-1">{c.fieldPath}</p>
                <div className="grid grid-cols-2 gap-2 text-xs">
                  <div className="rounded bg-destructive/10 px-2 py-1 text-destructive break-all">
                    <span className="font-medium">Trước: </span>{c.oldValue ?? "—"}
                  </div>
                  <div className="rounded bg-green-500/10 px-2 py-1 text-green-700 dark:text-green-400 break-all">
                    <span className="font-medium">Sau: </span>{c.newValue ?? "—"}
                  </div>
                </div>
                <p className="text-xs text-muted-foreground mt-1">{formatDate(c.changedAt)}</p>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

// ── Tab: Patch ────────────────────────────────────────────────────────────────

function PatchTab({ payload }: { payload: CollectionPayload }) {
  const p = payload.patch;
  const hotfixes = p?.hotfixes ?? [];
  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base flex items-center gap-2">
            <FileText className="h-4 w-4" />Cập nhật Windows
          </CardTitle>
        </CardHeader>
        <CardContent>
          {!p ? <EmptyMsg /> : (
            <>
              <InfoRow label="Số hotfix đã cài" value={String(p.hotfix_count ?? 0)} />
              <InfoRow label="WSUS Server" value={p.wsus_server || "Không cấu hình"} />
              <InfoRow label="Tắt tự cập nhật" value={<span className="flex items-center gap-1 justify-end"><BoolIcon value={p.no_auto_update} invert />{p.no_auto_update ? <span className="text-destructive">Tắt tự cập nhật</span> : "Tự cập nhật"}</span>} />
              <InfoRow label="Tuỳ chọn cập nhật" value={String(p.auto_update_options ?? "—")} />
              <InfoRow label="Phát hiện cập nhật cuối" value={p.last_success_detect ? formatDate(p.last_success_detect) : undefined} />
              <InfoRow label="Cài đặt cập nhật cuối" value={p.last_success_install ? formatDate(p.last_success_install) : undefined} />
            </>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Hotfixes ({hotfixes.length})</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {hotfixes.length === 0 ? <div className="px-4 pb-4 pt-2"><EmptyMsg /></div> : (
            <table className="w-full text-sm">
              <thead><tr className="border-b">
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">KB</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Mô tả</th>
                <th className="px-4 py-2 text-left font-medium text-muted-foreground">Ngày cài</th>
              </tr></thead>
              <tbody>
                {hotfixes.map((h, i) => (
                  <tr key={i} className="border-b last:border-0">
                    <td className="px-4 py-2 font-mono font-medium">{h.hotfix_id}</td>
                    <td className="px-4 py-2 text-muted-foreground">{h.description ?? "—"}</td>
                    <td className="px-4 py-2 text-muted-foreground">{h.installed_on ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function HostDetailPage() {
  // Next.js param key is "hostname" (folder name) but the value is a UUID host_id
  const { hostname: hostId } = useParams<{ hostname: string }>();
  const router = useRouter();

  const { data: payload, isLoading, refetch } = useQuery<CollectionPayload>({
    queryKey: ["host-latest", hostId],
    queryFn: () => hostsApi.getLatest(hostId).then(r => r.data),
  });

  const hostnameFromPayload = payload?.host?.hostname;

  const { data: violationsData } = useQuery({
    queryKey: ["host-violations", hostId],
    queryFn: () => violationsApi.list({ hostname: hostnameFromPayload, resolved: false, size: 50 }).then(r => r.data),
    enabled: !!hostnameFromPayload,
  });

  const violations: Violation[] = violationsData?.items ?? [];
  const registryFlags = payload?.registry_audit?.dangerous_flags?.length ?? 0;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => router.back()}>
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div>
            <h1 className="text-2xl font-bold tracking-tight font-mono">{hostnameFromPayload ?? hostId}</h1>
            <p className="text-muted-foreground text-sm">
              {payload?.host?.domain && <span>{payload.host.domain} · </span>}
              {payload?.host?.os_version && <span>{payload.host.os_version}</span>}
            </p>
          </div>
        </div>
        <Button variant="outline" size="sm" onClick={() => refetch()}>
          <RefreshCw className="mr-2 h-4 w-4" />Làm mới
        </Button>
      </div>

      {isLoading ? (
        <div className="text-center text-muted-foreground py-12">Đang tải...</div>
      ) : !payload ? (
        <div className="text-center text-muted-foreground py-12">
          <Monitor className="mx-auto h-10 w-10 text-muted-foreground/40 mb-3" />
          Không tìm thấy dữ liệu cho máy này
        </div>
      ) : (
        <>
          {/* Info cards */}
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <Card>
              <CardContent className="pt-4 pb-3">
                <p className="text-xs text-muted-foreground uppercase tracking-wide">Lần cuối ghi nhận</p>
                <p className="text-sm font-semibold mt-1">{timeAgo(payload.collected_at)}</p>
                <p className="text-xs text-muted-foreground">{formatDate(payload.collected_at)}</p>
              </CardContent>
            </Card>
            <Card>
              <CardContent className="pt-4 pb-3">
                <p className="text-xs text-muted-foreground uppercase tracking-wide">Agent version</p>
                <p className="text-sm font-semibold mt-1 font-mono">{payload.agent_version ?? "—"}</p>
              </CardContent>
            </Card>
            <Card>
              <CardContent className="pt-4 pb-3">
                <p className="text-xs text-muted-foreground uppercase tracking-wide">Vi phạm mở</p>
                <p className={`text-2xl font-bold mt-1 ${violations.length > 0 ? "text-destructive" : "text-green-600"}`}>{violations.length}</p>
              </CardContent>
            </Card>
            <Card>
              <CardContent className="pt-4 pb-3">
                <p className="text-xs text-muted-foreground uppercase tracking-wide">Registry cờ nguy hiểm</p>
                <p className={`text-2xl font-bold mt-1 ${registryFlags > 0 ? "text-amber-600" : "text-green-600"}`}>{registryFlags}</p>
              </CardContent>
            </Card>
          </div>

          {/* Tabs */}
          <Tabs defaultValue="overview">
            <TabsList className="flex-wrap h-auto gap-1">
              <TabsTrigger value="overview">Tổng quan</TabsTrigger>
              <TabsTrigger value="antivirus">
                <span className="flex items-center gap-1">
                  Antivirus
                  {payload.defender?.antivirus_enabled === false && <XCircle className="h-3 w-3 text-destructive" />}
                </span>
              </TabsTrigger>
              <TabsTrigger value="hardware">Phần cứng</TabsTrigger>
              <TabsTrigger value="policy">Chính sách</TabsTrigger>
              <TabsTrigger value="firewall">Tường lửa</TabsTrigger>
              <TabsTrigger value="remote">Truy cập từ xa</TabsTrigger>
              <TabsTrigger value="accounts">Tài khoản</TabsTrigger>
              <TabsTrigger value="apps">Phần mềm</TabsTrigger>
              <TabsTrigger value="services">Dịch vụ</TabsTrigger>
              <TabsTrigger value="system">Hệ thống</TabsTrigger>
              <TabsTrigger value="registry">
                <span className="flex items-center gap-1">
                  Registry
                  {registryFlags > 0 && <span className="ml-1 rounded-full bg-amber-500/20 text-amber-600 text-xs px-1.5">{registryFlags}</span>}
                </span>
              </TabsTrigger>
              <TabsTrigger value="patch">Bản vá</TabsTrigger>
              <TabsTrigger value="diff">Thay đổi</TabsTrigger>
            </TabsList>

            <TabsContent value="overview"><OverviewTab payload={payload} violations={violations} /></TabsContent>
            <TabsContent value="antivirus"><AntivirusTab payload={payload} /></TabsContent>
            <TabsContent value="hardware"><HardwareTab payload={payload} /></TabsContent>
            <TabsContent value="policy"><PolicyTab payload={payload} /></TabsContent>
            <TabsContent value="firewall"><FirewallTab payload={payload} /></TabsContent>
            <TabsContent value="remote"><RemoteAccessTab payload={payload} /></TabsContent>
            <TabsContent value="accounts"><AccountsTab payload={payload} /></TabsContent>
            <TabsContent value="apps"><AppsTab payload={payload} /></TabsContent>
            <TabsContent value="services"><ServicesTab payload={payload} /></TabsContent>
            <TabsContent value="system"><SystemTab payload={payload} /></TabsContent>
            <TabsContent value="registry"><RegistryTab payload={payload} /></TabsContent>
            <TabsContent value="patch"><PatchTab payload={payload} /></TabsContent>
            <TabsContent value="diff"><DiffTab hostId={hostId} /></TabsContent>
          </Tabs>
        </>
      )}
    </div>
  );
}
