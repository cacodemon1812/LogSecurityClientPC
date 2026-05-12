# 00 — Tổng quan Dự án

## Vấn đề cần giải quyết

Trong môi trường doanh nghiệp có hàng trăm đến hàng nghìn máy Windows, việc kiểm tra thủ công cấu hình GPO, chính sách bảo mật và ứng dụng đã cài đặt là không khả thi. Các vấn đề thường gặp:

- GPO không được áp dụng do lỗi kết nối DC hoặc cấu hình OU sai mà không ai hay.
- Ứng dụng không được phê duyệt cài vào máy không bị phát hiện.
- Chính sách mật khẩu / firewall bị thay đổi cục bộ và không có alert.
- Không có visibility tập trung — mỗi lần kiểm tra phải RDP từng máy.

## Mục tiêu (Goals)

1. **Thu thập tự động** — Agent chạy định kỳ, không cần thao tác thủ công.
2. **Dữ liệu đầy đủ** — GPO applied, security policy, firewall, Defender, BitLocker, app inventory, services, scheduled tasks.
3. **Gửi về trung tâm** — Backend nhận, lưu trữ, truy vấn theo thời gian.
4. **Phát hiện bất thường** — So sánh với baseline, alert khi lệch.
5. **Triển khai hàng loạt** — Cài qua GPO Software Deployment / Intune / SCCM, không cần can thiệp từng máy.
6. **Hoạt động offline** — Agent buffer dữ liệu khi mất kết nối, gửi lại khi có mạng.

## Không làm (Non-Goals)

- Không phải EDR / antivirus — không scan file, không detect malware realtime.
- Không thực thi chính sách — chỉ đọc trạng thái, không thay đổi cấu hình máy.
- Không thay thế SIEM — không thu thập event log raw (chỉ thu thập cấu hình snapshot).
- Không hỗ trợ Linux / macOS trong phạm vi hiện tại.
- Không hỗ trợ Windows XP / Vista / 7 / 8 / Server 2012 trở xuống.

## Phạm vi OS mục tiêu

| OS | Phiên bản tối thiểu | Ghi chú |
|---|---|---|
| Windows 10 | 1809 (LTSC 2019) | .NET 8 yêu cầu tối thiểu Win10 1607 |
| Windows 11 | 21H2 | Hỗ trợ đầy đủ |
| Windows Server | 2019 (1809) | Bao gồm Core edition |
| Windows Server | 2022 | Bao gồm Core edition |
| Windows Server | 2025 | Bao gồm Core edition |

## Success Criteria

| Tiêu chí | Đo lường |
|---|---|
| Agent ổn định | Uptime ≥ 99% trong 30 ngày liên tiếp, không crash |
| Dữ liệu đầy đủ | ≥ 95% fields có giá trị (không null/empty) trên máy joined domain |
| Hiệu năng nhẹ | CPU spike < 5% trong 30 giây collect, RAM idle < 50 MB |
| Gửi thành công | ≥ 99% payload đến Backend trong vòng 5 phút sau khi collect |
| Phát hiện bất thường | Alert trong < 2 giờ khi GPO không áp dụng hoặc app lạ xuất hiện |
| Rollout | Cài đặt silent thành công trên ≥ 95% máy trong domain qua GPO |

## Các bên liên quan

| Vai trò | Trách nhiệm |
|---|---|
| Security Team | Định nghĩa baseline policy, xử lý alert |
| IT Admin | Quản lý deployment, cấu hình GPO cho agent |
| Developer | Phát triển, maintain Agent và Backend |
| Compliance | Sử dụng report để audit CIS/ISO 27001 |
