"use client";

import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Plus, RefreshCw, Pencil, Trash2, KeyRound } from "lucide-react";
import { usersApi } from "@/lib/api";
import { useAuth } from "@/hooks/use-auth";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import type { AppUser } from "@/types/api";
import { formatDate } from "@/lib/utils";

const roleColor: Record<string, "default" | "secondary" | "outline"> = {
  admin: "default", analyst: "secondary", viewer: "outline",
};
const roleLabel: Record<string, string> = {
  admin: "Quản trị viên", analyst: "Phân tích viên", viewer: "Xem",
};

type UserForm = { username: string; email: string; fullName: string; password: string; role: string; };
type EditForm = { fullName: string; role: string; active: boolean; };

export default function UsersPage() {
  const { user: currentUser } = useAuth();
  const qc = useQueryClient();

  const [showCreate, setShowCreate] = useState(false);
  const [editUser, setEditUser] = useState<AppUser | null>(null);
  const [pwdUser, setPwdUser] = useState<AppUser | null>(null);
  const [newPwd, setNewPwd] = useState("");
  const [form, setForm] = useState<UserForm>({ username: "", email: "", fullName: "", password: "", role: "viewer" });
  const [editForm, setEditForm] = useState<EditForm>({ fullName: "", role: "viewer", active: true });
  const [err, setErr] = useState("");

  const { data: users, isLoading, refetch } = useQuery<AppUser[]>({
    queryKey: ["users"],
    queryFn: () => usersApi.list().then((r) => r.data),
  });

  const createMut = useMutation({
    mutationFn: () => usersApi.create({ ...form, fullName: form.fullName || undefined }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["users"] }); setShowCreate(false); setForm({ username: "", email: "", fullName: "", password: "", role: "viewer" }); setErr(""); },
    onError: (e: unknown) => { const msg = (e as { response?: { data?: { error?: string } } }).response?.data?.error; setErr(msg ?? "Lỗi khi tạo người dùng"); },
  });

  const updateMut = useMutation({
    mutationFn: (id: number) => usersApi.update(id, { fullName: editForm.fullName || undefined, role: editForm.role, active: editForm.active }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["users"] }); setEditUser(null); },
  });

  const pwdMut = useMutation({
    mutationFn: (id: number) => usersApi.changePassword(id, newPwd),
    onSuccess: () => { setPwdUser(null); setNewPwd(""); },
  });

  const deleteMut = useMutation({
    mutationFn: (id: number) => usersApi.delete(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }),
  });

  const openEdit = (u: AppUser) => {
    setEditUser(u);
    setEditForm({ fullName: u.fullName ?? "", role: u.role, active: u.active });
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Quản lý người dùng</h1>
          <p className="text-muted-foreground">{users?.length ?? 0} tài khoản</p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" onClick={() => refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" />Làm mới
          </Button>
          <Button size="sm" onClick={() => { setShowCreate(true); setErr(""); }}>
            <Plus className="mr-2 h-4 w-4" />Thêm người dùng
          </Button>
        </div>
      </div>

      <Card>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/50">
                <tr>
                  <th className="px-4 py-3 text-left font-medium">Tên đăng nhập</th>
                  <th className="px-4 py-3 text-left font-medium">Email</th>
                  <th className="px-4 py-3 text-left font-medium">Họ tên</th>
                  <th className="px-4 py-3 text-left font-medium">Vai trò</th>
                  <th className="px-4 py-3 text-left font-medium">Trạng thái</th>
                  <th className="px-4 py-3 text-left font-medium">Tạo lúc</th>
                  <th className="px-4 py-3 text-left font-medium">Đăng nhập cuối</th>
                  <th className="px-4 py-3 text-left font-medium">Thao tác</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {isLoading && <tr><td colSpan={8} className="px-4 py-8 text-center text-muted-foreground">Đang tải...</td></tr>}
                {(users ?? []).map((u) => (
                  <tr key={u.id} className="hover:bg-muted/30">
                    <td className="px-4 py-3 font-medium">
                      {u.username}
                      {u.id === currentUser?.id && <span className="ml-1 text-xs text-muted-foreground">(bạn)</span>}
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">{u.email}</td>
                    <td className="px-4 py-3 text-muted-foreground">{u.fullName ?? "—"}</td>
                    <td className="px-4 py-3"><Badge variant={roleColor[u.role]}>{roleLabel[u.role]}</Badge></td>
                    <td className="px-4 py-3">
                      <Badge variant={u.active ? "success" : "secondary"}>{u.active ? "Hoạt động" : "Bị tắt"}</Badge>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">{formatDate(u.createdAt)}</td>
                    <td className="px-4 py-3 text-muted-foreground">{formatDate(u.lastLogin)}</td>
                    <td className="px-4 py-3">
                      <div className="flex gap-1">
                        <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => openEdit(u)} title="Chỉnh sửa">
                          <Pencil className="h-3.5 w-3.5" />
                        </Button>
                        <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setPwdUser(u); setNewPwd(""); }} title="Đổi mật khẩu">
                          <KeyRound className="h-3.5 w-3.5" />
                        </Button>
                        {u.id !== currentUser?.id && (
                          <Button variant="ghost" size="icon" className="h-8 w-8 text-destructive hover:text-destructive"
                            onClick={() => confirm(`Xóa người dùng "${u.username}"?`) && deleteMut.mutate(u.id)} title="Xóa">
                            <Trash2 className="h-3.5 w-3.5" />
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>

      {/* Create Dialog */}
      <Dialog open={showCreate} onOpenChange={setShowCreate}>
        <DialogContent>
          <DialogHeader><DialogTitle>Thêm người dùng mới</DialogTitle></DialogHeader>
          <div className="space-y-4">
            {err && <p className="rounded bg-destructive/10 px-3 py-2 text-sm text-destructive">{err}</p>}
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label>Tên đăng nhập *</Label>
                <Input value={form.username} onChange={(e) => setForm(f => ({ ...f, username: e.target.value }))} placeholder="jdoe" />
              </div>
              <div className="space-y-2">
                <Label>Email *</Label>
                <Input type="email" value={form.email} onChange={(e) => setForm(f => ({ ...f, email: e.target.value }))} placeholder="j.doe@corp.local" />
              </div>
            </div>
            <div className="space-y-2">
              <Label>Họ tên</Label>
              <Input value={form.fullName} onChange={(e) => setForm(f => ({ ...f, fullName: e.target.value }))} placeholder="John Doe" />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label>Mật khẩu * (≥8 ký tự)</Label>
                <Input type="password" value={form.password} onChange={(e) => setForm(f => ({ ...f, password: e.target.value }))} />
              </div>
              <div className="space-y-2">
                <Label>Vai trò</Label>
                <Select value={form.role} onValueChange={(v) => setForm(f => ({ ...f, role: v }))}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="admin">Quản trị viên</SelectItem>
                    <SelectItem value="analyst">Phân tích viên</SelectItem>
                    <SelectItem value="viewer">Xem</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowCreate(false)}>Hủy</Button>
            <Button onClick={() => createMut.mutate()} disabled={createMut.isPending}>
              {createMut.isPending ? "Đang tạo..." : "Tạo người dùng"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Edit Dialog */}
      <Dialog open={!!editUser} onOpenChange={(o) => !o && setEditUser(null)}>
        <DialogContent>
          <DialogHeader><DialogTitle>Chỉnh sửa: {editUser?.username}</DialogTitle></DialogHeader>
          <div className="space-y-4">
            <div className="space-y-2">
              <Label>Họ tên</Label>
              <Input value={editForm.fullName} onChange={(e) => setEditForm(f => ({ ...f, fullName: e.target.value }))} />
            </div>
            <div className="space-y-2">
              <Label>Vai trò</Label>
              <Select value={editForm.role} onValueChange={(v) => setEditForm(f => ({ ...f, role: v }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="admin">Quản trị viên</SelectItem>
                  <SelectItem value="analyst">Phân tích viên</SelectItem>
                  <SelectItem value="viewer">Xem</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="flex items-center gap-3">
              <Switch checked={editForm.active} onCheckedChange={(v) => setEditForm(f => ({ ...f, active: v }))} />
              <Label>Tài khoản hoạt động</Label>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setEditUser(null)}>Hủy</Button>
            <Button onClick={() => editUser && updateMut.mutate(editUser.id)} disabled={updateMut.isPending}>
              {updateMut.isPending ? "Đang lưu..." : "Lưu thay đổi"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Change Password Dialog */}
      <Dialog open={!!pwdUser} onOpenChange={(o) => !o && setPwdUser(null)}>
        <DialogContent>
          <DialogHeader><DialogTitle>Đổi mật khẩu: {pwdUser?.username}</DialogTitle></DialogHeader>
          <div className="space-y-2">
            <Label>Mật khẩu mới (≥8 ký tự)</Label>
            <Input type="password" value={newPwd} onChange={(e) => setNewPwd(e.target.value)} autoFocus />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setPwdUser(null)}>Hủy</Button>
            <Button onClick={() => pwdUser && pwdMut.mutate(pwdUser.id)} disabled={pwdMut.isPending || newPwd.length < 8}>
              {pwdMut.isPending ? "Đang lưu..." : "Đổi mật khẩu"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
