"use client";

import { useQuery } from "@tanstack/react-query";
import { Monitor, AlertTriangle, Activity, Clock, TrendingUp, ShieldAlert } from "lucide-react";
import { statsApi, violationsApi, hostsApi } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import type { AdminStats, HostSummary, Violation } from "@/types/api";
import { formatDate } from "@/lib/utils";

function StatCard({ title, value, icon: Icon, sub, color }: {
  title: string; value: number | string; icon: React.ElementType;
  sub?: string; color?: string;
}) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle className="text-sm font-medium">{title}</CardTitle>
        <Icon className={`h-4 w-4 ${color ?? "text-muted-foreground"}`} />
      </CardHeader>
      <CardContent>
        <div className="text-2xl font-bold">{value}</div>
        {sub && <p className="text-xs text-muted-foreground mt-1">{sub}</p>}
      </CardContent>
    </Card>
  );
}

const statusColor: Record<string, string> = {
  online: "success", offline: "destructive", stale: "warning", unknown: "secondary",
};
const statusLabel: Record<string, string> = {
  online: "Online", offline: "Offline", stale: "Không hoạt động", unknown: "Chưa rõ",
};
const severityVariant: Record<string, "critical" | "high" | "medium" | "low"> = {
  critical: "critical", high: "high", medium: "medium", low: "low",
};

export default function DashboardPage() {
  const { data: stats } = useQuery<AdminStats>({
    queryKey: ["stats"],
    queryFn: () => statsApi.get().then((r) => r.data),
    refetchInterval: 30_000,
  });

  const { data: hosts } = useQuery({
    queryKey: ["hosts-recent"],
    queryFn: () => hostsApi.list({ size: 6, sort: "last_seen", order: "desc" }).then((r) => r.data),
  });

  const { data: violations } = useQuery({
    queryKey: ["violations-recent"],
    queryFn: () => violationsApi.list({ size: 8, resolved: false }).then((r) => r.data),
  });

  const recentHosts: HostSummary[] = hosts?.items ?? [];
  const recentViolations: Violation[] = violations?.items ?? [];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Tổng quan hệ thống</h1>
        <p className="text-muted-foreground">Giám sát trạng thái bảo mật toàn bộ thiết bị</p>
      </div>

      {/* Stats */}
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        <StatCard title="Tổng số máy" value={stats?.total_hosts ?? "—"} icon={Monitor} sub={`${stats?.online_hosts ?? 0} online, ${stats?.offline_hosts ?? 0} offline`} />
        <StatCard title="Vi phạm chưa xử lý" value={stats?.open_violations ?? "—"} icon={AlertTriangle} sub={`${stats?.critical_violations ?? 0} critical`} color="text-destructive" />
        <StatCard title="Critical hiện tại" value={stats?.critical_violations ?? "—"} icon={ShieldAlert} color="text-destructive" />
        <StatCard title="Máy online" value={stats?.online_hosts ?? "—"} icon={Activity} color="text-green-500" />
        <StatCard title="Máy offline" value={stats?.offline_hosts ?? "—"} icon={Clock} color="text-orange-500" />
        <StatCard title="Ingest (1 giờ qua)" value={stats?.ingestions_last_hour ?? "—"} icon={TrendingUp} sub="snapshots nhận được" />
      </div>

      {/* Recent */}
      <div className="grid gap-6 lg:grid-cols-2">
        {/* Hosts */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Máy tính gần đây</CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <div className="divide-y">
              {recentHosts.length === 0 && (
                <p className="px-6 py-4 text-sm text-muted-foreground">Chưa có dữ liệu</p>
              )}
              {recentHosts.map((h) => (
                <div key={h.hostname} className="flex items-center justify-between px-6 py-3">
                  <div>
                    <div className="text-sm font-medium">{h.hostname}</div>
                    <div className="text-xs text-muted-foreground">{h.domain ?? "—"} · {h.osVersion ?? "—"}</div>
                  </div>
                  <div className="flex items-center gap-2 text-right">
                    <div>
                      <Badge variant={statusColor[h.status] as "success" | "destructive" | "warning" | "secondary"}>
                        {statusLabel[h.status]}
                      </Badge>
                      {h.openViolations > 0 && (
                        <div className="text-xs text-destructive mt-1">{h.openViolations} vi phạm</div>
                      )}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>

        {/* Violations */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Vi phạm gần đây</CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <div className="divide-y">
              {recentViolations.length === 0 && (
                <p className="px-6 py-4 text-sm text-muted-foreground">Không có vi phạm nào</p>
              )}
              {recentViolations.map((v) => (
                <div key={v.id} className="px-6 py-3">
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-medium">{v.hostname}</span>
                    <Badge variant={severityVariant[v.severity]}>{v.severity}</Badge>
                  </div>
                  <div className="text-xs text-muted-foreground mt-0.5">{v.message}</div>
                  <div className="text-xs text-muted-foreground">{formatDate(v.detectedAt)}</div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
