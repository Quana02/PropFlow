# PropFlow Authorization Hardening 1.1

Ngày cập nhật 16/11/2026. Phạm vi là mã nguồn và môi trường kiểm thử cục bộ của một chung cư PropFlow.

Phần kiểm thử bổ sung, diễn tập và mức sẵn sàng hiện tại được ghi ở [báo cáo Hardening 1.2](propflow-authorization-hardening-1.2-report.md).

## Kết quả và baseline

| Finding | Baseline | Thay đổi |
| --- | --- | --- |
| PF-AUTHZ-03 | STAFF từng tham gia có thể nằm trong phạm vi tải ảnh của kết quả phát sinh sau khi rời nhóm | Chính sách đợt tham gia áp dụng ở lịch sử, tiến độ, kết quả và tệp. Chi tiết hiện hành giữ quyền bàn giao cho người được giao |
| PF-AUTHZ-04 | HTTP kiểm trạng thái/quyền hiện tại. Test tái hiện JWT trả 200 sau logout. SignalR trên kết nối đã mở cần xác minh | SID và trạng thái PostgreSQL, cùng kiểm nối/gọi/gửi SignalR và đóng transport theo phiên. Web kiểm tra khi mất kết nối và đồng bộ logout cùng phiên giữa các tab |
| PF-AUTHZ-06 | 1.073 probe từ chối và 28 probe chéo tài khoản trong đợt cũ | Inventory và ma trận kỳ vọng có thể chạy lại từ repo. Fixture bảo trì và tài khoản dùng payload hợp lệ, có actor đủ quyền và đối chiếu tác động |

Baseline hồi quy gốc gồm 939 Unit, 64 Architecture, 36 JavaScript, 549 Integration, 150 Billing và 378 kiểm tra route Chrome. Các số này là kết quả đợt trước, giữ làm mốc so sánh. [Báo cáo hồi quy](propflow-authorization-regression-report.md)

## Thiết kế và tác động

