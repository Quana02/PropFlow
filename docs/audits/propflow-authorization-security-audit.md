# Kiểm toán phân quyền PropFlow

Mốc nguồn là `0859534`, cùng các thay đổi sẵn có ở worktree ngày 15/11/2026. Kiểm toán giữ thay đổi tab bảo trì, breadcrumb yêu cầu dịch vụ và tài liệu nghiên cứu chưa track. Phạm vi gồm FE-01 đến FE-15, các trang công khai và điểm vào kỹ thuật. Chạy trên PostgreSQL loopback với dữ liệu giả. Provider đám mây và môi trường production nằm ngoài lượt kiểm chứng.

Mức sẵn sàng và bằng chứng bổ sung sau hardening được duy trì ở [báo cáo 1.2](propflow-authorization-hardening-1.2-report.md). PF-AUTHZ-03/04/06 được đánh giá theo từng tiêu chí trong báo cáo đó.

## Phạm vi và cách đọc kết quả

| Bề mặt | Kiểm kê | Kết quả chi tiết |
| --- | ---: | --- |
| Razor routes | 65 khai báo gồm route client, trang lỗi Web host và trang từ chối | [Ma trận route theo vai trò](propflow-route-authorization-matrix.md) |
| MVC API | 247 cặp method/route gồm alias, đối chiếu OpenAPI thực thi | [Ma trận API theo vai trò](propflow-api-authorization-matrix.md) |
| Realtime | 7 đường dẫn hub, gồm alias thông báo | [Ma trận tài nguyên và điểm vào khác](propflow-resource-authorization-matrix.md) |
| Ownership, field privacy, trạng thái, file và workers | FE-01–FE-15 | [Ma trận tài nguyên](propflow-resource-authorization-matrix.md) |
| Build, test, Chrome, các trường hợp chưa xác minh | Theo từng lượt chạy | [Báo cáo hồi quy](propflow-authorization-regression-report.md) |

Trong 65 route có 51 route bảo vệ, 12 route công khai và 2 route kỹ thuật. Trong 247 cặp method/route có 236 cặp yêu cầu Bearer, 10 cặp auth public có điều kiện riêng và 1 webhook xác thực theo cấu hình. Tìm thấy 4 sai lệch đã tái hiện, gồm 1 Medium và 3 Low trong phạm vi kiểm chứng. Các hạng mục chưa xác minh được ghi riêng ở cuối báo cáo.

Nguồn nghiệp vụ gồm đặc tả Word FE-01–15, [hợp đồng/quyết định bổ sung](../requirements/README.md), [DBML](../database/PropFlow.dbml), [ownership mapping](../database/PropFlow_Database_Ownership_Mapping.md) và quy tắc tác nhân trong repo. Báo cáo Phase 1, 1.1 và 2 hiện được lưu thành [báo cáo bảo mật](security-privacy-regression.md) và [báo cáo trang công khai](public-information-verification.md). Các bản gốc còn lưu cục bộ trong `artifacts/audit-originals`. Phần handoff riêng không có ở đường dẫn được nêu trong yêu cầu.

## Kiến trúc phân quyền

- Web layer cung cấp shell WASM chung cho direct URL. Metadata auth phía server của thành phần WASM được bỏ ở điểm map shell. `AuthBootstrap` phục hồi session trước khi `AuthorizeRouteView` khởi tạo thành phần được phép. URL bị từ chối có thể vẫn nhận HTTP 200 cho shell. Bằng chứng route là nội dung sau khi WASM chạy và các request do trang gửi.
- API dùng Bearer RS256, yêu cầu issuer/audience hợp lệ và kiểm trạng thái ACTIVE, email, role, permission hiện hành. Các cấu hình auth public, antiforgery, challenge và webhook là ngoại lệ theo contract. Ma trận ACL dùng đúng năm role cố định, với permission tương ứng từng policy.
- Lớp API quyết định role và permission trước. Service tiếp tục kiểm quan hệ actor–resource, người nhận hoặc trạng thái khi hợp đồng yêu cầu. Phạm vi tòa nhà là deployment hiện hành. Quyền tổng hợp của ADMIN không cấp quyền vào business workflow của các role khác.
- Client giữ access token trong bộ nhớ. Cookie refresh và CSRF quản lý vòng đời session. WebSocket đã xác thực, JWT HTTP còn hạn và refresh cookie có cơ chế hết hiệu lực khác nhau. Ma trận tài nguyên ghi các trường hợp còn cần xác minh.
- Swagger mở trong Development/Testing. Các route thông tin công khai vẫn nằm ngoài role nghiệp vụ. API files và local storage đi qua các service; URL công khai Cloudinary legacy có ranh giới riêng.

