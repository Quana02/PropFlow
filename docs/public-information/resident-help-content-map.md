# Bản đồ nội dung FAQ

| Mục hỏi | Nguồn và phạm vi |
| --- | --- |
| Ai có thể đăng ký, vì sao không tìm được hồ sơ | [FE-01 decisions](../requirements/FE-01-Implementation-Decisions.md), [FE-02 onboarding](../requirements/FE-02-Resident-Onboarding-API.md), `ResidentOnboarding`, `ResidentRegistration`. Bốn trường đối chiếu, hồ sơ chưa liên kết và đủ điều kiện. |
| OTP, đăng nhập, khóa, quên và đổi mật khẩu | `AuthController`, `AuthUseCases`, `Login`, `Otp`, `ForgotPassword`, `ChangePassword`. Gửi lại theo trạng thái, recovery cần email đăng nhập. |
| Email đăng nhập, email liên hệ, tên và số điện thoại | [FE-01 API](../requirements/FE-01-API-and-Deployment.md), `VerifyResidentEmail`, `ResidentAccounts`, `Account` và `ResidentsController`. Manager khởi tạo đổi email đăng nhập cư dân, hồ sơ liên hệ thuộc FE-02. |
| Xem hồ sơ/căn hộ, sửa sai và quan hệ | `ResidentApartment`, `ResidentPortalLayout`, `ResidentsController`, `ApartmentManagement`, [FE-03 defaults](../requirements/FE-03-Apartment-Type-Defaults.md). Theo scope. |
| Tạo, theo dõi yêu cầu, ảnh và trao đổi | `ResidentServiceRequestList`, `ResidentServiceRequestDetail`, `ResidentServiceRequestDialog.razor.cs`. Picker cư dân hiện giữ tệp cục bộ nhưng lệnh Create chưa chuyển tệp, nên FAQ hướng dẫn hỏi kênh cung cấp bổ sung. Luồng bằng chứng nhân viên là luồng khác. |
| Chi phí phát sinh và đánh giá | [FE-05 consent](../requirements/FE-05-Additional-Cost-Consent.md), `ResidentAdditionalCostConsent`, `ResidentServiceRequestDetail`. Chỉ khi đủ trạng thái và điều kiện. |
| Sự cố khẩn, liên hệ ngoài giờ | [FE-13 Emergency Voice](../requirements/FE-13-Emergency-Voice.md), `EmergencyCallOverlay`. Tùy người tiếp nhận, micro, kết nối. Giờ/kênh chính thức chờ duyệt. |
| Phản ánh, phân biệt sửa chữa | `ResidentComplaints`, `ComplaintWorkspace`, `ComplaintUseCases`. Theo dõi trạng thái trong mục Phản ánh. |
| Hóa đơn, công nợ, báo chuyển khoản và sai lệch | `ResidentBilling`, `ResidentPaymentDialog`, `PaymentFinanceStore`. Pending chưa giảm công nợ; chọn đúng hóa đơn. |
| Kế toán nhắc phí và người nhận | `FinanceReminderButton`, `BillingCycleController`, `BillingHouseholdDirectory`, `FinanceNoticePublisher`. Backend xác định từ nghĩa vụ, toast chỉ tiếp nhận. |
| Chuông, đánh dấu đã đọc, thông báo thiếu | `NotificationBell`, `ResidentNotificationsController`. Theo actor; kênh email tùy trạng thái và cấu hình. |
| Chat Ban quản lý | [FE-13 chat](../requirements/FE-13-Resident-Manager-Chat.md), `SupportChatWorkspace`. Dùng trang liên hệ trong Portal và chờ Manager đủ điều kiện. |
| Nguồn chatbot, giới hạn và dữ liệu nhạy cảm | [FE-12 tài liệu](../requirements/FE-12-Knowledge-Documents.md), `ResidentChatWidget`, `ResidentChatController`, `GeminiChat`. Kiểm nguồn khi có, quyết định thuộc người có thẩm quyền. |

Đối chiếu [ownership](../database/PropFlow_Database_Ownership_Mapping.md) và [DBML](../database/PropFlow.dbml) cho nguồn và mục đích dữ liệu.
