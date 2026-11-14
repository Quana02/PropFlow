# Bảo mật, quyền riêng tư và kiểm chứng hồi quy

Báo cáo gộp hai lượt triển khai bảo mật và ổn định hồi quy. Phạm vi gồm xác thực, bảo vệ dữ liệu cư dân, ảnh bằng chứng, thông báo tài chính và bảo toàn bản nháp chat. Số liệu dưới đây thuộc các lượt kiểm thử đã ghi nhận. Phiên bản có bốn trang công khai được kiểm tra riêng trong [báo cáo trang thông tin công khai](public-information-verification.md).

## Các thay đổi đã xác minh

### Nguyên nhân và cách xử lý

| Phạm vi | Nguyên nhân trong mã nguồn |
| --- | --- |
| FE-05 | Upload đã ký nhưng dùng delivery mặc định `image/upload`. Người biết URL cũ có thể đi vòng qua endpoint PropFlow nếu tenant cho phép URL public. Đây là rủi ro ở mã nguồn, chưa xác minh có ảnh thật bị truy cập. Ảnh mới chuyển sang delivery authenticated, API kiểm quyền trước khi tải và trả no-store. |
| FE-02 | Điều kiện tìm kiếm dùng email/phone trước khi tạo projection và tổng số. Việc trả null trong DTO vẫn cho phép suy luận qua kết quả và count. Tìm kiếm và TotalCount dùng cùng phạm vi quyền với projection. |
| ACCOUNTANT | Tìm email cho bản ghi đã gửi dùng địa chỉ trong khi unique index của tài khoản liên kết dùng user ID. Worker trước đây gửi theo địa chỉ ghi lúc xếp hàng. Toast thành công từng gọi enqueue là đã gửi. Luồng hiện dùng ID tài khoản liên kết để dedupe và báo đã tiếp nhận ở UI. |
| FE-13 | Test cũ cố định `alertdialog`. Native `dialog` hiện tại có modal semantics, tên và mô tả truy cập, phù hợp với bước xác nhận do người dùng mở. |

### Quy tắc xác định người nhận

FE-08 chọn chủ sở hữu có hiệu lực ở kỳ tính phí. Khi căn không có chủ sở hữu, directory dùng người đứng đầu hộ đủ điều kiện, gồm head tenant. Directory lọc hồ sơ cư dân ACTIVE và lưu snapshot bên chịu phí theo hóa đơn. Một người có thể nhận thông báo cho nhiều căn hoặc nghĩa vụ độc lập. Người cùng căn chỉ có quan hệ cư trú không tự động thành người nhận. Hóa đơn đã thanh toán đủ bị loại khỏi luồng nhắc. Xác nhận thanh toán lấy người nhận qua hóa đơn và tạo email biên nhận nếu đủ điều kiện. Thông báo web lọc theo recipient user ID. Giá mới, phân công và thông báo vận hành dùng các luồng riêng chỉ ghi thông báo web.

Tài khoản đã liên kết dùng email đăng nhập của đúng user ID còn ACTIVE. Worker phân giải lại khi gửi. Tài khoản không còn đủ điều kiện được hoãn một giờ và sẽ được kiểm tra lại. Bản ghi không có user ID dùng email liên hệ trong snapshot đã duyệt. Việc đổi hoặc thu hồi email snapshot của hồ sơ chưa có tài khoản cần chính sách riêng. Ranh giới giữa truy vấn trạng thái tài khoản và thao tác SMTP vẫn có thể xảy ra tranh chấp. SMTP xác nhận nhận thư chỉ chứng minh máy chủ SMTP chấp nhận, và retry sau khi SMTP đã chấp nhận nhưng DB chưa commit vẫn có thể giao lặp.

## Ổn định môi trường và fixtures kiểm thử

