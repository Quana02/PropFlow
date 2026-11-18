# Báo cáo kiểm tra PropFlow

| Báo cáo | Nội dung |
| --- | --- |
| [Điều kiện demo, pilot và production](release-readiness.md) | Việc đã xử lý, các điều kiện còn mở và tiêu chí kiểm chứng. |
| [Bảo mật, quyền riêng tư và kiểm chứng hồi quy](security-privacy-regression.md) | Quyền truy cập, ảnh bằng chứng, người nhận thông báo tài chính, bản nháp chat và test PostgreSQL. |
| [Kiểm toán phân quyền FE-01–FE-15](propflow-authorization-security-audit.md) | Ma trận route, API, quyền tài nguyên, các bản sửa và bằng chứng hồi quy local. |
| [Authorization Hardening 1.1](propflow-authorization-hardening-1.1-report.md) | Quyền lịch sử bảo trì, phiên HTTP/SignalR, độ phủ fixture và kiểm chứng Release. |
| [Authorization Hardening 1.2](propflow-authorization-hardening-1.2-report.md) | Độ phủ tác động theo endpoint, thu hồi phiên, diễn tập Release và các điều kiện triển khai còn mở. |
| [Kiểm chứng phân quyền cuối kỳ](propflow-final-authorization-coverage-report.md) | Bằng chứng HTTP theo operation, kiểm thử SePay và trạng thái các khoảng trống còn mở. |
| [Hồi quy thông báo realtime](propflow-realtime-notification-regression-report.md) | Đối chiếu recipient, xử lý outbox, browser và phạm vi chưa kiểm. |
| [Rủi ro runtime](propflow-runtime-security-risk-assessment.md) | Phiên bản DLL nạp trong Release và advisory cần xử lý trước lúc triển khai. |
| [Trang thông tin công khai](public-information-verification.md) | Điều khoản, quyền riêng tư, trợ giúp, liên hệ, kết quả kiểm thử và giới hạn đã ghi nhận. |
| [Rà soát pháp lý và quyền riêng tư](legal-privacy-review.md) | Nguồn pháp lý, luồng dữ liệu, bên xử lý và hiện trạng trước triển khai. |
| [Hồi quy giao diện và phản hồi thao tác](frontend-regression.md) | Toast, đăng nhập, dialog, điều hướng, bộ lọc và bố cục quản lý. |

Log, TRX, ảnh và tài liệu làm việc lưu cục bộ trong `artifacts/`. Các bảng báo cáo ghi phạm vi của từng lượt, kể cả phần chưa xác minh.
