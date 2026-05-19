# So sánh Agent thu thập hiện tại và Kaspersky Security Center (KSC/KAS)

Ngày cập nhật: 2026-05-19

## 1) Mục tiêu tài liệu

Tài liệu này giúp bạn quyết định chiến lược giám sát và bảo đảm an ninh mạng cho máy người dùng trong công ty theo 3 hướng:

- Phát triển thêm Agent thu thập tại endpoint.
- Khai thác sâu dữ liệu từ KSC/KAS master server.
- Kết hợp hai nguồn (Hybrid) để có độ phủ cao nhất.

## 2) Kết luận điều hành (Executive Summary)

Khuyến nghị chính: triển khai mô hình Hybrid, trong đó:

- Agent local đóng vai trò "ground truth" tại máy (cấu hình thực tế, độ sâu kỹ thuật cao).
- KSC/KAS đóng vai trò "control plane" tập trung (quản trị, chính sách, tác vụ, vòng đời sự cố).

Vì sao không nên chỉ chọn một bên:

- Chỉ Agent: thiếu góc nhìn tập trung theo nhóm/phòng ban, thiếu policy governance và vòng đời xử lý sự cố tập trung.
- Chỉ KSC: thiếu nhiều tín hiệu hardening cấp OS và dấu hiệu persistence/attack surface mà Agent hiện có đang thu rất tốt.

Kỳ vọng khi đi Hybrid:

- Giảm điểm mù kỹ thuật ở endpoint.
- Tăng năng lực quản trị tập trung và audit compliance.
- Nâng chất lượng cảnh báo: cảnh báo dựa trên tương quan hai nguồn thay vì chỉ một chiều.

## 3) Đánh giá chi tiết năng lực Agent hiện tại

Theo phạm vi hiện có, Agent đang thu 21 collector và có giá trị cao ở các lớp sau:

### 3.1. Lớp kiểm soát cấu hình bảo mật endpoint

- SecurityPolicy (secedit, auditpol, UAC/TLS/RDP)
- RegistryAudit (WDigest, SMBv1, NTLM, Winlogon persistence, PowerShell logging)
- Defender, BitLocker, Patch, LAPS

Ý nghĩa an ninh:

- Phát hiện lệch baseline hardening rất sát thực tế máy.
- Có thể tạo rule vi phạm mức critical/high từ bằng chứng kỹ thuật rõ ràng.

### 3.2. Lớp phơi bày bề mặt tấn công (Exposure)

- Firewall profiles/rules, listening ports, risky ports.
- RemoteAccess (WinRM/SSH/Telnet), SharedFolders, WiFi profile risk.

Ý nghĩa an ninh:

- Dễ phát hiện lỗ hổng cấu hình dẫn đến lateral movement.
- Dễ ưu tiên khắc phục theo mức độ rủi ro thực tế.

### 3.3. Lớp kiểm tra persistence và vận hành endpoint

- Services, ScheduledTasks, StartupEntries, LocalAccounts, App inventory.

Ý nghĩa an ninh:

- Tốt cho use case "săn tìm cấu hình bất thường" và kiểm soát thay đổi trái phép.

### 3.4. Điểm còn thiếu của Agent (nếu muốn thay vai trò KSC)

- Chưa có trạng thái quản trị tập trung theo cấu trúc tổ chức (group/path) trong KSC.
- Chưa có vòng đời tác vụ Kaspersky (deploy/update/scan) ở cấp điều phối tập trung.
- Chưa có dòng sự kiện tập trung toàn fleet (detection/action/quarantine/timeline).
- Chưa có lớp license/compliance vận hành ở cấp trung tâm.

## 4) Đánh giá chi tiết năng lực KSC/KAS master server

KSC/KAS thường mạnh ở lớp quản trị và vận hành tập trung:

- Inventory toàn hệ thống, trạng thái managed/unmanaged, last seen.
- Policy assignment, inheritance, lock, profile theo OU/group.
- Task orchestration: triển khai, cập nhật, quét, tỷ lệ thành công/thất bại.
- Security event và quarantine ở quy mô toàn fleet.
- Báo cáo xu hướng, audit theo đơn vị, theo thời gian.

Ý nghĩa an ninh:

- Hữu ích cho SOC, quản trị vận hành, kiểm tra tuân thủ, báo cáo lãnh đạo.
- Tăng năng lực điều phối khắc phục đồng loạt.

Giới hạn khi chỉ dựa vào KSC:

- Một số tín hiệu hardening cấp sâu có thể không đầy đủ bằng collector local.
- Có thể có độ trễ đồng bộ giữa thực trạng máy và trạng thái trên server.

