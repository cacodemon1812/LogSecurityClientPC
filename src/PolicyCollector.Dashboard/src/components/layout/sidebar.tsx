"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  LayoutDashboard, Monitor, AlertTriangle, Package,
  FileText, Shield, Users, LogOut, ChevronRight, ShieldCheck, FileCheck,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useAuth } from "@/hooks/use-auth";

const navItems = [
  { href: "/", label: "Tổng quan", icon: LayoutDashboard },
  { href: "/hosts", label: "Máy tính", icon: Monitor },
  { href: "/violations", label: "Vi phạm chính sách", icon: AlertTriangle },
  { href: "/security", label: "Bảo mật fleet", icon: ShieldCheck },
  { href: "/patches", label: "Tuân thủ bản vá", icon: FileCheck },
  { href: "/inventory", label: "Phần mềm", icon: Package },
  { href: "/reports", label: "Báo cáo", icon: FileText },
];

const adminItems = [
  { href: "/admin/rules", label: "Quy tắc chính sách", icon: Shield },
  { href: "/admin/users", label: "Quản lý người dùng", icon: Users },
];

export function Sidebar() {
  const pathname = usePathname();
  const { user, logout, isAdmin } = useAuth();

  return (
    <aside className="flex h-screen w-64 flex-col border-r bg-card">
      {/* Logo */}
      <div className="flex h-16 items-center gap-3 border-b px-6">
        <Shield className="h-6 w-6 text-primary" />
        <div>
          <div className="text-sm font-semibold">PolicyCollector</div>
          <div className="text-xs text-muted-foreground">Security Dashboard</div>
        </div>
      </div>

      {/* Nav */}
      <nav className="flex-1 space-y-1 overflow-y-auto px-3 py-4">
        <div className="mb-2 px-3 text-xs font-medium uppercase text-muted-foreground">Giám sát</div>
        {navItems.map(({ href, label, icon: Icon }) => (
          <Link
            key={href}
            href={href}
            className={cn(
              "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
              pathname === href
                ? "bg-primary/10 text-primary"
                : "text-muted-foreground hover:bg-accent hover:text-accent-foreground"
            )}
          >
            <Icon className="h-4 w-4" />
            {label}
            {pathname === href && <ChevronRight className="ml-auto h-4 w-4" />}
          </Link>
        ))}

        {isAdmin && (
          <>
            <div className="mb-2 mt-4 px-3 text-xs font-medium uppercase text-muted-foreground">Quản trị</div>
            {adminItems.map(({ href, label, icon: Icon }) => (
              <Link
                key={href}
                href={href}
                className={cn(
                  "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
                  pathname === href
                    ? "bg-primary/10 text-primary"
                    : "text-muted-foreground hover:bg-accent hover:text-accent-foreground"
                )}
              >
                <Icon className="h-4 w-4" />
                {label}
                {pathname === href && <ChevronRight className="ml-auto h-4 w-4" />}
              </Link>
            ))}
          </>
        )}
      </nav>

      {/* User */}
      <div className="border-t p-4">
        <div className="flex items-center gap-3">
          <div className="flex h-8 w-8 items-center justify-center rounded-full bg-primary/10 text-sm font-semibold text-primary">
            {user?.username?.[0]?.toUpperCase() ?? "?"}
          </div>
          <div className="flex-1 min-w-0">
            <div className="truncate text-sm font-medium">{user?.username}</div>
            <div className="truncate text-xs text-muted-foreground capitalize">{user?.role}</div>
          </div>
          <button
            onClick={logout}
            className="rounded p-1 text-muted-foreground hover:bg-accent hover:text-accent-foreground"
            title="Đăng xuất"
          >
            <LogOut className="h-4 w-4" />
          </button>
        </div>
      </div>
    </aside>
  );
}
