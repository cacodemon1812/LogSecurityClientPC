# 11 — Glossary

## Domain Terms

| Thuật ngữ | Định nghĩa |
|---|---|
| **GPO** | Group Policy Object — đối tượng chính sách nhóm trong Active Directory, áp dụng cấu hình lên User/Computer trong OU |
| **RSOP** | Resultant Set of Policy — tập hợp chính sách thực sự được áp dụng trên 1 máy, sau khi giải quyết xung đột giữa nhiều GPO |
| **CSE** | Client-Side Extension — module xử lý từng loại policy setting (Registry CSE, Security CSE, Software Installation CSE...) |
| **secedit** | Công cụ dòng lệnh Windows để export/import/phân tích security policy (password, user rights, audit) |
| **auditpol** | Công cụ dòng lệnh Windows để xem/sửa audit policy chi tiết (Object Access, Logon Events...) |
| **WMI** | Windows Management Instrumentation — cơ sở hạ tầng quản lý hệ thống Windows, cung cấp interface để query thông tin hệ thống |
| **CIM** | Common Information Model — chuẩn mới hơn WMI, dùng `Get-CimInstance` trong PowerShell |
| **DPAPI** | Data Protection API — Windows API mã hoá dữ liệu gắn với user account hoặc machine account |
| **Credential Manager** | Windows vault lưu credential an toàn, truy cập qua DPAPI |
| **UAC** | User Account Control — cơ chế Windows yêu cầu xác nhận khi thực thi action với quyền admin |
| **NLA** | Network Level Authentication — yêu cầu xác thực trước khi mở RDP session |
| **SCHANNEL** | Secure Channel — Windows security provider quản lý TLS/SSL |
| **BitLocker** | Tính năng mã hoá toàn bộ ổ đĩa của Windows |
| **AppX / MSIX** | Định dạng package ứng dụng Universal Windows Platform |
| **SCM** | Service Control Manager — Windows component quản lý lifecycle của Windows Services |
| **SYSVOL** | Thư mục chia sẻ trên Domain Controller chứa GPO templates và scripts |
| **OU** | Organizational Unit — đơn vị tổ chức trong Active Directory để nhóm và áp GPO |
| **mTLS** | Mutual TLS — xác thực 2 chiều, cả client và server đều có certificate |
| **HMAC** | Hash-based Message Authentication Code — chữ ký hash dùng shared secret để xác minh toàn vẹn message |

## Project-Specific Terms

| Thuật ngữ | Định nghĩa |
|---|---|
| **Agent** | PolicyCollector Windows Service chạy trên endpoint |
| **Backend** | Hệ thống thu thập trung tâm (Ingest API + Workers + DB) |
| **Collector** | Module độc lập thu thập 1 loại dữ liệu (GPO, SecPolicy, Apps...) |
| **CollectionJob** | Orchestrator chạy tất cả collectors và build payload |
| **Payload** | JSON object chứa toàn bộ dữ liệu 1 lần collect |
| **Snapshot** | 1 bản lưu trữ payload trong DB, đại diện cho trạng thái tại 1 thời điểm |
| **Diff** | Sự khác biệt giữa 2 snapshot liên tiếp |
| **Violation** | Khi snapshot vi phạm 1 rule trong policy baseline |
| **LocalQueue** | SQLite offline buffer trên agent |
| **Outbox Pattern** | Pattern lưu message vào DB trước khi gửi, đảm bảo không mất khi crash |
| **ADMX** | Administrative Template XML — file định nghĩa GPO settings hiển thị trong GPMC |
| **Staged Rollout** | Triển khai dần từ pilot → toàn bộ để kiểm soát rủi ro |
| **schema_version** | Trường trong payload xác định cấu trúc dữ liệu, dùng để tương thích Agent/Backend khác version |
| **Baseline** | Cấu hình chuẩn (expected state) để so sánh với actual state |
| **ViolationEngine** | Module Backend đánh giá snapshot theo rule set, phát hiện violations |