## 5) So sánh trực tiếp theo mục tiêu giám sát doanh nghiệp

| Tiêu chí | Agent local hiện tại | KSC/KAS master |
|---|---|---|
| Độ sâu cấu hình OS | Rất cao | Trung bình |
| Hardening chi tiết (registry, auditpol, secedit) | Rất tốt | Hạn chế/không đầy đủ |
| Trạng thái Kaspersky AV/FW | Có (local view) | Có (central view) |
| Quản trị policy theo tổ chức | Không | Rất tốt |
| Điều phối task trên toàn fleet | Không | Rất tốt |
| Sự kiện bảo mật tập trung | Hạn chế | Rất tốt |
| Vòng đời quarantine/incident | Hạn chế | Rất tốt |
| Báo cáo compliance cho lãnh đạo | Hạn chế | Rất tốt |
| Độ tin cậy trạng thái thực tế tại máy | Rất cao | Cao nhưng có độ trễ |
| Độ phức tạp tích hợp ban đầu | Trung bình | Trung bình đến cao |

## 6) Nên phát triển công cụ hay sử dụng KAS như thế nào?

### 6.1. Nếu ưu tiên giảm rủi ro kỹ thuật tại endpoint

Ưu tiên đầu tư Agent khi mục tiêu chính là:

- Phát hiện cấu hình nguy hiểm theo bằng chứng kỹ thuật cụ thể.
- Áp chuẩn hardening nội bộ/CIS xuống từng máy.
- Kiểm soát persistence, service/task/startup bất thường.

Kết luận cho trường hợp này:

- Nên tiếp tục phát triển Agent collector và rule engine.
- Tuy nhiên vẫn cần dữ liệu KSC để không thiếu lớp quản trị tập trung.

### 6.2. Nếu ưu tiên vận hành SOC và quản trị tập trung

Ưu tiên khai thác KSC/KAS khi mục tiêu chính là:

- Theo dõi fleet-level security posture theo group/phòng ban.
- Đo policy compliance tập trung và drift theo thời gian.
- Quản lý task/quarantine/incident lifecycle đồng bộ.

Kết luận cho trường hợp này:

- Cần tích hợp KSC sớm để có dữ liệu điều hành.
- Nhưng không nên bỏ Agent vì sẽ mất chiều sâu endpoint.

### 6.3. Mô hình khuyến nghị cho doanh nghiệp vừa và lớn

Mô hình Hybrid 3 lớp:

- Lớp 1 (Endpoint truth): Agent thu thập local.
- Lớp 2 (Central governance): KSC/KAS đồng bộ về backend.
- Lớp 3 (Correlation): máy chấm điểm và cảnh báo hợp nhất hai nguồn.

Lợi ích thực tế:

- Cảnh báo chính xác hơn (giảm false positive khi có đối chiếu hai chiều).
- Ưu tiên xử lý tốt hơn (host nào vừa lệch hardening vừa lệch policy trung tâm).
- Dễ chứng minh tuân thủ trong audit nội bộ/đối tác.

## 7) Khung giám sát an ninh mạng đề xuất cho máy người dùng công ty

### 7.1. Mục tiêu kiểm soát

- Phát hiện sớm cấu hình yếu và bề mặt tấn công mở.
- Đảm bảo endpoint bám chính sách an ninh thống nhất.
- Theo dõi đầy đủ vòng đời cảnh báo đến khi đóng sự cố.

### 7.2. Nhóm chỉ số vận hành bắt buộc (KPI/KRI)

- Tỷ lệ máy đạt baseline hardening (% compliant).
- Tỷ lệ máy lệch policy KSC theo nhóm/phòng ban.
- Thời gian trung bình xử lý vi phạm critical (MTTR).
- Tỷ lệ endpoint không gửi dữ liệu quá N giờ.
- Tỷ lệ task KSC thất bại lặp lại.

### 7.3. Bộ cảnh báo ưu tiên cao nên có ngay

- Cổng nguy hiểm đang listen và có inbound allow.
- WDigest bật cleartext, SMBv1 bật, NTLM cấu hình yếu.
- Tài khoản local admin bất thường hoặc tăng đột biến.
- Máy "đã managed" trên KSC nhưng không khớp policy mong đợi.
- Máy không cập nhật signature/patch vượt ngưỡng thời gian.

## 8) Lộ trình triển khai khuyến nghị (thực dụng)

### Giai đoạn 1: Chuẩn hóa dữ liệu và định danh (1-2 sprint)

