"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Search,
  RefreshCw,
  ChevronLeft,
  ChevronRight,
  Package,
} from "lucide-react";
import { inventoryApi } from "@/lib/api";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { AppInventory } from "@/types/api";
import { formatDate } from "@/lib/utils";

export default function InventoryPage() {
  const [name, setName] = useState("");
  const [publisher, setPublisher] = useState("");
  const [hostname, setHostname] = useState("");
  const [page, setPage] = useState(1);
  const size = 50;

  const { data, isLoading, refetch } = useQuery({
    queryKey: ["inventory", name, publisher, hostname, page],
    queryFn: () =>
      inventoryApi
        .list({
          name: name || undefined,
          publisher: publisher || undefined,
          hostname: hostname || undefined,
          page,
          size,
        })
        .then((r) => r.data),
  });

  const items: AppInventory[] = data?.items ?? [];
  const total: number = data?.total ?? 0;
  const totalPages = Math.ceil(total / size);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Phần mềm đã cài</h1>
          <p className="text-muted-foreground">
            {total} ứng dụng duy nhất trên toàn bộ máy
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => refetch()}>
          <RefreshCw className="mr-2 h-4 w-4" />
          Làm mới
        </Button>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap gap-3">
        <div className="relative flex-1 min-w-[160px] max-w-xs">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Tên ứng dụng..."
            className="pl-9"
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              setPage(1);
            }}
          />
        </div>
        <div className="relative flex-1 min-w-[160px] max-w-xs">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Nhà phát hành..."
            className="pl-9"
            value={publisher}
            onChange={(e) => {
              setPublisher(e.target.value);
              setPage(1);
            }}
          />
        </div>
        <div className="relative flex-1 min-w-[160px] max-w-xs">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Hostname..."
            className="pl-9"
            value={hostname}
            onChange={(e) => {
              setHostname(e.target.value);
              setPage(1);
            }}
          />
        </div>
      </div>

      <Card>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/50">
                <tr>
                  <th className="px-4 py-3 text-left font-medium">
                    Tên ứng dụng
                  </th>
                  <th className="px-4 py-3 text-left font-medium">Phiên bản</th>
                  <th className="px-4 py-3 text-left font-medium">
                    Nhà phát hành
                  </th>
                  <th className="px-4 py-3 text-left font-medium">Số máy</th>
                  <th className="px-4 py-3 text-left font-medium">
                    Lần cuối ghi nhận
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {isLoading && (
                  <tr>
                    <td
                      colSpan={5}
                      className="px-4 py-8 text-center text-muted-foreground"
                    >
                      Đang tải...
                    </td>
                  </tr>
                )}
                {!isLoading && items.length === 0 && (
                  <tr>
                    <td
                      colSpan={5}
                      className="px-4 py-8 text-center text-muted-foreground"
                    >
                      <div className="flex flex-col items-center gap-2">
                        <Package className="h-8 w-8 text-muted-foreground/50" />
                        Không tìm thấy ứng dụng nào
                      </div>
                    </td>
                  </tr>
                )}
                {items.map((app, i) => (
                  <tr
                    key={`${app.display_name}-${i}`}
                    className="hover:bg-muted/30"
                  >
                    <td className="px-4 py-3 font-medium">
                      {app.display_name}
                    </td>
                    <td className="px-4 py-3 text-muted-foreground font-mono text-xs">
                      {app.version ?? "—"}
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      {app.publisher ?? "—"}
                    </td>
                    <td className="px-4 py-3">
                      <span className="inline-flex items-center rounded-full bg-primary/10 px-2.5 py-0.5 text-xs font-medium text-primary">
                        {app.machineCount} máy
                      </span>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      {formatDate(app.lastSeen)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {totalPages > 1 && (
            <div className="flex items-center justify-between border-t px-4 py-3">
              <span className="text-sm text-muted-foreground">
                Trang {page}/{totalPages} · {total} kết quả
              </span>
              <div className="flex gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={page <= 1}
                  onClick={() => setPage((p) => p - 1)}
                >
                  <ChevronLeft className="h-4 w-4" />
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={page >= totalPages}
                  onClick={() => setPage((p) => p + 1)}
                >
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
