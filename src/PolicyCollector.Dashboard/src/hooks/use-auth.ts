"use client";

import { useEffect, useState } from "react";
import type { AuthUser } from "@/types/api";

export function useAuth() {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const stored = localStorage.getItem("pc_user");
    if (stored) {
      try { setUser(JSON.parse(stored)); } catch { /* ignore */ }
    }
    setLoading(false);
  }, []);

  const login = (token: string, u: AuthUser) => {
    localStorage.setItem("pc_token", token);
    localStorage.setItem("pc_user", JSON.stringify(u));
    setUser(u);
  };

  const logout = () => {
    localStorage.removeItem("pc_token");
    localStorage.removeItem("pc_user");
    setUser(null);
    window.location.href = "/login";
  };

  return { user, loading, login, logout, isAdmin: user?.role === "admin" };
}