- Chuẩn hóa định danh host: endpoint_id, hostname, FQDN, domain, serial (nếu có).
- Tạo bảng ánh xạ host Agent <-> host KSC.
- Chuẩn hóa severity và taxonomy rule/violation.

Đầu ra:

- Mapping 1-1 đáng tin cậy giữa hai nguồn.

### Giai đoạn 2: Tích hợp KSC/KAS vào backend (2-3 sprint)

- Xây worker đồng bộ định kỳ (incremental sync).
- Nạp 4 nhóm dữ liệu trước: inventory, policy assignment, task status, security events.
- Bổ sung retry, idempotency, watermark thời gian để chống trùng/mất dữ liệu.

Đầu ra:

- Bảng dữ liệu KSC chuẩn hóa, truy vấn được theo host/group/time.

### Giai đoạn 3: Correlation và chấm điểm rủi ro (2 sprint)

- Xây rules đối chiếu:
  - Policy expected (KSC) vs Actual state (Agent).
  - Task expected (KSC) vs Evidence endpoint (Agent).
- Tạo điểm rủi ro hợp nhất theo host và theo nhóm.

Đầu ra:

- Danh sách host ưu tiên xử lý theo tác động thực tế.

### Giai đoạn 4: Vận hành SOC và cải tiến liên tục (1-2 sprint)

- Dashboard điều hành: heatmap rủi ro, top mismatch, trend compliance.
- SOP xử lý sự cố theo mức độ (critical/high/medium).
- Cơ chế "policy feedback": vi phạm lặp lại sẽ đẩy đề xuất sửa policy trung tâm.

Đầu ra:

- Quy trình giám sát và xử lý có thể vận hành ổn định hằng ngày.

## 9) Khuyến nghị quyết định đầu tư

Nếu chỉ được ưu tiên một hướng trong ngắn hạn, thứ tự khuyến nghị là:

1. Giữ và nâng chất Agent hiện tại (vì đây là nguồn sự thật kỹ thuật tại máy).
2. Song song triển khai tích hợp KSC dữ liệu cốt lõi (inventory + policy + task + event).
3. Ưu tiên xây correlation sớm, không đợi hoàn thiện 100% dữ liệu mới bắt đầu.

Lý do:

- Mục tiêu "giám sát và đảm bảo an ninh mạng" cần cả hai: độ sâu kỹ thuật và năng lực quản trị tập trung.
- Hybrid cho hiệu quả tốt nhất về giảm rủi ro thực tế và khả năng điều hành.

## 10) Checklist ra quyết định nhanh cho ban lãnh đạo

Trả lời 5 câu hỏi sau:

1. Doanh nghiệp có cần báo cáo an ninh theo phòng ban/đơn vị và xu hướng theo tháng không?
2. Có cần chứng minh máy tuân thủ chính sách chuẩn trong các kỳ audit không?
3. Có cần theo dõi vòng đời task/quarantine/incident tập trung không?
4. Có cần phát hiện sai lệch hardening sâu ở cấp OS không?
5. Có cần ưu tiên xử lý theo mức độ rủi ro thực tế thay vì theo cảnh báo rời rạc không?

Diễn giải:

- Nếu từ 3 câu "Có" trở lên: bắt buộc đi theo Hybrid.
- Nếu chủ yếu "Có" ở câu 4: ưu tiên đầu tư Agent trước, nhưng vẫn lên kế hoạch tích hợp KSC.
- Nếu chủ yếu "Có" ở câu 1-2-3: ưu tiên tích hợp KSC trước, đồng thời giữ Agent mức tối thiểu.

## 11) Phạm vi dữ liệu: cái gì không lấy được từ KAS và cái gì nên lấy qua KAS

Mục này giúp tránh kỳ vọng sai khi thiết kế hệ thống giám sát.

Ghi chú phiên bản:

- Các nhận định trong mục này áp dụng theo hướng triển khai với KSC/KAS phiên bản 12.8 đang sử dụng.

### 11.1. Dữ liệu thường không thu thập được (hoặc không đủ sâu) từ KAS/KSC

Các dữ liệu dưới đây nên lấy từ Agent local vì KAS thường không có, hoặc có nhưng không đủ chi tiết để điều tra kỹ thuật:

- Chi tiết hardening mức registry/security option của Windows:
  - WDigest `UseLogonCredential`, NTLM `LmCompatibilityLevel`, SMBv1 driver/start type.
  - Winlogon `Shell/Userinit`, `LocalAccountTokenFilterPolicy`, `RunAsPPL`, các cờ LSA.
