# ADR-001 — Ngôn ngữ Agent: C# .NET 8

**Status:** Accepted  
**Date:** 2026-05-12

## Context

Agent cần chạy trên Windows endpoint với các yêu cầu:
- Đọc GPO result (chạy `gpresult.exe`, parse XML)
- Đọc WMI (Win32_Service, RSOP)
- Chạy PowerShell cmdlet trong-process (Get-NetFirewallRule, Get-MpPreference)
- Đọc Windows Registry
- Chạy như Windows Service với lifecycle (Start/Stop/Pause)
- Tự-contained: không yêu cầu runtime cài sẵn

## Quyết định

**Dùng C# / .NET 8 self-contained.**

## Lý do

- `System.Management` (WMI/CIM) là native .NET package, không cần P/Invoke phức tạp.
- `System.Management.Automation` NuGet cho PowerShell runspace trong-process (không fork process).
- `Microsoft.Win32.Registry` cho registry access type-safe.
- `UseWindowsService()` tích hợp SCM lifecycle.
- `PublishSingleFile + self-contained` → 1 file .exe, không cần .NET runtime cài trước.
- Team đã có kinh nghiệm C#, tái sử dụng model class giữa Agent và Backend.

## Alternatives đã xem xét

| | C# .NET 8 | Go | PowerShell Script |
|---|---|---|---|
| WMI/COM | Native | Third-party lib, limited | Native |
| PS Runspace | Native NuGet | Không có | N/A |
| Service lifecycle | UseWindowsService() | golang.org/x/sys/windows/svc | Task Scheduler |
| Single binary | Self-contained publish | Native | Không |
| Team familiarity | Cao | Thấp | Trung bình |

## Hệ quả

- Build artifact lớn hơn (~60-80 MB do self-contained), chấp nhận được với context GPO deployment.
- Cần .NET SDK 8 trên build machine (không phải endpoint).
