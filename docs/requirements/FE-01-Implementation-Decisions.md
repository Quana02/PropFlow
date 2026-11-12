# FE-01 — Quyết định triển khai

ADMIN, MANAGER, STAFF và ACCOUNTANT tiếp tục tự đổi email. RESIDENT liên hệ Ban quản lý để khởi tạo yêu cầu và tự hoàn tất xác minh. Phạm vi MANAGER là hồ sơ FE-02 trong một deployment chung cư. Authentication đọc liên kết qua Residents.Contracts và kiểm tra vai trò qua Administration.Contracts.

## Vận hành đổi email cư dân

1. MANAGER gặp cư dân tại quầy Ban quản lý. Đối chiếu người xuất trình, họ tên, loại và số giấy tờ gốc với hồ sơ FE-02. Nếu hồ sơ thiếu, không khớp hoặc không xác định được người xuất trình, dừng yêu cầu và chuyển sang quy trình xử lý hồ sơ FE-02 của Ban quản lý.
2. MANAGER mở **Quản lý cư dân → Tài khoản cư dân → Xem chi tiết**. Xác nhận username với cư dân, nhập địa chỉ mới do cư dân cung cấp, chọn lý do và xác nhận đã đối chiếu trực tiếp. Dùng lý do có sẵn để tránh đưa giấy tờ hoặc dữ liệu nhạy cảm vào nhật ký.
3. Cư dân mở **Xác minh email theo yêu cầu BQL** từ menu Resident Portal trên thiết bị của mình. Cư dân tự nhập địa chỉ mới, mật khẩu hiện tại và OTP từ hộp thư mới. MANAGER hướng dẫn thao tác và không yêu cầu cư dân đọc, chuyển tiếp hoặc nhập mã trên thiết bị của MANAGER.
4. Nếu mã hết hạn hoặc đã hết số lần thử, MANAGER khởi tạo lại sau thời gian chờ. Nếu cư dân không thể đăng nhập hoặc cung cấp mật khẩu, xử lý qua quy trình khôi phục FE-01 đang có và chuyển ca không đủ bằng chứng cho Ban quản lý xử lý ngoài chức năng này. Xác nhận tại quầy không thay thế mật khẩu hoặc OTP.

## Tra cứu và xử lý sự cố

Authentication lưu nhật ký nghiệp vụ tại `auth.privileged_action_challenges` với purpose `MANAGED_RESIDENT_EMAIL_CHANGE`. Các trường `requested_by`, `user_id`, `resident_id`, `reason`, `created_at`, `used_at`, `expires_at` và `outcome` cho phép tra cứu người khởi tạo, đích, lý do, thời điểm và kết quả. Bản ghi chỉ được tạo sau khi MANAGER xác nhận đối chiếu trực tiếp. `PENDING` qua thời hạn được đọc là hết hạn. Các kết quả đã đóng gồm `VERIFIED`, `SUPERSEDED`, `ATTEMPTS_EXHAUSTED`, `INELIGIBLE`, `EMAIL_CONFLICT` và `DELIVERY_FAILED` khi hàng đợi từ chối gửi. Lỗi SMTP sau khi đã xếp hàng chỉ xuất hiện trong log giao nhận đã lược bỏ nội dung. Tra cứu phục vụ vận hành chỉ chọn các trường nhật ký nêu trên, theo quyền truy cập dữ liệu và thời hạn lưu hiện hành. Hash thử thách và hash OTP phục vụ xác minh, không đưa vào báo cáo.

Email tài khoản và email liên hệ FE-02 có thể khác nhau. Quy trình giữ nguyên liên kết và dữ liệu căn hộ. [API và triển khai](FE-01-API-and-Deployment.md#đổi-email-cư-dân-theo-yêu-cầu-ban-quản-lý)
