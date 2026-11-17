# Checklist trước triển khai

Ghi người phụ trách, thời điểm và đường dẫn bằng chứng cho từng mục trước khi mở môi trường. Chủ dự án chốt quyền truy cập và thời điểm thực hiện. Đơn vị vận hành giữ cấu hình, bằng chứng và kế hoạch quay lui.

- [ ] Chốt hosting, tên miền, runtime LTS còn hỗ trợ và topology một API instance. Kiểm phiên bản runtime thực tế trên host.
- [ ] Xác nhận network chỉ mở ingress HTTPS cho Web và API qua proxy. PostgreSQL và cổng ứng dụng chỉ truy cập được từ mạng được phép.
- [ ] Xác nhận proxy chuyển /api/v1 và /hubs về API, giữ 401/403/404, hỗ trợ WebSocket và lược bỏ query string khỏi log hub.
- [ ] Chọn CORS origin chính xác hoặc topology cùng origin. Thử Secure/HttpOnly, cookie Path, CSRF sai và CSRF hợp lệ.
- [ ] Tạo tài khoản DB migration riêng và tài khoản runtime. Xác minh quyền schema, version extension và backup có thể restore.
- [ ] Cấp RSA, OTP key và Data Protection key ring riêng cho môi trường. Xác minh khởi động lại cùng khóa và bảo vệ bản sao khóa.
- [ ] Kiểm manifest của Release artifacts và báo cáo hồi quy cùng source. Theo dõi các hạng mục chưa đủ fixture.
- [ ] Kiểm Cloudinary, SMTP, SePay, Gemini và TURN theo danh sách được bật. Chốt dữ liệu giả, chi phí và người giữ quyền trước mỗi phép thử.
- [ ] Chạy staging smoke với tài khoản cả năm role. Đối chiếu revoke phiên, ảnh sau reassignment, tiền đang chờ và thông báo gửi đúng người.
- [ ] Chốt log redaction, người nhận cảnh báo, quy trình disable tài khoản, quyền backup, SLA nội bộ và kế hoạch rollback.

Quản trị viên cùng chủ dự án đối chiếu các mục chưa đạt trước khi cấp traffic mở rộng. [Báo cáo Hardening 1.2](../audits/propflow-authorization-hardening-1.2-report.md)
