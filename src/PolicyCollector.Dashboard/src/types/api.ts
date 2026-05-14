export interface PaginatedResponse<T> {
  total: number;
  page: number;
  size: number;
  items: T[];
}

export interface HostSummary {
  id: string;
  hostname: string;
  domain: string | null;
  osVersion: string | null;
  agentVersion: string | null;
  lastSeen: string | null;
  status: "online" | "offline" | "stale" | "unknown";
  openViolations: number;
}

export interface Violation {
  id: number;
  snapshotId: string;
  hostname: string;
  detectedAt: string;
  ruleId: string;
  severity: "critical" | "high" | "medium" | "low";
  message: string;
  expected: string | null;
  actual: string | null;
  resolved: boolean;
  resolvedAt: string | null;
}

export interface AppInventory {
  display_name: string;
  version: string | null;
  publisher: string | null;
  machineCount: number;
  lastSeen: string | null;
}

export interface PolicyRule {
  id: number;
  ruleId: string;
  severity: string;
  description: string;
  enabled: boolean;
}

export interface AdminStats {
  total_hosts: number;
  online_hosts: number;
  offline_hosts: number;
  open_violations: number;
  critical_violations: number;
  ingestions_last_hour: number;
}

export interface AppUser {
  id: number;
  username: string;
  email: string;
  fullName: string | null;
  role: "admin" | "analyst" | "viewer";
  active: boolean;
  createdAt: string;
  lastLogin: string | null;
}

export interface AuthUser {
  id: number;
  username: string;
  email: string;
  fullName: string | null;
  role: "admin" | "analyst" | "viewer";
}

export interface LoginResponse {
  token: string;
  user: AuthUser;
}

export interface DiffChange {
  fieldPath: string;
  oldValue: string | null;
  newValue: string | null;
  changedAt: string;
}

export interface DiffResponse {
  hostname: string;
  fromSnapshotId: string | null;
  toSnapshotId: string;
  fromTime: string | null;
  toTime: string;
  changes: DiffChange[];
}
