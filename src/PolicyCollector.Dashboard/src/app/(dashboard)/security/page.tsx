"use client";

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Shield, CheckCircle2, XCircle, MinusCircle, AlertTriangle } from "lucide-react";
import { securityApi } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { formatDate } from "@/lib/utils";

interface SecurityItem {
  hostname: string;
  collected_at: string;
  defender_enabled: boolean | null;
  real_time_protection: boolean | null;
  cloud_protection: boolean | null;
  signature_version: string | null;
  bitlocker_volume_count: number;
  tpm_present: boolean | null;
  tpm_enabled: boolean | null;
  secure_boot_enabled: boolean | null;
  uefi_mode: boolean | null;
  laps_configured: boolean | null;
}

interface SecurityOverview {
  total_hosts: number;
  defender_disabled: number;
  rtprotection_off: number;
  tpm_missing: number;
  secure_boot_off: number;
  laps_unconfigured: number;
  items: SecurityItem[];
}

function StatusIcon({ value }: { value: boolean | null }) {
  if (value === true) return <CheckCircle2 className="h-4 w-4 text-green-600" />;
  if (value === false) return <XCircle className="h-4 w-4 text-destructive" />;
  return <MinusCircle className="h-4 w-4 text-muted-foreground" />;
}

function SummaryCard({ label, value, total, icon: Icon, bad }: {
  label: string;
  value: number;
  total: number;
  icon: React.ElementType;
  bad?: boolean;
}) {
  return (
    <Card>
      <CardContent className="pt-4 pb-3">
        <div className="flex items-start justify-between">
          <div>
            <p className="text-xs text-muted-foreground uppercase tracking-wide">{label}</p>
            <p className={`text-2xl font-bold mt-1 ${bad && value > 0 ? "text-destructive" : "text-foreground"}`}>
              {value}
              <span className="text-sm font-normal text-muted-foreground ml-1">/ {total}</span>
            </p>
          </div>
          <Icon className={`h-5 w-5 mt-1 ${bad && value > 0 ? "text-destructive" : "text-muted-foreground"}`} />
        </div>
      </CardContent>
    </Card>
  );
}

export default function SecurityPage() {
  const router = useRouter();
  const { data, isLoading } = useQuery<SecurityOverview>({
    queryKey: ["security-overview"],
    queryFn: () => securityApi.overview().then(r => r.data),
    refetchInterval: 300_000,
  });

  if (isLoading) return <div className="text-center text-muted-foreground py-12">Đang tải...</div>;
  if (!data) return null;

  const items = data.items ?? [];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Tổng quan bảo mật</h1>
        <p className="text-muted-foreground text-sm">Trạng thái bảo mật toàn bộ máy tính trong fleet</p>
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
        <SummaryCard label="Tổng máy tính" value={data.total_hosts} total={data.total_hosts} icon={Shield} />
        <SummaryCard label="Defender tắt" value={data.defender_disabled} total={data.total_hosts} icon={XCircle} bad />
        <SummaryCard label="Real-time tắt" value={data.rtprotection_off} total={data.total_hosts} icon={AlertTriangle} bad />
        <SummaryCard label="TPM thiếu" value={data.tpm_missing} total={data.total_hosts} icon={XCircle} bad />
        <SummaryCard label="Secure Boot tắt" value={data.secure_boot_off} total={data.total_hosts} icon={XCircle} bad />
        <SummaryCard label="LAPS chưa cấu hình" value={data.laps_unconfigured} total={data.total_hosts} icon={XCircle} bad />
      </div>

      {/* Per-host table */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Chi tiết từng máy</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm min-w-[900px]">
              <thead>
                <tr className="border-b">
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">Máy tính</th>
                  <th className="px-4 py-3 text-center font-medium text-muted-foreground">Defender</th>
                  <th className="px-4 py-3 text-center font-medium text-muted-foreground">Real-time</th>
                  <th className="px-4 py-3 text-center font-medium text-muted-foreground">Cloud</th>
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">Signature</th>
                  <th className="px-4 py-3 text-center font-medium text-muted-foreground">BitLocker</th>
                  <th className="px-4 py-3 text-center font-medium text-muted-foreground">TPM</th>
                  <th className="px-4 py-3 text-center font-medium text-muted-foreground">Secure Boot</th>
                  <th className="px-4 py-3 text-center font-medium text-muted-foreground">UEFI</th>
                  <th className="px-4 py-3 text-center font-medium text-muted-foreground">LAPS</th>
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">Cập nhật</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => {
                  const hasIssue =
                    item.defender_enabled === false ||
                    item.real_time_protection === false ||
                    item.tpm_present === false ||
                    item.secure_boot_enabled === false ||
                    item.laps_configured === false;

                  return (
                    <tr
                      key={item.hostname}
                      className="border-b last:border-0 hover:bg-muted/50 cursor-pointer transition-colors"
                      onClick={() => router.push(`/hosts/${encodeURIComponent(item.hostname)}`)}
                    >
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-2">
                          {hasIssue && <AlertTriangle className="h-3.5 w-3.5 text-amber-500 shrink-0" />}
                          <span className="font-mono font-medium">{item.hostname}</span>
                        </div>
                      </td>
                      <td className="px-4 py-3 text-center"><StatusIcon value={item.defender_enabled} /></td>
                      <td className="px-4 py-3 text-center"><StatusIcon value={item.real_time_protection} /></td>
                      <td className="px-4 py-3 text-center"><StatusIcon value={item.cloud_protection} /></td>
                      <td className="px-4 py-3 font-mono text-xs text-muted-foreground">{item.signature_version ?? "—"}</td>
                      <td className="px-4 py-3 text-center">
                        {item.bitlocker_volume_count > 0
                          ? <Badge variant="success">{item.bitlocker_volume_count} vol</Badge>
                          : <span className="text-muted-foreground text-xs">—</span>}
                      </td>
                      <td className="px-4 py-3 text-center"><StatusIcon value={item.tpm_present} /></td>
                      <td className="px-4 py-3 text-center"><StatusIcon value={item.secure_boot_enabled} /></td>
                      <td className="px-4 py-3 text-center">
                        {item.uefi_mode == null
                          ? <MinusCircle className="h-4 w-4 text-muted-foreground mx-auto" />
                          : item.uefi_mode
                            ? <Badge variant="outline" className="text-xs">UEFI</Badge>
                            : <Badge variant="secondary" className="text-xs">Legacy</Badge>}
                      </td>
                      <td className="px-4 py-3 text-center"><StatusIcon value={item.laps_configured} /></td>
                      <td className="px-4 py-3 text-xs text-muted-foreground">{formatDate(item.collected_at)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          {items.length === 0 && (
            <div className="px-4 py-8 text-center text-muted-foreground text-sm">Chưa có dữ liệu</div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
