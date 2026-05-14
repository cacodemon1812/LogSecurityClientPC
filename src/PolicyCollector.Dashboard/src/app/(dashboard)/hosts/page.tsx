"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Search, RefreshCw, ChevronLeft, ChevronRight } from "lucide-react";
import { hostsApi } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { HostSummary } from "@/types/api";
import { timeAgo } from "@/lib/utils";

const statusVariant: Record<string, "success" | "destructive" | "warning" | "secondary"> = {
  online: "success", offline: "destructive", stale: "warning", unknown: "secondary",
};
const statusLabel: Record<string, string> = {
  online: "Online", offline: "Offline", stale: "Không hoạt động", unknown: "Chưa rõ",
};

export default function HostsPage() {
  const router = useRouter();
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("all");
  const [page, setPage] = useState(1);
  const size = 20;

  const { data, isLoading, refetch } = useQuery({
    queryKey: ["hosts", search, status, page],
    queryFn: () =>
      hostsApi.list({
        domain: search || undefined,
        status: status === "all" ? undefined : status,
        page, size, sort: "last_seen", order: "desc",
      }).then((r) => r.data),
  });

  const items: HostSummary[] = data?.items ?? [];
  const total: number = data?.total ?? 0;
  const totalPages = Math.ceil(total / size);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Máy tính</h1>
          <p className="text-muted-foreground">{total} máy đã đăng ký</p>
        </div>
        <Button variant="outline" size="sm" onClick={() => refetch()}>
          <RefreshCw className="mr-2 h-4 w-4" />Làm mới
        </Button>
      </div>

      {/* Filters */}
      <div className="flex gap-3">
        <div className="relative flex-1 max-w-sm">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Tìm theo domain..."
            className="pl-9"
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
          />
        </div>
        <Select value={status} onValueChange={(v) => { setStatus(v); setPage(1); }}>
          <SelectTrigger className="w-44"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Tất cả trạng thái</SelectItem>
            <SelectItem value="online">Online</SelectItem>
            <SelectItem value="offline">Offline</SelectItem>
            <SelectItem value="stale">Không hoạt động</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* Table */}
      <Card>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/50">
                <tr>
                  <th className="px-4 py-3 text-left font-medium">Hostname</th>
                  <th className="px-4 py-3 text-left font-medium">Domain</th>
                  <th className="px-4 py-3 text-left font-medium">OS</th>
                  <th className="px-4 py-3 text-left font-medium">Agent</th>
                  <th className="px-4 py-3 text-left font-medium">Lần cuối</th>
                  <th className="px-4 py-3 text-left font-medium">Trạng thái</th>
                  <th className="px-4 py-3 text-left font-medium">Vi phạm</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {isLoading && (
                  <tr><td colSpan={7} className="px-4 py-8 text-center text-muted-foreground">Đang tải...</td></tr>
                )}
                {!isLoading && items.length === 0 && (
                  <tr><td colSpan={7} className="px-4 py-8 text-center text-muted-foreground">Không có dữ liệu</td></tr>
                )}
                {items.map((h) => (
                  <tr
                    key={h.id}
                    className="hover:bg-muted/30 cursor-pointer"
                    onClick={() => router.push(`/hosts/${h.id}`)}
                  >
                    <td className="px-4 py-3 font-medium">{h.hostname}</td>
                    <td className="px-4 py-3 text-muted-foreground">{h.domain ?? "—"}</td>
                    <td className="px-4 py-3 text-muted-foreground">{h.osVersion ?? "—"}</td>
                    <td className="px-4 py-3 text-muted-foreground">{h.agentVersion ?? "—"}</td>
                    <td className="px-4 py-3 text-muted-foreground">{timeAgo(h.lastSeen)}</td>
                    <td className="px-4 py-3">
                      <Badge variant={statusVariant[h.status] ?? "secondary"}>{statusLabel[h.status] ?? h.status}</Badge>
                    </td>
                    <td className="px-4 py-3">
                      {h.openViolations > 0
                        ? <Badge variant="destructive">{h.openViolations}</Badge>
                        : <span className="text-muted-foreground">—</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="flex items-center justify-between border-t px-4 py-3">
              <span className="text-sm text-muted-foreground">Trang {page}/{totalPages} · {total} kết quả</span>
              <div className="flex gap-2">
                <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>
                  <ChevronLeft className="h-4 w-4" />
                </Button>
                <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}>
                  <ChevronRight className="h-4 w-4" />
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
