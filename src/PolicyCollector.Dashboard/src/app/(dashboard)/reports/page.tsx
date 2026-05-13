"use client";

import { useState } from "react";
import { Download, FileText, AlertTriangle, Loader2 } from "lucide-react";
import { reportsApi } from "@/lib/api";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

export default function ReportsPage() {
  const [complianceDomain, setComplianceDomain] = useState("");
  const [violationsDomain, setViolationsDomain] = useState("");
  const [violationsSeverity, setViolationsSeverity] = useState("all");
  const [loadingCompliance, setLoadingCompliance] = useState(false);
  const [loadingViolations, setLoadingViolations] = useState(false);

  const downloadCompliance = async () => {
    setLoadingCompliance(true);
    try {
      const res = await reportsApi.complianceCsv(complianceDomain || undefined);
      downloadBlob(res.data, `compliance-${new Date().toISOString().slice(0, 10)}.csv`);
    } catch { alert("Xuất báo cáo thất bại"); }
    finally { setLoadingCompliance(false); }
  };

  const downloadViolations = async () => {
    setLoadingViolations(true);
    try {
      const res = await reportsApi.violationsCsv(
        violationsDomain || undefined,
        violationsSeverity === "all" ? undefined : violationsSeverity
      );
      downloadBlob(res.data, `violations-${new Date().toISOString().slice(0, 10)}.csv`);
    } catch { alert("Xuất báo cáo thất bại"); }
    finally { setLoadingViolations(false); }
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Báo cáo</h1>
        <p className="text-muted-foreground">Xuất báo cáo CSV để audit hoặc lưu trữ</p>
      </div>

      <div className="grid gap-6 md:grid-cols-2">
        {/* Compliance Report */}
        <Card>
          <CardHeader>
            <div className="flex items-center gap-3">
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10">
                <FileText className="h-5 w-5 text-primary" />
              </div>
              <div>
                <CardTitle className="text-base">Báo cáo Compliance</CardTitle>
                <CardDescription>Tổng hợp trạng thái tuân thủ của tất cả máy</CardDescription>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label>Lọc theo domain (tùy chọn)</Label>
              <Input
                placeholder="corp.local"
                value={complianceDomain}
                onChange={(e) => setComplianceDomain(e.target.value)}
              />
            </div>
            <div className="rounded-md bg-muted/50 p-3 text-xs text-muted-foreground space-y-1">
              <div>Nội dung: hostname, domain, os, trạng thái, số vi phạm, lần check-in cuối</div>
              <div>Định dạng: CSV (UTF-8), có thể mở bằng Excel</div>
            </div>
            <Button className="w-full" onClick={downloadCompliance} disabled={loadingCompliance}>
              {loadingCompliance
                ? <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Đang xuất...</>
                : <><Download className="mr-2 h-4 w-4" />Tải báo cáo compliance</>}
            </Button>
          </CardContent>
        </Card>

        {/* Violations Report */}
        <Card>
          <CardHeader>
            <div className="flex items-center gap-3">
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-destructive/10">
                <AlertTriangle className="h-5 w-5 text-destructive" />
              </div>
              <div>
                <CardTitle className="text-base">Báo cáo Vi phạm</CardTitle>
                <CardDescription>Danh sách chi tiết các vi phạm chính sách</CardDescription>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label>Lọc theo domain (tùy chọn)</Label>
              <Input
                placeholder="corp.local"
                value={violationsDomain}
                onChange={(e) => setViolationsDomain(e.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label>Mức độ nghiêm trọng</Label>
              <Select value={violationsSeverity} onValueChange={setViolationsSeverity}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">Tất cả mức độ</SelectItem>
                  <SelectItem value="critical">Critical</SelectItem>
                  <SelectItem value="high">High</SelectItem>
                  <SelectItem value="medium">Medium</SelectItem>
                  <SelectItem value="low">Low</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="rounded-md bg-muted/50 p-3 text-xs text-muted-foreground space-y-1">
              <div>Nội dung: hostname, rule, severity, message, expected, actual, detected_at</div>
              <div>Chỉ bao gồm vi phạm chưa xử lý</div>
            </div>
            <Button className="w-full" variant="destructive" onClick={downloadViolations} disabled={loadingViolations}>
              {loadingViolations
                ? <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Đang xuất...</>
                : <><Download className="mr-2 h-4 w-4" />Tải báo cáo vi phạm</>}
            </Button>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
