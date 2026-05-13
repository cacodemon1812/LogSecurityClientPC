import axios from "axios";

const BASE_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:8080";

export const api = axios.create({
  baseURL: BASE_URL,
  headers: { "Content-Type": "application/json" },
  timeout: 15000,
});

// Inject JWT from localStorage on every request
api.interceptors.request.use((config) => {
  if (typeof window !== "undefined") {
    const token = localStorage.getItem("pc_token");
    if (token) config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Redirect to login on 401
api.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err.response?.status === 401 && typeof window !== "undefined") {
      localStorage.removeItem("pc_token");
      localStorage.removeItem("pc_user");
      window.location.href = "/login";
    }
    return Promise.reject(err);
  }
);

// ── Auth ─────────────────────────────────────────────────────────────────────
export const authApi = {
  login: (username: string, password: string) =>
    api.post("/api/v1/auth/login", { username, password }),
  logout: () => api.post("/api/v1/auth/logout"),
  me: () => api.get("/api/v1/auth/me"),
};

// ── Stats ─────────────────────────────────────────────────────────────────────
export const statsApi = {
  get: () => api.get("/api/v1/admin/stats"),
};

// ── Hosts ─────────────────────────────────────────────────────────────────────
export const hostsApi = {
  list: (params?: { domain?: string; status?: string; page?: number; size?: number; sort?: string; order?: string }) =>
    api.get("/api/v1/hosts", { params }),
  getLatest: (hostname: string) =>
    api.get(`/api/v1/hosts/${encodeURIComponent(hostname)}/latest`),
  getDiff: (hostname: string, from?: string, to?: string) =>
    api.get(`/api/v1/hosts/${encodeURIComponent(hostname)}/diff`, { params: { from, to } }),
};

// ── Violations ────────────────────────────────────────────────────────────────
export const violationsApi = {
  list: (params?: { hostname?: string; severity?: string; ruleId?: string; resolved?: boolean; page?: number; size?: number }) =>
    api.get("/api/v1/policy/violations", { params }),
};

// ── App Inventory ─────────────────────────────────────────────────────────────
export const inventoryApi = {
  list: (params?: { name?: string; publisher?: string; hostname?: string; page?: number; size?: number }) =>
    api.get("/api/v1/apps/inventory", { params }),
};

// ── Policy Rules ──────────────────────────────────────────────────────────────
export const rulesApi = {
  list: () => api.get("/api/v1/admin/rules"),
  update: (ruleId: string, payload: { enabled: boolean; severity?: string }) =>
    api.put(`/api/v1/admin/rules/${encodeURIComponent(ruleId)}`, payload),
};

// ── Users ─────────────────────────────────────────────────────────────────────
export const usersApi = {
  list: () => api.get("/api/v1/admin/users"),
  create: (data: { username: string; email: string; fullName?: string; password: string; role: string }) =>
    api.post("/api/v1/admin/users", data),
  update: (id: number, data: { fullName?: string; role?: string; active?: boolean }) =>
    api.put(`/api/v1/admin/users/${id}`, data),
  changePassword: (id: number, newPassword: string) =>
    api.put(`/api/v1/admin/users/${id}/password`, { newPassword }),
  delete: (id: number) => api.delete(`/api/v1/admin/users/${id}`),
};

// ── Security Overview ─────────────────────────────────────────────────────────
export const securityApi = {
  overview: () => api.get("/api/v1/admin/security-overview"),
};

// ── Patch Compliance ──────────────────────────────────────────────────────────
export const patchesApi = {
  list: () => api.get("/api/v1/admin/patches"),
};

// ── Reports ───────────────────────────────────────────────────────────────────
export const reportsApi = {
  complianceCsv: (domain?: string) =>
    api.get("/api/v1/reports/compliance", { params: { domain }, responseType: "blob" }),
  violationsCsv: (domain?: string, severity?: string) =>
    api.get("/api/v1/reports/violations", { params: { domain, severity }, responseType: "blob" }),
};