- Kết quả `secedit` và `auditpol` theo subcategory (mức evidence để đối chiếu chính xác).
- Mapping chi tiết firewall rule với cổng đang listen theo tiến trình tại thời điểm thu thập.
- Danh sách startup persistence, scheduled task ngoài Microsoft, service binary path chi tiết.
- Cấu hình WiFi profile (Open/WEP/WPA/TKIP/CCMP) và đánh giá rủi ro từng SSID.
- Một số dấu hiệu cấu hình bất thường theo ngữ cảnh nội bộ (rule tùy biến) mà KAS không hiểu bối cảnh doanh nghiệp.

Lý do:

- Đây là lớp telemetry gần hệ điều hành, cần thu thập trực tiếp tại endpoint mới đảm bảo độ sâu và tính thời điểm.

### 11.2. Dữ liệu chỉ có thể, hoặc nên lấy qua KAS/KSC

Các dữ liệu dưới đây nên lấy từ KAS/KSC vì bản chất là dữ liệu quản trị tập trung:

- Cây tổ chức quản trị endpoint:
  - Nhóm quản trị, nhánh OU logic trong KSC, trạng thái managed/unmanaged toàn hệ thống.
- Policy governance:
  - Policy gán cho từng nhóm, inheritance, trạng thái khóa policy, profile đang áp dụng.
- Vòng đời tác vụ tập trung:
  - Deploy/update/scan task, lịch chạy, tỷ lệ thành công/thất bại, nguyên nhân lỗi theo task.
- Sự kiện bảo mật tập trung và quarantine lifecycle:
  - Detection, hành động xử lý, cách ly/khôi phục, trạng thái đóng mở sự cố.
- Tình trạng license và sức khỏe vận hành nền tảng Kaspersky:
  - Hạn license, phạm vi cấp phát, trạng thái cập nhật nguồn/repository/distribution point.
- Báo cáo fleet-level cho quản trị và kiểm toán:
  - Xu hướng rủi ro theo thời gian, top nhóm/đơn vị rủi ro, mức tuân thủ theo chính sách.

Lý do:

- Đây là dữ liệu do control plane quản lý; Agent tại máy không thể tái tạo đầy đủ bối cảnh điều phối trung tâm.

### 11.3. Dữ liệu có ở cả hai bên nhưng vai trò khác nhau

Một số trường có thể xuất hiện ở cả Agent và KAS, nhưng nên dùng theo nguyên tắc:

- Trạng thái AV/FW:
  - Agent: dùng để xác nhận thực trạng tức thời tại máy.
  - KAS: dùng cho quản trị tập trung, kiểm tra mức phủ chính sách.
- Trạng thái cập nhật/chữ ký:
  - Agent: bằng chứng tại thời điểm thu thập.
  - KAS: theo dõi xu hướng và xử lý hàng loạt.
- Compliance:
  - Agent: compliance kỹ thuật chi tiết.
  - KAS: compliance điều hành theo nhóm/phòng ban.

Nguyên tắc xử lý khi lệch dữ liệu:

- Dùng Agent làm nguồn xác minh kỹ thuật cuối cùng tại host.
- Dùng KAS làm nguồn sự thật cho bối cảnh quản trị tập trung.
- Đưa cảnh báo "mismatch" thành loại cảnh báo riêng để SOC ưu tiên điều tra.

---

## Ghi chú kỹ thuật

- Đánh giá này dựa trên phạm vi collector hiện có và mô hình dữ liệu KSC/KAS phổ biến.
- Phiên bản KSC/KAS đang dùng: 12.8.
- Với KSC/KAS 12.8, nên xác nhận sớm 5 điểm trước khi chốt backlog tích hợp:
  - Danh sách API/endpoint thực sự được mở trong môi trường hiện tại.
  - Cơ chế xác thực và phân quyền tài khoản tích hợp (service account, scope quyền đọc).
  - Giới hạn tốc độ truy vấn và chiến lược phân trang (pagination) cho dữ liệu fleet lớn.
  - Trường thời gian chuẩn để làm incremental sync (created/updated/event time) nhằm tránh trùng hoặc mất bản ghi.
  - Khả năng truy xuất lịch sử task, event, quarantine và license theo khoảng thời gian.
- Khuyến nghị vận hành với 12.8:
  - Đồng bộ theo lô nhỏ và có checkpoint watermark.
  - Thiết kế cơ chế retry có backoff để tránh quá tải server quản trị.
  - Bắt buộc lưu audit log cho tiến trình đồng bộ để phục vụ truy vết sự cố tích hợp.