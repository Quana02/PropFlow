# Hạng mục tích hợp cần kiểm chứng trên staging

Chủ dự án giữ quyền trên dashboard. Đơn vị vận hành cấp cấu hình qua secret store và chạy phép thử với dữ liệu giả. Dùng log đã lược bỏ credential và chỉ thử từng provider sau khi có môi trường staging được duyệt.

| Provider | Chuẩn bị và vị trí | Kết quả cần kiểm | Khắc phục và quay lui |
| --- | --- | --- | --- |
| Cloudinary | Chọn product environment staging, cấp API key/secret phía API, đối chiếu authenticated delivery | Upload ảnh tổng hợp, tải qua người được phép, chặn URL ngoài phạm vi, xác minh TTL và việc thu hồi quyền | Kiểm delivery type, quyền key và URL đã ký. Tắt sử dụng flow ở staging khi không bảo vệ được ảnh, giữ dữ liệu gốc |
| SMTP | Chọn server gửi và hộp thư thử nghiệm, đặt hostname/port, From và credential ở API. Xác nhận STARTTLS | OTP và mail outbox vào đúng hộp thư thử nghiệm, lỗi tạm có retry, log tránh PII | Kiểm TLS, quyền From, quota và DNS mail theo provider. Tạm dừng gửi staging khi sai recipient |
| SePay | Chủ dự án chọn cơ chế sandbox/ứng dụng OAuth, cấu hình callback HTTPS và webhook riêng cho staging theo runbook | Challenge đúng actor, callback khớp, webhook hợp lệ và replay idempotent, kiểm công nợ bằng giao dịch thử nghiệm được phép | Kiểm redirect và quyền ứng dụng, giữ feature flag ở trạng thái tắt khi chưa xác minh |
| Gemini | Chủ dự án chọn project staging và giới hạn quota, cấp credential của API, xác nhận model và ngân sách phù hợp | Request tổng hợp hoạt động, lỗi quota có thông báo an toàn, nội dung prompt và key không xuất hiện ở client/log | Kiểm quyền project/model, tắt các feature AI đang thử nếu quota hoặc credential lỗi |
| TURN/WebRTC | Chọn TURN staging, xác nhận URL relay, cổng và secret phía API, dùng hai thiết bị/mạng thử nghiệm | Kết nối relay và credential ngắn hạn, hết quyền dừng signaling/media, reconnect đúng phạm vi | Kiểm firewall, cấu hình relay/TLS và thời gian máy. Hạn chế thử nghiệm cuộc gọi nếu relay chưa ổn định |

Ghi người chạy, ngày giờ, phiên bản Release, tiêu chí đạt và việc cần xử lý sau mỗi bước. Chưa có tài khoản triển khai được chọn trong Hardening 1.2. [Cloudinary](https://cloudinary.com/documentation/control_access_to_media) · [Gemini](https://ai.google.dev/gemini-api/docs/api-key) · [SePay](../sepay-live-deployment.md)
