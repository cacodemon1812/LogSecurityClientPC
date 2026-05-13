"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { RefreshCw, Shield } from "lucide-react";
import { rulesApi } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { PolicyRule } from "@/types/api";

const severityVariant: Record<string, "critical" | "high" | "medium" | "low"> = {
  critical: "critical", high: "high", medium: "medium", low: "low",
};

export default function RulesPage() {
  const qc = useQueryClient();

  const { data: rules, isLoading, refetch } = useQuery<PolicyRule[]>({
    queryKey: ["rules"],
    queryFn: () => rulesApi.list().then((r) => r.data),
  });

  const updateMutation = useMutation({
    mutationFn: ({ ruleId, enabled, severity }: { ruleId: string; enabled: boolean; severity?: string }) =>
      rulesApi.update(ruleId, { enabled, severity }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["rules"] }),
  });

  const handleToggle = (rule: PolicyRule) => {
    updateMutation.mutate({ ruleId: rule.ruleId, enabled: !rule.enabled, severity: rule.severity });
  };

  const handleSeverity = (rule: PolicyRule, severity: string) => {
    updateMutation.mutate({ ruleId: rule.ruleId, enabled: rule.enabled, severity });
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Quy tắc chính sách</h1>
          <p className="text-muted-foreground">{rules?.length ?? 0} quy tắc · Bật/tắt và điều chỉnh mức độ nghiêm trọng</p>
        </div>
        <Button variant="outline" size="sm" onClick={() => refetch()}>
          <RefreshCw className="mr-2 h-4 w-4" />Làm mới
        </Button>
      </div>

      <Card>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/50">
                <tr>
                  <th className="px-4 py-3 text-left font-medium">Rule ID</th>
                  <th className="px-4 py-3 text-left font-medium">Mô tả</th>
                  <th className="px-4 py-3 text-left font-medium">Mức độ</th>
                  <th className="px-4 py-3 text-left font-medium">Trạng thái</th>
                  <th className="px-4 py-3 text-left font-medium">Bật/tắt</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {isLoading && (
                  <tr><td colSpan={5} className="px-4 py-8 text-center text-muted-foreground">Đang tải...</td></tr>
                )}
                {!isLoading && (!rules || rules.length === 0) && (
                  <tr><td colSpan={5} className="px-4 py-8 text-center text-muted-foreground">
                    <div className="flex flex-col items-center gap-2">
                      <Shield className="h-8 w-8 text-muted-foreground/50" />
                      Không có quy tắc nào. Seed dữ liệu DB trước.
                    </div>
                  </td></tr>
                )}
                {(rules ?? []).map((rule) => (
                  <tr key={rule.ruleId} className={`hover:bg-muted/30 ${!rule.enabled ? "opacity-50" : ""}`}>
                    <td className="px-4 py-3 font-mono text-xs font-medium">{rule.ruleId}</td>
                    <td className="px-4 py-3 text-muted-foreground max-w-sm">{rule.description}</td>
                    <td className="px-4 py-3">
                      <Select
                        value={rule.severity}
                        onValueChange={(v) => handleSeverity(rule, v)}
                        disabled={updateMutation.isPending}
                      >
                        <SelectTrigger className="h-8 w-28">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="critical">Critical</SelectItem>
                          <SelectItem value="high">High</SelectItem>
                          <SelectItem value="medium">Medium</SelectItem>
                          <SelectItem value="low">Low</SelectItem>
                        </SelectContent>
                      </Select>
                    </td>
                    <td className="px-4 py-3">
                      <Badge variant={rule.enabled ? (severityVariant[rule.severity] ?? "secondary") : "secondary"}>
                        {rule.enabled ? rule.severity : "Tắt"}
                      </Badge>
                    </td>
                    <td className="px-4 py-3">
                      <Switch
                        checked={rule.enabled}
                        onCheckedChange={() => handleToggle(rule)}
                        disabled={updateMutation.isPending}
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