- FE-07 lọc phạm vi trước phân trang. Dữ liệu cũ giữ nguyên và không backfill. View STAFF cho xem từng đợt, chỉ gọi API ảnh khi mở ảnh. Kiểm tra parent và quyền chạy trước đọc storage. Tín hiệu làm mới cho người vừa bị gỡ không chứa mã task. [Quyết định FE-07](../requirements/FE-07-Historical-Access.md)
- FE-01 dùng hàng refresh đang hoạt động theo `(user_id, session_id)` và dấu vân tay quyền. Migrate thu hồi hàng legacy, bổ sung hai cột nullable và index partial. JWT legacy bị từ chối và phải đăng nhập lại. FE-15 chia sẻ giao dịch PostgreSQL khi đổi vai trò/trạng thái và đồng bộ với login/refresh qua cùng khóa tài khoản. [Quyết định FE-01](../requirements/FE-01-Implementation-Decisions.md#thu-hồi-phiên-đăng-nhập)
- SignalR sáu hub type và bảy đường dẫn. API dùng lifetime manager và hub filter công khai của .NET 8. Registry đếm kết nối và lần đóng do thu hồi. Dispatch xác minh quyền theo lô tối đa 256 kết nối, mỗi lô dùng truy vấn RBAC và truy vấn phiên/tài khoản. Logic phân người nhận tài nguyên vẫn chạy tại từng feature. Mục tiêu 2 giây server và 3 giây phía Web cần đo trong các fixture tương ứng. Dữ liệu đã vào hàng đợi transport trước commit có thể đến client sau commit. [SignalR authentication](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-8.0) · [Hub filters](https://learn.microsoft.com/en-us/aspnet/core/signalr/hub-filters?view=aspnetcore-8.0) · [SignalR configuration](https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration?view=aspnetcore-8.0)
- Kiểm tra quyền theo tài nguyên và vòng đời phiên theo nguyên tắc OWASP. Khả năng revoke trực tiếp trên một API instance phù hợp deployment hiện tại. [OWASP Authorization](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html) · [OWASP Session Management](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html) · [Microsoft Entra CAE](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation)

## Checkpoint kiểm thử

| Giai đoạn | Bằng chứng hiện có | Cần xác minh |
| --- | --- | --- |
| Baseline | Đối chiếu báo cáo trước với 65 route và 247 API action/alias. Chạy lại ma trận Chrome | Bổ sung các tình huống ngoài fixture hiện có |
| Lịch sử | 133/133 test bảo trì ở checkpoint này, gồm A → B → A → gỡ. Có kiểm tra HTTP lịch sử và trước khi mở kho ảnh | Kiểm tra delivery của Cloudinary trong môi trường tích hợp |
| Phiên HTTP | Red test kỳ vọng 401 nhận 200. Sau khi gắn SID và migration, 83/83 test tập trung đạt. Test upgrade xác nhận hàng refresh cũ được giữ và đánh dấu thu hồi | Theo dõi rollout thực tế và thay đổi mapping quyền qua migration |
| Realtime | Logout đóng sáu WebSocket thuộc cùng phiên RESIDENT và giữ socket của thiết bị khác. Đổi role hoặc disable đóng WebSocket bảo trì của STAFF. Guard kiểm đúng bảy URL và quyền endpoint | Ma trận mọi nguyên nhân thu hồi tại mọi hub, đặc biệt cuộc gọi WebRTC đang hoạt động, reconnect và event bị hụt |
| Fixture | 9/130 endpoint thuộc nhóm POST, PUT, PATCH, DELETE có fixture được xác nhận trong manifest. Gồm start, progress, multipart result, đổi role, disable, gửi hóa đơn, nhắc phí, thu tiền và xác nhận thanh toán | Bổ sung fixture payload hợp lệ và đối chiếu side effect cho các operation còn mở |
| Công cụ | Inventory khớp 65 route và 247 API, test architecture kiểm allowlist; có launcher Chrome và fixture PostgreSQL dùng lại | Bổ sung fixture cho route nhiều dữ liệu đặc thù |
| Triển khai | Publish Release API/Web, chạy trên HTTPS với môi trường Production cục bộ. Giữ key ring và xác minh restart | Reverse proxy của môi trường đích, provider và tải nhiều kết nối |

## Đo và kiểm chứng cục bộ

- Logout và đóng sáu WebSocket cùng phiên trong một ca tích hợp mất 33,8 ms. Thiết bị thứ hai nhận được marker sau logout. Chrome Release đo một lần từ bấm logout đến khi tab hai rời workspace là 987 ms, bearer cũ trả 401. Đây là số đo của fixture cục bộ. Trang có hub nhận biết qua transport hoặc tín hiệu sang tab. Trang chưa có hub đang mở xác minh lại khi thực hiện request bảo vệ tiếp theo. [Bằng chứng WebSocket](../../artifacts/authz/hardening-boundaries-final.trx) · [Bằng chứng Chrome](../../artifacts/authz/browser-session-lifecycle.json)
- Sau restart API và Web, bearer còn phiên trả 200, bearer đã thu hồi trả 401 và refresh với cookie/CSRF từ trước restart trả 200. 30 mẫu `GET /auth/me` tuần tự qua HTTPS loopback trên Windows 10.0.26200, 12 logical processors cho p50 10,15 ms, p95 14,99 ms và max 19,57 ms. Có hai truy vấn đọc trạng thái theo cấu trúc xác thực hiện hành. Chưa có baseline cùng cấu hình hoặc kết quả tải đồng thời của worker/dispatch. [Bằng chứng restart và phân vị](../../artifacts/authz/release-restart-smoke.json)
- Quét `--vulnerable --include-transitive` hoàn tất cho solution và project BillingCycle, chưa báo gói dễ tổn thương từ nguồn đã cấu hình.

## Kiểm thử hồi quy và phạm vi

| Bộ | Kết quả | Bằng chứng |
| --- | --- | --- |
| Build | Solution Debug và publish Release riêng API/Web thành công | `artifacts/authz/build` và `artifacts/authz/web-build` |
| Unit | 939 đạt trong suite, 1 ca SePay chạy riêng với PostgreSQL fixture và đạt | [Unit](../../artifacts/authz/hardening-Unit-verified.trx) · [SePay local](../../artifacts/authz/hardening-sepay-local.trx) |
| Architecture / JavaScript | 64/64 và 36/36 | [Architecture](../../artifacts/authz/hardening-Architecture-verified.trx) · `node --test tests/frontend/*.test.cjs` |
| Integration | 557/557, chạy lại 8/8 ca tập trung về phiên, realtime và bảo trì sau thay đổi cuối Backend | [Suite](../../artifacts/authz/hardening-integration-verified.trx) · [Tập trung](../../artifacts/authz/hardening-boundaries-final.trx) |
| Billing | 150/150, chạy lại 6/6 ca thông báo trễ và thao tác tài chính sau kiểm tra thêm outbox | [Suite](../../artifacts/authz/hardening-billing-verified.trx) · [Tập trung](../../artifacts/authz/hardening-notice-final.trx) |
| Chrome | 378 kiểm tra route, 1.073 probe từ chối, 35 probe JWT, 28 probe chéo tài khoản đạt | [Route](../../artifacts/authz/browser-matrix.json) · [HTTP](../../artifacts/authz/api-matrix.json) · [JWT](../../artifacts/authz/jwt-matrix.json) · [Cross-account](../../artifacts/authz/horizontal-matrix.json) |
| Lịch sử trên Chrome Release | Giữ thứ tự tab và tab mặc định. Nhiệm vụ đã gỡ chỉ hiện trong lịch sử, hai đợt hợp lệ hiển thị và nội dung ngoài phạm vi bị ẩn. Kiểm cả chế độ sáng/tối thật ở 390, 768 và 1440 px | [Kết quả](../../artifacts/authz/hardening-history-browser.json) · [Ảnh chế độ tối 390 px](../../artifacts/authz/hardening-history-390-dark.png) |
| Mutation fixture | 9/130 (6,92%) trong manifest. Snapshot tùy operation gồm trạng thái, kết quả, ảnh, event, email hoặc hiệu lực phiên | [Độ phủ](../../artifacts/authz/hardening-mutation-coverage.json) · [Cách chạy](../../scripts/authz/README.md) |

Suite và ca tập trung có phần trùng nhau. Probe từ chối trong Chrome dùng dữ liệu tối thiểu và đo lớp bảo vệ HTTP. Fixture valid payload dùng cùng dữ liệu cho actor đủ quyền và actor bị từ chối, rồi đối chiếu thay đổi có thể phát sinh.

## Điểm vào chính

| Phạm vi | Mã nguồn |
| --- | --- |
| FE-07 history và tệp | [MaintenanceService](../../src/Modules/Maintenance/Application/MaintenanceService.cs) · [Trang STAFF](../../src/PropFlow.Web.Client/Features/Maintenance/Pages/MyMaintenanceTasks.razor) |
| Phân công trễ | [OperationalNoticeDispatcher](../../src/PropFlow.Api/Composition/OperationalNoticeDispatcher.cs) |
| Phiên | [Login/refresh/logout](../../src/Modules/Authentication/Application/Sessions/Sessions.cs) · [State validation](../../src/Modules/Authentication/Infrastructure/AccessTokenStateValidator.cs) |
| WebSocket | [Connection registry](../../src/PropFlow.Api/Composition/SessionConnections.cs) · [Lifetime manager](../../src/PropFlow.Api/Composition/SessionHubLifetimeManager.cs) |
| Đồng bộ Web | [AuthSession](../../src/PropFlow.Web.Client/Services/Authentication/AuthSession.cs) |

## Trạng thái sẵn sàng

Môi trường local đã chạy API/Web Release và xác minh phiên qua restart. Nghiệm thu pilot còn cần proxy TLS/WebSocket tại môi trường đích, kiểm provider và bổ sung các ca thu hồi trong cuộc gọi đang hoạt động. Nghiệm thu production còn cần phần fixture mutation tồn đọng, đo tải dự kiến và hoàn thành các bước bàn giao. Mọi thao tác DB và deployment trong đợt này thực hiện trên fixture local. Tiến trình API, Web và Chrome do kịch bản tạo đã dừng, cổng HTTPS kiểm thử đã được giải phóng.

Chi tiết cấu hình, kiểm tra sau triển khai và rollback nằm trong [runbook](../deployment/authorization-hardening-runbook.md).