| Nhóm | Phân loại | Bằng chứng và xử lý |
| --- | --- | --- |
| FE-04/07 mã tài sản, công việc và lịch | SCHEMA_MIGRATION | Baseline xác nhận PostgreSQL 42703 trên `maintenance_tasks.is_emergency` và 42P01 trên `facility_types`. Fixture dùng schema chung chưa chạy migration. Các migration cũ được áp dụng trên `propflow_test`. Fixture mã tự sinh nay tạo DB riêng và chạy đúng EF history theo schema. |
| Mã công việc bắt đầu từ 002 | FIXTURE_DATA | Sau khi nâng schema, `Task_GeneratesMonotonicSequenceForSameAssetPrefix` thấy mã còn lại trong DB chung. DB riêng bảo toàn assertion 001–003. Ba ca tạo lịch trước đó đưa tài sản UNDER_MAINTENANCE vào nhánh tạo lịch thông thường. Fixture chuyển sang ACTIVE đúng nghiệp vụ ở commit `19f5d9b`. |
| PostgreSQL teardown | TEST_INFRASTRUCTURE, FLAKY_RACE_CONDITION | Các lượt song song ghi timeout ngay ở `DROP DATABASE … WITH (FORCE)`, nhân lỗi lên mọi ca thuộc fixture FE-12/13, và một ca ResidentEligibility trong BillingCycle. Pool có thể giữ session sau Dispose. Lỗi được quan sát khi các lớp fixture hoặc lượt chạy dùng chung PostgreSQL có thời điểm chồng lấp. Loại pooling cho các fixture bị ảnh hưởng và chạy suites tuần tự theo collection để kiểm chứng. Chưa có snapshot locks đủ để quy nguyên nhân lịch sử cho từng timeout. Không đổi timeout. |
| FE-05 submission outbox | FIXTURE_DATA | Test đưa Title trống và kỳ vọng lỗi. Handler tự sinh tiêu đề từ mô tả từ commit `4956095`. Dùng mô tả trống để tiếp tục kiểm dữ liệu sai không ghi outbox, rollback không phát, và thông báo gửi đúng đối tượng. |
| Kiểm chứng schema đầy đủ | SCHEMA_MIGRATION, TEST_INFRASTRUCTURE | Schema cũ thiếu bảng Billing/Payments/Communication/ServiceRequests và quyền Admin. Áp dụng migrations hiện có. EF Payments dùng API làm startup project vì module không tham chiếu EF Design. Kiểm tra schema sau nâng phát hiện hai bảng emergency voice từ `4956095`. Cập nhật assertions tổng 72 bảng core, 11 bảng Communication và tên hai bảng mới. |
| Hai ca BillingCycle merge loại căn hộ | FIXTURE_DATA | Baseline tái hiện NullReference tại `path!.FullName`. SQL tháng 10 không tồn tại trong lịch sử tracked đang có. Quyết định FE-03 ngày 02/11 dùng preset và bảo toàn loại đã gán, mức phí. Test chạy SQL hiện hành hai lần và kiểm dữ liệu giữ nguyên cho cả mức giá giống nhau và khác nhau. |
| BillingCycle thông báo tài sản trả 409 | FIXTURE_DATA | `EquipmentService` và `FacilityService` từ `19f5d9b` chặn đổi trực tiếp sang UNDER_MAINTENANCE. Test xác nhận 409 và không ghi outbox, rồi gọi luồng FE-07 tạo công việc khẩn cấp với đủ ưu tiên và thời gian. Vẫn kiểm realtime, retry một phần, dedupe và tài khoản ngoài tập nhận. |

Baseline Phase 1 ghi 37 lỗi integration nhưng lượt mới chỉ tái hiện 11 lỗi trên 471 ca. Không có TRX của lượt 37 lỗi để ghép tên từng test. Lượt đầy đủ ban đầu đạt 527/539. Sau khi ổn định schema và fixtures, bộ đầy đủ đạt 539/539.

## Kết quả kiểm thử cuối của lượt ổn định hồi quy

Solution build đạt 0 lỗi và 0 cảnh báo. PostgreSQL integration và BillingCycle dùng [cấu hình tuần tự](../../tests/postgres.runsettings). Các fixture bị ảnh hưởng tạo DB riêng và tắt pooling tại điểm cần dọn DB. Cấu hình này chỉ áp dụng cho môi trường kiểm thử.

| Bộ kiểm thử | Đạt | Trượt | Bỏ qua |
| --- | ---: | ---: | ---: |
| Unit/component | 896 | 0 | 1 |
| SePay PostgreSQL local, chạy riêng ca bỏ qua | 1 | 0 | 0 |
| Architecture | 63 | 0 | 0 |
| JavaScript frontend | 33 | 0 | 0 |
| PostgreSQL integration | 539 | 0 | 0 |
| BillingCycle | 150 | 0 | 0 |

Chrome chạy API, Web và Interactive WASM thật trên PostgreSQL local với dữ liệu giả. Kiểm 19 route thuộc năm vai trò. MANAGER và STAFF đọc ảnh PNG tổng hợp qua API. ACCOUNTANT tìm được hồ sơ bằng mã, không tìm ra qua email liên hệ, và thấy phản hồi tiếp nhận khi gửi hóa đơn hoặc nhắc phí. RESIDENT giữ bản nháp sau Hủy và Escape. Test HTTP xác minh quyền ảnh theo phân công và header `Cache-Control: no-store`. Các kiểm tra browser còn thiếu được tổng hợp trong [điều kiện pilot và production](release-readiness.md).

Kịch bản dialog có thể chạy lại bằng [script Chrome](../../scripts/test-confirmation-dialog.mjs) với fixture HTML do `ConfirmationDialogTests` xuất qua `PROPFLOW_DIALOG_PREVIEW_DIR`. Lượt kiểm thử đã chạy bốn biến thể tại năm kích thước và hai theme, kiểm focus, Escape và backdrop.

## Giới hạn vận hành

- Lượt thử dùng kho ảnh giả. Tenant Cloudinary, khả năng truy cập trực tiếp original/derived, thu hồi, cache CDN và chuyển ảnh public cũ vẫn cần xác minh trước khi dùng dữ liệu thật.
- SMTP loopback xác minh đường gửi. SMTP chấp nhận thư có thể đi trước commit DB, nên retry còn có thể giao lặp. Quy trình thu hồi email snapshot của hồ sơ chưa liên kết vẫn cần chốt.
- Chưa xác minh SePay live, retention, log production hay xử lý orphan khi kết quả commit không xác định. [Điều kiện phát hành](release-readiness.md).
