# Hướng dẫn triển khai cùng chủ dự án

Hướng dẫn theo từng lượt bằng tiếng Việt. Trước mỗi bước cần xác nhận mục tiêu, người làm, môi trường, quyền thực hiện và bằng chứng hoàn tất. Chủ dự án thao tác trên tài khoản hosting/provider của mình. Codex chuẩn bị cấu hình, lệnh, đọc kết quả đã che thông tin nhạy cảm và hướng dẫn sửa lỗi trong phạm vi đã được phép.

## 1. Chốt hosting, mạng và phiên bản

- **Mục tiêu và người thực hiện** — Chủ dự án chọn nền tảng với hỗ trợ của Codex, chốt một ingress HTTPS, một process API và một Web host.
- **Điều kiện và vị trí** — Có quyền tài khoản và ngân sách dự kiến. Tại màn hình cấu hình hạ tầng, đối chiếu OS/runtime LTS, khả năng giữ volume, kết nối PostgreSQL private, giới hạn WebSocket và log.
- **Thao tác** — Chốt bản .NET còn hỗ trợ trước khi dùng dữ liệu thật. Xác định tên project, vùng, thư mục publish, quyền tạo service và cơ chế quản lý secret. Khi provider được xác định, Codex sẽ đưa vị trí dashboard và các trường cần điền.
- **Kết quả và xác minh** — Có bản ghi topology, phiên bản runtime, quyền và người chịu trách nhiệm. So phiên bản tại terminal với artifact cần triển khai.
- **Khắc phục và quay lui** — Nếu thiếu WebSocket, volume, truy cập private hoặc runtime phù hợp, giữ môi trường local và chỉnh phương án trước khi mở traffic. Quyền truy cập nên được cấp ở phạm vi nhỏ nhất.

## 2. DNS, HTTPS, PostgreSQL và kho khóa

- **Mục tiêu và người thực hiện** — Chủ dự án cấu hình DNS/TLS theo hướng dẫn. DBA tạo database và tài khoản. Đơn vị vận hành cấp khóa.
- **Điều kiện và vị trí** — Có tên miền và máy staging, cấu hình proxy đã được duyệt, database trước deploy có backup. Thao tác tại DNS dashboard, secret store, PostgreSQL và terminal host.
- **Thao tác** — Tạo record staging tới ingress đã xác nhận. Gắn chứng chỉ TLS cho đúng hostname và lên lịch gia hạn. Phân tuyến /api/v1, /hubs và Web. Tạo runtime identity và migration identity, cấp cấu hình API theo tài liệu, kiểm backup/restore trước khi migrate. [Tham chiếu cấu hình](deployment-configuration-reference.md) · [Migration và phục hồi](database-migration-and-recovery.md)
- **Kết quả và xác minh** — Hostname nhận đúng chứng chỉ. 401/403/404 đi qua proxy. WebSocket nâng cấp thành công. Tất cả context báo `pending=0`.
- **Khắc phục và quay lui** — Nếu proxy trả HTML 200 thay lỗi API, đối chiếu quy tắc route và fallback. Nếu xác thực mất sau restart, kiểm volume key ring và issuer. Hạ DNS staging hoặc tạm đóng ingress trong lúc xử lý, áp dụng kế hoạch phục hồi PostgreSQL đã thử.

## 3. Provider và triển khai staging

- **Mục tiêu và người thực hiện** — Chủ dự án cấp credentials staging và xác nhận thực hiện từng phép thử. Codex hướng dẫn cấu hình và đối chiếu kết quả.
- **Điều kiện và vị trí** — API/Web Release đã publish và có nguồn dữ liệu giả. Thực hiện tại secret store, dashboard provider và giao diện PropFlow staging.
- **Thao tác** — Cấp từng provider theo checklist, khởi động API trước Web, kiểm TLS và smoke với năm role. Chỉ bật từng tích hợp sau phép thử local và khi có dữ liệu thử nghiệm phù hợp. [Kiểm chứng provider](external-provider-verification.md) · [Runbook](authorization-hardening-runbook.md)
- **Kết quả và xác minh** — Các hành trình tối thiểu có bằng chứng status, log đã lọc, ảnh giao diện và dữ liệu kiểm thử. Kiểm logout nhiều tab, restart có và không có phiên đã thu hồi, ảnh FE-05/07, hóa đơn và notification.
- **Khắc phục và quay lui** — Khi một tích hợp lỗi, đóng flow bị ảnh hưởng, giữ cấu hình hoạt động cuối và đối chiếu callback/TLS/permission. Chủ dự án chốt mọi thay đổi có thể phát sinh phí, thư thật hoặc giao dịch.

## 4. Vận hành và nghiệm thu

- **Mục tiêu và người thực hiện** — Chủ dự án cùng đơn vị vận hành ký nhận preflight và phân công người trực.
- **Điều kiện và vị trí** — Có dashboard log đã lọc, lịch backup và cách liên hệ khi có sự cố.
- **Thao tác và xác minh** — Định kỳ thử restore, lên lịch cập nhật runtime, kiểm lỗi 401/403, worker retry, WebSocket disconnect và cảnh báo finance. Ghi lại các thao tác có tác động trước rollout.
- **Khắc phục và quay lui** — Giữ artifacts và hash của bản đang chạy, runbook dừng ingress, phương án sửa tiến và backup khôi phục. [Checklist](deployment-preflight-checklist.md)

Khi bắt đầu triển khai thật, chủ dự án gửi tên nhà cung cấp, hệ điều hành và topology dự kiến. Dùng ảnh đã che token hoặc mô tả chữ để đối chiếu. Với mỗi bước, Codex sẽ cung cấp lệnh cụ thể, trường cần điền, thao tác dashboard, kết quả mong đợi và cách xử lý khi kết quả khác dự kiến.
