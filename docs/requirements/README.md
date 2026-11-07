# Tài liệu yêu cầu PropFlow

Các file Word trong thư mục là đặc tả feature. Các file Markdown dưới đây gom contract, quyết định bổ sung đã chốt và hướng dẫn triển khai. Không suy diễn đề xuất hoặc kết quả test thành yêu cầu đã được phê duyệt.

| Tài liệu | Nội dung |
| --- | --- |
| [FE-01 — API và triển khai](FE-01-API-and-Deployment.md) | Điều kiện đăng ký, OTP/session, bảo mật, API, secrets, migration và vận hành |
| [FE-02 — Onboarding cư dân](FE-02-Resident-Onboarding-API.md) | Định danh, ownership/residency, household, ngày hiệu lực và giao dịch |
| [FE-03 — Loại căn hộ và defaults](FE-03-Apartment-Type-Defaults.md) | CRUD, validation, tự điền, migration và bộ năm loại mẫu có nguồn tham khảo |
| [FE-15 — Quản trị và nhật ký](FE-15-Administration.md) | Nhật ký hệ thống, filter popup, audit/privacy, giao diện và phần cấu hình còn cần chốt |

## Quyết định bổ sung so với đặc tả cũ

- **FE-01 (03/11/2026):** chỉ owner hiện hành hoặc head TENANT tại căn không có owner hiện hành được tự đăng ký. Không áp dụng cách hiểu cũ “mọi active residency đều đủ điều kiện”. Điều kiện đăng ký không cấp thêm quyền nghiệp vụ.
- **FE-03 (02/11/2026):** Manager quản lý loại căn hộ và defaults; năm loại mẫu không phải fixed catalog. Thay defaults không ghi đè căn đã lưu.
- **FE-15.9 (04/11/2026):** nhật ký mở rộng sang các thao tác quan trọng qua website/API, giữ lịch sử cũ; không phải log mọi lần xem trang/click hoặc mọi thay đổi DB bên ngoài.

Các điểm đã được người dùng chốt ở trên bổ sung cho Word cũ. Các phần còn thiếu contract phải được chốt trước khi triển khai.

## FE-08/FE-09 — Kết luận rà soát tài liệu

Đợt chuẩn hóa trước đã thống nhất catalog FE-08 gồm 12 mục, actor matrix của năm role, workflow, thuật ngữ và ranh giới module trong hai đặc tả Word. Các kết luận cần giữ:

- Xác thực thuộc FE-01, fixed RBAC thuộc FE-15, business-scope check thuộc feature và được thực thi phía server; không có dynamic RBAC hoặc Building selector/cross-condominium filter.
- FE-08 sở hữu phí/hóa đơn; FE-09 sở hữu payment/debt. UI chỉ cập nhật kết quả tài chính sau xác nhận của backend; pending/rejected payment không giảm dư nợ.
- Chi tiết screen item, message/error ID, request/response, cột DB, field validation, mặc định search/sort và chuyển màn hình chưa được Report 1 chốt; cần thiết kế chi tiết/API contract đã duyệt. Không tự đặt các giá trị này từ checklist.
- Tài liệu tham chiếu không có chuẩn tiếng Nhật, revision-history table, footnote hoặc appendix cần bổ sung. Chuẩn hóa tài liệu không phải nghiệm thu implementation FE-08/09.

## Phạm vi kiểm chứng và việc còn lại

- Các báo cáo Audit/Recheck ngày 02/11 và Fixes ngày 03/11 đã được gộp vào tài liệu theo feature. Kết luận eligibility cũ đã bị thay thế; không dùng lại để đánh giá FE-01.
- Đợt 03/11 sau làm rõ FE-01 ghi nhận 213 integration, 11 unit đăng ký đạt; đợt tổng quát trước đó ghi nhận 116 unit và 62 architecture đạt. Đợt audit 04/11 ghi nhận 233 integration, 34 unit và 62 architecture đạt. Đây là các run/phạm vi khác nhau, không cộng thành một tổng test hiện tại hoặc quy đổi thành phần trăm hoàn tất.
- Lỗi multi-residency/household lifecycle FE-02 và CRUD/defaults FE-03 đã được sửa trong các đợt đó; kiểm chứng cụ thể xem tài liệu feature. Sửa điều hướng 05/11 được xác nhận trên WebAssembly thật, gồm lần bấm đầu và Back/Forward; kịch bản ở `docs/testing`.
- **FE-15.8 còn cần chốt phạm vi/contract và triển khai:** cấu hình thanh toán riêng không thay thế cấu hình hành chính chung. Các key hỗ trợ email/phone chỉ là đề xuất, chưa phê duyệt.
- Bộ tài liệu rà soát trước chưa có `PropFlow_15FE.docx` và SRS Report 3 tổng hợp; không tự tạo consolidated UC ID. Truy vết FE-01 dùng FE-01.1–FE-01.7 theo đặc tả hiện có.
- Chưa có bằng chứng nghiệm thu đầy đủ SMTP thật, cookie/CORS đa tab, toàn bộ browser matrix năm actor và production deployment. Cần cấu hình secrets/HTTPS/proxy/database/log sink theo môi trường; security log retention 30 ngày do hạ tầng vận hành quản lý, email queue không bền qua restart.
- Cần contract business date/timezone thống nhất nếu UI phải hoạt động đúng trên máy người dùng ở múi giờ khác. Data-quality item ownership history P302 của `RES-000002` được báo cáo ngày 03/10 chưa có xác nhận xử lý trong bộ tài liệu này; không tự xóa lịch sử.

## Cách duy trì tài liệu

Sửa trực tiếp tài liệu feature tương ứng khi có quyết định mới; không tạo thêm Audit/Recheck/Report trùng nội dung. Bằng chứng test nằm trong artifact của từng lần chạy; `.codex-tmp` là dữ liệu local ignored, không phải tài liệu bắt buộc trên máy khác. Việc gộp tài liệu ngày 05/11/2026 không chạy lại test nghiệp vụ hoặc thay đổi source/database.
