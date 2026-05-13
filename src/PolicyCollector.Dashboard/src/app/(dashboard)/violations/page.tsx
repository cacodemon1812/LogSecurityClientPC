"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { RefreshCw, ChevronLeft, ChevronRight, Search } from "lucide-react";
import { violationsApi } from "@/lib/api";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { Violation } from "@/types/api";
import { formatDate } from "@/lib/utils";

const severityVariant: Record<string, "critical" | "high" | "medium" | "low"> = {
  critical: "critical", high: "high", medium: "medium", low: "low",
};

export default function ViolationsPage() {
  const [hostname, setHostname] = useState("");
  const [severity, setSeverity] = useState("all");
  const [resolved, setResolved] = useState("false");
  const [page, setPage] = useState(1);
  const size = 25;

  const { data, isLoading, refetch } = useQuery({
    queryKey: ["violations", hostname, severity, resolved, page],
    queryFn: () =>
      violationsApi.list({
        hostname: hostname || undefined,
        severity: severity === "all" ? undefined : severity,
        resolved: resolved === "true",
        page, size,
      }).then((r) => r.data),
  });

  const items: Violation[] = data?.items ?? [];
  const total: number = data?.total ?? 0;
  const totalPages = Math.ceil(total / size);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Vi phạm chính sách</h1>
          <p className="text-muted-foreground">{total} vi phạm {resolved === "true" ? "đã xử lý" : "chưa xử lý"}</p>
        </div>
        <Button variant="outline" size="sm" onClick={() => refetch()}>
          <RefreshCw className="mr-2 h-4 w-4" />Làm mới
        </Button>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap gap-3">
        <div className="relative flex-1 min-w-[180px] max-w-xs">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Tìm hostname..."
            className="pl-9"
            value={hostname}
            onChange={(e) => { setHostname(e.target.value); setPage(1); }}
          />
        </div>
        <Select value={severity} onValueChange={(v) => { setSeverity(v); setPage(1); }}>
          <SelectTrigger className="w-40"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Tất cả mức độ</SelectItem>
            <SelectItem value="critical">Critical</SelectItem>
            <SelectItem value="high">High</SelectItem>
            <SelectItem value="medium">Medium</SelectItem>
            <SelectItem value="low">Low</SelectItem>
          </SelectContent>
        </Select>
        <Select value={resolved} onValueChange={(v) => { setResolved(v); setPage(1); }}>
          <SelectTrigger className="w-44"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="false">Chưa xử lý</SelectItem>
            <SelectItem value="true">Đã xử lý</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <Card>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/50">
                <tr>
                  <th className="px-4 py-3 text-left font-medium">Hostname</th>
                  <th className="px-4 py-3 text-left font-medium">Mức độ</th>
                  <th className="px-4 py-3 text-left font-medium">Rule</th>
                  <th className="px-4 py-3 text-left font-medium">Mô tả</th>
                  <th className="px-4 py-3 text-left font-medium">Mong đợi</th>
                  <th className="px-4 py-3 text-left font-medium">Thực tế</th>
                  <th className="px-4 py-3 text-left font-medium">Phát hiện lúc</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {isLoading && (
                  <tr><td colSpan={7} className="px-4 py-8 text-center text-muted-foreground">Đang tải...</td></tr>
                )}
                {!isLoading && items.length === 0 && (
                  <tr><td colSpan={7} className="px-4 py-8 text-center text-muted-foreground">Không có vi phạm nào</td></tr>
                )}
                {items.map((v) => (
                  <tr key={v.id} className="hover:bg-muted/30">
                    <td className="px-4 py-3 font-medium">{v.hostname}</td>
                    <td className="px-4 py-3">
                      <Badge variant={severityVariant[v.severity] ?? "secondary"}>{v.severity}</Badge>
                    </td>
                    <td className="px-4 py-3 font-mono text-xs text-muted-foreground">{v.ruleId}</td>
                    <td className="px-4 py-3 max-w-xs truncate" title={v.message}>{v.message}</td>
                    <td className="px-4 py-3 text-xs text-muted-foreground">{v.expected ?? "—"}</td>
                    <td className="px-4 py-3 text-xs text-muted-foreground">{v.actual ?? "—"}</td>
                    <td className="px-4 py-3 text-muted-foreground">{formatDate(v.detectedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
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
