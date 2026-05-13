"use client";

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { FileText, AlertTriangle, CheckCircle2 } from "lucide-react";
import { patchesApi } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { formatDate } from "@/lib/utils";

interface PatchItem {
  hostname: string;
  collected_at: string;
  hotfix_count: number | null;
  wsus_server: string | null;
  no_auto_update: boolean | null;
  auto_update_options: number | null;
  last_success_install: string | null;
  last_success_detect: string | null;
}

interface PatchesResponse {
  items: PatchItem[];
}

function daysSince(dateStr: string | null): number | null {
  if (!dateStr) return null;
  const d = new Date(dateStr);
  if (isNaN(d.getTime())) return null;
  return Math.floor((Date.now() - d.getTime()) / 86_400_000);
}

function patchStatus(item: PatchItem): { label: string; variant: "success" | "high" | "medium" | "secondary" } {
  if (item.no_auto_update) return { label: "Tắt cập nhật", variant: "high" };
  const days = daysSince(item.last_success_install);
  if (days == null) return { label: "Chưa cập nhật", variant: "medium" };
  if (days > 90) return { label: `${days} ngày`, variant: "high" };
  if (days > 30) return { label: `${days} ngày`, variant: "medium" };
  return { label: `${days} ngày`, variant: "success" };
}

export default function PatchesPage() {
  const router = useRouter();
  const { data, isLoading } = useQuery<PatchesResponse>({
    queryKey: ["patches"],
    queryFn: () => patchesApi.list().then(r => r.data),
    refetchInterval: 300_000,
  });

  if (isLoading) return <div className="text-center text-muted-foreground py-12">Đang tải...</div>;
  if (!data) return null;

  const items = data.items ?? [];
  const noUpdateCount = items.filter(i => i.no_auto_update).length;
  const outdated90 = items.filter(i => (daysSince(i.last_success_install) ?? 0) > 90).length;
  const outdated30 = items.filter(i => {
    const d = daysSince(i.last_success_install) ?? 0;
    return d > 30 && d <= 90;
  }).length;
  const wsusConfigured = items.filter(i => i.wsus_server).length;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Tuân thủ bản vá</h1>
        <p className="text-muted-foreground text-sm">Trạng thái cập nhật Windows và hotfix trên toàn fleet</p>
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <Card>
          <CardContent className="pt-4 pb-3">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Tổng máy tính</p>
            <p className="text-2xl font-bold mt-1">{items.length}</p>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="pt-4 pb-3">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Chưa cập nhật 90+ ngày</p>
            <p className={`text-2xl font-bold mt-1 ${outdated90 > 0 ? "text-destructive" : "text-green-600"}`}>{outdated90}</p>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="pt-4 pb-3">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Tắt tự cập nhật</p>
            <p className={`text-2xl font-bold mt-1 ${noUpdateCount > 0 ? "text-destructive" : "text-green-600"}`}>{noUpdateCount}</p>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="pt-4 pb-3">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Có cấu hình WSUS</p>
            <p className="text-2xl font-bold mt-1">{wsusConfigured}</p>
          </CardContent>
        </Card>
      </div>

      {/* Patch table */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base flex items-center gap-2">
            <FileText className="h-4 w-4" />
            Chi tiết bản vá
          </CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm min-w-[700px]">
              <thead>
                <tr className="border-b">
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">Máy tính</th>
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">Trạng thái</th>
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">Số hotfix</th>
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">Cài đặt cuối</th>
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">Phát hiện cuối</th>
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">WSUS</th>
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">Thu thập lúc</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => {
                  const status = patchStatus(item);
                  const days = daysSince(item.last_success_install);
                  return (
                    <tr
                      key={item.hostname}
                      className="border-b last:border-0 hover:bg-muted/50 cursor-pointer transition-colors"
                      onClick={() => router.push(`/hosts/${encodeURIComponent(item.hostname)}?tab=patch`)}
                    >
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-2">
                          {(item.no_auto_update || (days ?? 0) > 90) && (
                            <AlertTriangle className="h-3.5 w-3.5 text-amber-500 shrink-0" />
                          )}
                          <span className="font-mono font-medium">{item.hostname}</span>
                        </div>
                      </td>
                      <td className="px-4 py-3">
                        <Badge variant={status.variant}>{status.label}</Badge>
                      </td>
                      <td className="px-4 py-3 font-mono">{item.hotfix_count ?? "—"}</td>
                      <td className="px-4 py-3 text-muted-foreground text-xs">
                        {item.last_success_install
                          ? <span title={formatDate(item.last_success_install)}>{formatDate(item.last_success_install)}</span>
                          : <span className="text-destructive">Chưa từng</span>}
                      </td>
                      <td className="px-4 py-3 text-muted-foreground text-xs">
                        {item.last_success_detect ? formatDate(item.last_success_detect) : "—"}
                      </td>
                      <td className="px-4 py-3 text-xs">
                        {item.wsus_server
                          ? <span className="font-mono text-muted-foreground">{item.wsus_server}</span>
                          : <span className="text-muted-foreground">—</span>}
                      </td>
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

      {(outdated30 > 0 || outdated90 > 0 || noUpdateCount > 0) && (
        <Card className="border-amber-500/30 bg-amber-500/5">
          <CardContent className="pt-4 pb-3">
            <div className="flex items-start gap-3">
              <AlertTriangle className="h-5 w-5 text-amber-500 shrink-0 mt-0.5" />
              <div className="text-sm">
                <p className="font-medium">Khuyến nghị bảo mật</p>
                <ul className="mt-1 space-y-1 text-muted-foreground">
                  {noUpdateCount > 0 && <li>• {noUpdateCount} máy đã tắt Windows Update — cần bật lại hoặc cấu hình WSUS</li>}
                  {outdated90 > 0 && <li>• {outdated90} máy chưa cập nhật hơn 90 ngày — cần cập nhật khẩn cấp</li>}
                  {outdated30 > 0 && <li>• {outdated30} máy chưa cập nhật 30-90 ngày — nên lên lịch cập nhật</li>}
                  {wsusConfigured < items.length && <li>• {items.length - wsusConfigured} máy chưa cấu hình WSUS — khó kiểm soát tập trung</li>}
                </ul>
              </div>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