## Phát hiện và xử lý

### PF-AUTHZ-01 · Medium · REALTIME_AUTHORIZATION_GAP · FE-07

- **Actor và bề mặt** STAFF từng được giao việc, trên `/hubs/maintenance` và method `JoinTask`.
- **Kỳ vọng** Sau khi gỡ assignment, actor không tiếp tục nhận tín hiệu cập nhật của nhiệm vụ. MANAGER và STAFF hiện được giao vẫn nhận sự kiện cần thiết.
- **Tái hiện** Kết nối hai STAFF và một MANAGER. STAFF A tham gia group task trong lúc được giao. Chuyển assignment sang B, phát cập nhật chỉ định B. Trước sửa, A vẫn nhận ID của task trước marker riêng được gửi sau đó.
- **Nguyên nhân và tác động** Hub kiểm có bản ghi assignment bất kỳ lúc join. Notifier phát tới group task lâu dài, độc lập với danh sách người nhận hiện hành. STAFF A quan sát được ID công việc và thời điểm hoạt động sau khi quyền đã bị thu hồi.
- **Bản sửa** Group task dành cho MANAGER theo policy Manage. STAFF nhận sự kiện qua group cá nhân, với người nhận được xác định bởi luồng phân công. Hub yêu cầu policy View khi kết nối và đóng theo thời hạn token. Client STAFF dùng subscription theo user. Sự kiện chuyển giao hiện hữu dùng để gỡ dữ liệu khỏi màn hình được gửi theo luồng phân công.
- **Bằng chứng** `MaintenanceRealtime_StopsSendingTaskEventsToFormerAssignee` trượt trước bản sửa và đạt sau bản sửa. Ca có hai đối chứng dương cho MANAGER, STAFF B và marker xác nhận kết nối của A vẫn hoạt động. [Hub](../../src/Modules/Maintenance/Presentation/Hubs/MaintenanceHub.cs) · [Notifier](../../src/Modules/Maintenance/Presentation/Hubs/MaintenanceNotifier.cs) · [Kiểm thử](../../tests/PropFlow.IntegrationTests/Fe04StaffHttpTests.cs).

### PF-AUTHZ-02 · Low · FILE_ACCESS_CONTROL_GAP · FE-07

- **Actor và bề mặt** STAFF gọi trực tiếp URL evidence hoặc activity của công việc chỉ được giao cho STAFF khác. MANAGER và STAFF đủ quyền tải cùng một ảnh qua hai URL khác nhau.
- **Kỳ vọng** 401 khi thiếu đăng nhập, 403 khi STAFF ngoài phạm vi, 404 khi ghép ID ảnh không khớp. Response tải ảnh thành công yêu cầu `Cache-Control: no-store`.
- **Tái hiện và tác động** Test HTTP trên ảnh giả và liên kết task/result/attachment hợp lệ cho thấy STAFF ngoài phạm vi nhận 500. Service đã từ chối trước khi đọc file, nhưng action bỏ qua bước ánh xạ lỗi. Cả hai action tải ảnh thiếu chỉ thị no-store. Chưa có bằng chứng dữ liệu ảnh bị đọc bởi STAFF ngoài phạm vi.
- **Bản sửa và đối chứng** Ánh xạ lỗi phạm vi và lỗi thiếu ảnh sang 403/404, sử dụng ánh xạ hiện có cho activity và thêm no-store cho hai URL ảnh. Test kiểm 401/403/404, số lần kho ảnh được mở và nội dung tải đúng của MANAGER/STAFF được giao. [Controller](../../src/Modules/Maintenance/Presentation/MaintenanceController.cs) · [Kiểm thử](../../tests/PropFlow.IntegrationTests/Fe04StaffHttpTests.cs) · [TRX sau sửa](../../artifacts/authz/authorization-fixes.trx).

### PF-AUTHZ-07 · Low · SPECIFICATION_CONFLICT · FE-09/FE-15

- **Bề mặt** `POST /api/v1/payment-settings/sepay/oauth/challenge/CONNECT`. Bước xin OTP yêu cầu ADMIN cùng permission `MANAGE_PAYMENT_DESTINATION`.
- **Tái hiện** Probe Chrome và test HTTP dùng đúng URL cho thấy 404 trước khi policy chạy, kể cả ADMIN đúng quyền. MVC dùng `action` làm tên giá trị route dành riêng để chọn action. Đặt tham số nghiệp vụ trùng tên khiến `CONNECT` được dùng để định tuyến action.
- **Bản sửa** Đổi tên tham số template và tham số action thành `operation`. Chuỗi URL cụ thể client gửi và hai điều kiện role/permission giữ nguyên.
- **Kiểm thử** Ca mới tái hiện 404 trước sửa. Kỳ vọng sau sửa là 401 cho ẩn danh, 403 cho sai role hoặc thiếu permission, và phản hồi nghiệp vụ `sepay_oauth_not_configured` cho ADMIN đúng quyền trong fixture tắt provider. [Controller](../../src/Modules/Payments/Presentation/SePayOAuthController.cs) · [Kiểm thử](../../tests/PropFlow.IntegrationTests/FinanceWorkflowHttpTests.cs).

### PF-AUTHZ-08 · Low · FIELD_LEVEL_DATA_LEAK · FE-12

- **Actor và bề mặt** STAFF xem list hoặc detail `/api/v1/knowledge/documents` với quyền đọc tài liệu đang xuất bản.
- **Tái hiện** MANAGER xuất bản tài liệu, STAFF xem metadata, MANAGER lưu bản nháp chưa xuất bản rồi STAFF đọc lại. Tiêu đề và nội dung vẫn từ bản xuất bản, nhưng token chỉnh sửa và thời điểm cập nhật thay đổi. Sắp xếp list cũng dùng thời điểm ghi bản nháp.
- **Bản sửa** List và detail dùng chung projection. STAFF nhận thời điểm của phiên bản được hiển thị và token rỗng, list sắp xếp theo cùng thời điểm. MANAGER/ADMIN đủ quyền vẫn nhận token phiên bản mới nhất để sửa và xuất bản.
- **Kiểm thử** So sánh list/detail của STAFF trước và sau khi lưu nháp, đồng thời xác nhận token chỉnh sửa của actor quản lý vẫn đổi. [Service](../../src/Modules/AiChatbot/Infrastructure/KnowledgeDocuments.cs) · [Unit](../../tests/PropFlow.UnitTests/KnowledgeDocumentTests.cs) · [PostgreSQL](../../tests/PropFlow.IntegrationTests/KnowledgeDocumentsTests.cs) · [TRX trước sửa](../../artifacts/authz/knowledge-metadata-red.trx).

## Vấn đề mở

| ID | Mức đánh giá | Ghi nhận |
| --- | --- | --- |
| PF-AUTHZ-03 | ĐÃ TRIỂN KHAI HARDENING 1.1 | Baseline từng cho người tham gia trước tải ảnh kết quả muộn. Chính sách mới giữ quyền lịch sử theo từng đợt tham gia, vẫn cho quyền bàn giao với phân công hiện hành. [Kết quả kiểm chứng](propflow-authorization-hardening-1.1-report.md) |
| PF-AUTHZ-04 | ĐÃ TRIỂN KHAI HARDENING 1.1 · CÒN KIỂM CHỨNG | Baseline JWT HTTP kiểm account và quyền hiện hành nhưng vẫn trả 200 sau logout. Đợt 1.1 gắn SID vào JWT và kiểm PostgreSQL cho HTTP, invocation và dispatch trên SignalR. [Kết quả kiểm chứng](propflow-authorization-hardening-1.1-report.md) |
| PF-AUTHZ-05 | NOT VERIFIED · PROVIDER_NOT_VERIFIED | Cloudinary legacy original/derived CDN, cache và revoke, SMTP thật, TURN và SePay live. Giữ các hạng mục R01–R03 nêu trong yêu cầu kiểm toán. |
| PF-AUTHZ-06 | CÓ TIẾN TRIỂN · TEST_COVERAGE_GAP | Baseline có probe từ chối và cross-account. Đợt 1.1 bổ sung fixture có payload hợp lệ, đối chứng thành công và kiểm trạng thái, cùng công cụ chạy lại. Độ phủ sâu còn mở theo [báo cáo 1.1](propflow-authorization-hardening-1.1-report.md). |

## Kết luận nghiệm thu

Đọc kết quả thực thi và các điều kiện chưa đủ trong [báo cáo hồi quy](propflow-authorization-regression-report.md). Trạng thái sẵn sàng demo local, pilot dữ liệu thật và production được đánh giá riêng theo bằng chứng của từng môi trường.
