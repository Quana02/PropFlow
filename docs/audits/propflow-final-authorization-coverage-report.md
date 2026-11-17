# PropFlow — Kiểm chứng phân quyền cuối kỳ

Checkpoint bắt đầu ngày 16/11/2026 từ HEAD `0859534` và worktree đã có Hardening 1.1/1.2 cùng SDK audit. Inventory chạy lại khớp 65 route, 247 cặp HTTP method/route và 7 đường dẫn SignalR với 6 hub. Mẫu số làm việc vẫn là 133 operation có tác động. Baseline 32 VERIFIED, 100 PARTIALLY VERIFIED và 1 NOT VERIFIED.

## Checkpoint sau các ca HTTP

| Nhóm | Test và thay đổi | Trạng thái |
| --- | --- | --- |
| FE-09 webhook SePay | `SePay_http_requires_valid_provider_credentials_and_settles_concurrent_delivery_once`, API key và HMAC qua endpoint MVC với PostgreSQL cô lập. | Hai mode PASS, có lỗi ghi payment tổng hợp và retry. |
| FE-13 read/read-all | `Notification_api_paginates_newest_first_and_updates_only_the_current_user`, thêm alias, 401/404 và snapshot read state. | Hai alias PASS, cả hai thao tác chạy trên từng alias. |
| FE-01 refresh và logout | `Rotation_rejects_replay_and_logout_preserves_other_devices`, kiểm snapshot token chain khi thiếu CSRF và replay. | PASS. |

Runner Chrome qua proxy HTTPS dùng task tổng hợp đã seed và hành động nghiệp vụ hợp lệ qua API. Xem bằng chứng giao nhận, giới hạn phép thử và hướng dẫn chạy lại ở [báo cáo realtime](propflow-realtime-notification-regression-report.md) và [hướng dẫn script](../../scripts/authz/README.md). Các phần còn thiếu được ghi theo operation trong manifest và theo hub trong bảng bên dưới. [Báo cáo 1.2](propflow-authorization-hardening-1.2-report.md)

## Độ phủ và phạm vi SePay

| Mức | Baseline 1.2 | Bằng chứng lượt này |
| --- | ---: | ---: |
| VERIFIED | 32 | 39 |
| PARTIALLY VERIFIED | 100 | 94 |
| NOT VERIFIED | 1 | 0 |
| NOT APPLICABLE | 114 | 114 |

39/133 tương ứng 29,32% tại thời điểm trước B1. Hai nhánh webhook đều phải có TRX PASS khớp chính xác trước khi tính VERIFIED. Manifest giữ từng route, action, bộ đối chứng đã xác minh, probe và phần còn thiếu. Mức PARTIALLY VERIFIED tiếp tục mở kể cả khi probe chặn route đã đạt. [Manifest trước B1](../../artifacts/authz/b1-mutation-coverage-before.json) · [Fixture mappings](../../scripts/authz/mutation-cases.json)

| Nhóm theo inventory | VERIFIED / áp dụng | Còn một phần |
| --- | ---: | ---: |
| FE-01 | 4/17 | 13 |
| FE-02 | 1/6 | 5 |
| FE-03 | 7/8 | 1 |
| FE-04 | 0/10 | 10 |
| FE-05 | 1/14 | 13 |
| FE-05/10 | 1/1 | 0 |
| FE-06 | 0/7 | 7 |
| FE-07 | 4/13 | 9 |
| FE-08 | 6/6 | 0 |
| FE-09 | 4/20 | 16 |
| FE-10 | 1/2 | 1 |
| FE-11 | 0/2 | 2 |
| FE-12 | 0/5 | 5 |
| FE-13 | 6/18 | 12 |
| FE-15 | 4/4 | 0 |

Webhook kiểm thiếu/sai key hoặc chữ ký, HMAC quá hạn, malformed JSON, amount phân số, reference không khớp, sai tài khoản, giao dịch cũ đến muộn, bốn request đồng thời, payload xung đột cùng ID, lỗi insert payment, retry và replay. Khoản xác nhận chỉ được ghi một lần và số dư về 0. Không có giao dịch ngân hàng hoặc SePay live. Ca dùng ASP.NET TestServer chạy middleware MVC thật và PostgreSQL, worker processing được gọi có chủ đích để tránh phụ thuộc thời gian timer. Còn mở timeout/DB outage tại lúc nhận HTTP, hành vi provider live và tất cả mức phân bổ sai số lượng. [Webhooks test](../../tests/PropFlow.BillingCycleTests/SePayWebhookHttpTests.cs) · [TRX tập trung](../../artifacts/authz/final-billing-focused.trx)

## PF-AUTHZ-04 theo điểm vào SignalR

| Hub path | Bằng chứng transport/session hiện có | Phần còn mở |
| --- | --- | --- |
| `/hubs/maintenance` | Proxy WebSocket và Long Polling, bearer cũ nhận 401, role/status trong Integration. | Mọi nguyên nhân thu hồi × inbound/outbound riêng, gián đoạn DB. |
| `/hubs/service-requests` | TestServer socket đóng khi logout cùng phiên, quyền tài nguyên của các watcher. | Thu hồi do email/password, role, reconnect và sự kiện outbound khi permission mất. |
| `/hubs/announcements` | TestServer transport và invalidation rỗng, logout cùng phiên. | Kiểm tổ hợp nguyên nhân thu hồi và audience browser. |
| `/hubs/resident-notifications` | TestServer logout, không gửi marker private cho phiên revoked, thiết bị khác còn nhận. | Lifecycle khác logout và missed dispatch trên alias này. |
| `/hubs/notifications` | TestServer logout và batch 255/256/257, real Chrome kiểm giao nhận. | Password/email/disable với UI và transient reconnect. |
| `/hubs/support-chat` | TestServer logout, negotiate theo policy. | Ca chat đang hoạt động, revoke theo tài nguyên, reconnect, typing/draft. |
| `/hubs/emergency-calls` | Cuộc gọi đã xác nhận media qua hub, revoke giữa cuộc gọi, giữ lịch sử. | Ma trận nguyên nhân khác, âm thanh/TURN qua mạng thực. |

Lỗi ứng dụng đã tái hiện nằm ở dispatcher FE-05 gửi trễ cho assignee cũ. Bản sửa và bằng chứng red/green ghi ở [báo cáo realtime](propflow-realtime-notification-regression-report.md). Phần guard, role và route của inventory giữ nguyên.

## Hồi quy trên source cuối

SHA-256 của snapshot `src` và `tests` theo path và nội dung là `f3db038a743f2baafb5cf516eee22490b17cd2e53a53b149b8c653cf4d1876da`. Release DLL API `2AD27258F8C2EF4C88D322F03A48CE76F67EB792FDAE3B582DB033224F91061E` và Web `8D2F0D2BB8ECC5625E743CEA49D42C80B89EDEB23480046D69ACCA0C3C1C28E3`. Cấu hình SDK, package và migration hiện hữu giữ nguyên.

| Bộ | Kết quả | Bằng chứng |
| --- | --- | --- |
| Solution Debug | Build thành công, 0 lỗi | `dotnet build PropFlow.sln --no-restore` |
| Unit | 940 PASS, 0 FAIL, 0 SKIP | [TRX](../../artifacts/authz/final-unit-fixed.trx) |
| Architecture | 64 PASS, 0 FAIL, 0 SKIP | [TRX](../../artifacts/authz/final-architecture-fixed.trx) |
| Integration | 561 PASS, 0 FAIL, 0 SKIP | [TRX](../../artifacts/authz/final-integration-fixed.trx) |
| BillingCycle | 153 PASS, 0 FAIL, 0 SKIP | [TRX](../../artifacts/authz/final-billing-fixed.trx) |
| JavaScript | 36 PASS | [TAP](../../artifacts/authz/final-js-regression.log) |
| Ma trận Chrome và HTTP | 378 route, 1.073 probe từ chối HTTP, 35 JWT, 28 tài nguyên chéo. Tất cả PASS | [Route](../../artifacts/authz/browser-matrix.json) · [HTTP](../../artifacts/authz/api-matrix.json) · [JWT](../../artifacts/authz/jwt-matrix.json) · [Tài nguyên chéo](../../artifacts/authz/horizontal-matrix.json) |
| Release | API/Web publish. Restart, HTTPS, WebSocket và Long Polling local đạt. 50 phiên STAFF, 500 HTTP, 500 invocation, 50 socket đóng theo đúng phiên | [Tải proxy](../../artifacts/authz/hardening-12-proxy-load-50.json) · [Runtime](../../artifacts/authz/final-loaded-runtimes.json) |
| Chrome notification | FE-07, FE-05 và FE-09, actor ngoài phạm vi, read/read-all, đối soát đọc giữa hai tab, phục hồi offline, thu hồi đúng SID và giao sự kiện mới cho phiên độc lập của cùng tài khoản | [Bằng chứng](../../artifacts/authz/final-notification-browser.json) |
| Migrations | 14/14 `pending=0` trên database fixture mới | [Status](../../artifacts/authz/final-migration-status.log) |
| Backup/restore | Dump và restore PostgreSQL fixture local, snapshot khớp và 14 context ở đích đều `pending=0` | [Bằng chứng](../../artifacts/authz/final-database-recovery.json) |

Đã xác minh cleanup sau hồi quy. Cổng test đã giải phóng, không còn tiến trình audit, thư mục Chrome/key/ảnh fixture hay DB tạo cho lượt này. Hai database BillingCycle có từ trước vẫn hiện diện. [Bằng chứng cleanup](../../artifacts/authz/final-cleanup.json)

## Còn mở tại mốc trước B1

- 94 operation cần thêm đối chứng phù hợp, gồm payload, actor, resource hoặc snapshot. Ca tiếp theo là `Recovery_proof_is_single_use_and_password_changes_revoke_sessions`. Dùng challenge thật của fixture với OTP sai và hợp lệ, đối chiếu số lần thử, reset proof và refresh chain. Sau đó ưu tiên chuyển trạng thái FE-05 và các thao tác cấu hình, đối soát FE-09 theo từng dòng `PARTIALLY VERIFIED` trong manifest. Giữ ma trận nguồn actor độc lập với code được kiểm.
- Thử trạng thái đang dùng chat/call trong nhiều lý do thu hồi, reconnect transient trong browser, notification đến từ các nhóm còn mở và retry khi DB gián đoạn. Kiểm theo nhóm trong [ma trận thông báo](propflow-notification-interaction-matrix.md).
- Cho phép rehearsal và demo biệt lập bằng fixture. Pilot/production còn chặn vì runtime 8.0.33, độ phủ phân quyền, môi trường đích và các provider chưa kiểm chứng. Giữ nguyên checklist Cloudinary, SMTP, SePay, Gemini, TURN và approvals. Khi triển khai thật, hướng dẫn chủ dự án bằng tiếng Việt từng bước tại hosting/provider được chọn. [Đánh giá runtime](propflow-runtime-security-risk-assessment.md) · [Hướng dẫn triển khai](../deployment/README.md)

## B1 — FE-01 Authentication và Account Management

Mốc kiểm kê 4/17 VERIFIED với 17 operation thuộc mẫu số hiện hành, gồm endpoint cấp dữ liệu CSRF. Các endpoint GET chỉ đọc ngoài CSRF được giữ theo phân loại hiện có. Tiêu chí đối chứng lấy từ hợp đồng FE-01 và quyết định thu hồi phiên đã duyệt. Các thư gọi ra được chặn ở mail stub. Ảnh đại diện đi qua HTTP multipart với storage fixture trong bộ nhớ. [Quyết định FE-01](../requirements/FE-01-Implementation-Decisions.md) · [Hợp đồng API](../requirements/FE-01-API-and-Deployment.md)

| Operation dưới `/api/v1/auth` | Trước | Ca đối chứng và tác động kiểm tra |
| --- | --- | --- |
| GET `csrf` | PARTIALLY VERIFIED | Cookie Secure/HttpOnly, cặp hợp lệ vào được login, thiếu token hoặc token từ cookie khác không cấp refresh session. |
| POST `login` | PARTIALLY VERIFIED | Credential sai, tài khoản không tồn tại, disabled, pending và lockout. Kiểm phản hồi và refresh chain, actor/quyền/SID cấp từ trạng thái có thẩm quyền. |
| POST `register` | PARTIALLY VERIFIED | Cùng hồ sơ sai một trường rồi đúng cả bốn; challenge, mail, duplicate và chưa liên kết trước OTP. |
| POST `registration/resend` | PARTIALLY VERIFIED | Credential sai, cooldown, hồ sơ thật, challenge cũ bị hủy, challenge mới lưu và số lần gửi qua stub. |
| POST `registration/verify` | PARTIALLY VERIFIED | Challenge đúng chủ sở hữu hoặc chủ hộ thuê đủ điều kiện, OTP sai/hết hạn/replay, liên kết đúng một tài khoản và trạng thái hồ sơ khác. |
| POST `password/forgot` | PARTIALLY VERIFIED | Email có tài khoản hợp lệ, disabled, không tồn tại và cooldown. Chỉ tài khoản hợp lệ có challenge và mail. |
| POST `password/verify` | PARTIALLY VERIFIED | OTP sai, hết hạn, replay và OTP hợp lệ. Đối chiếu số lần thử, thời điểm xác minh và việc tạo proof. |
| POST `password/reset` | PARTIALLY VERIFIED | Proof sai, ghép challenge khác, hết hạn, replay, sử dụng đồng thời. Đối chiếu hash, proof, hai phiên đích và tài khoản khác. |
| POST `password/change` | PARTIALLY VERIFIED | Anonymous, bearer đã thu hồi, mật khẩu hiện tại sai, mật khẩu mới lỗi, trường đích tự chèn. Đổi đúng caller, thu hồi các phiên của caller. |
| POST `email/change` | VERIFIED | Bốn role nội bộ, có mật khẩu và CSRF, tạo OTP tới email đích, email đăng nhập giữ nguyên đến bước xác minh. |
| POST `email/verify` | VERIFIED | Bốn role nội bộ. Email đổi sau OTP đúng chủ, refresh/bearer cũ bị chặn. |
| POST `resident-accounts/{residentId:guid}/email-change` | PARTIALLY VERIFIED | Manager và bốn role đối chứng trên hồ sơ tồn tại, xác minh tại quầy và CSRF. Chỉ tạo challenge khi được phép. |
| POST `resident-email-change/verify` | PARTIALLY VERIFIED | Challenge của cư dân khác, sai mật khẩu/OTP, replay và phiên đã thu hồi. Đối chiếu email đăng nhập, liên hệ, audit và refresh chain. |
| PUT `me` | PARTIALLY VERIFIED | Cập nhật tên/điện thoại bằng bearer hiện hành. Trường định danh, quyền, trạng thái, email và hash giữ nguyên. |
| POST `me/avatar` | PARTIALLY VERIFIED | PNG hợp lệ, actor sai, bearer đã thu hồi, MIME/signature/kích thước sai, thay ảnh và chỉ dọn key cũ của chính caller. Route xóa avatar riêng không có trong inventory. |
| POST `refresh` | VERIFIED | CSRF, rotation và replay, SID ổn định, refresh đồng thời logout. |
| POST `logout` | VERIFIED | Cookie/SID đúng, revoke nhóm phiên hiện hành qua mọi lần rotation, phiên độc lập giữ hoạt động. |

Checkpoint HTTP tập trung đã đạt 90/90 gồm các ca FE-01, SignalR theo phiên và FE-05 liên quan. Hai ca dựng dữ liệu ban đầu lỗi khóa ngoại do actor fixture chưa có trong DB. Đã dùng actor có thật trong fixture và thử lại thành công. Các thay đổi B1 đến checkpoint nằm trong test, chưa có thay đổi implementation. [TRX tập trung](../../artifacts/authz/b1-fe01-focused.trx)

Checkpoint cuối của login kiểm riêng bộ giới hạn request qua một test host cấu hình ngưỡng thấp. Request hợp lệ sau khi đạt ngưỡng bị 429 và refresh chain giữ nguyên. Chrome Release xác nhận đăng nhập/đăng xuất STAFF, hai tab cùng SID, phiên độc lập, truy cập route bảo vệ sau logout và đọc thông báo sau thu hồi phiên. Đối chứng Chrome anonymous đạt 63/63, gồm `/terms`, `/privacy`, `/help` và `/contact`. Bootstrap OTP, recovery, email và avatar dùng HTTP/PostgreSQL với mail/storage fixture. [TRX login](../../artifacts/authz/b1-login-rate.trx) · [Chrome session](../../artifacts/authz/b1-hardening-12-browser-proxy-session.json) · [Chrome unread](../../artifacts/authz/b1-notification-read-state.json) · [Chrome routes](../../artifacts/authz/b1-browser-public.json)

### Kết quả B1

| Chỉ số | Trước | Sau |
| --- | ---: | ---: |
| FE-01 VERIFIED | 4/17 | 17/17 |
| FE-01 PARTIALLY VERIFIED | 13 | 0 |
| Toàn hệ thống VERIFIED | 39/133 | 52/133 |
| Toàn hệ thống PARTIALLY VERIFIED | 94 | 81 |

Cả 17 dòng ở bảng B1 khớp test PASS trong TRX cuối. Các dòng ngoài FE-01 giữ evidence trước B1. Không phát hiện lỗi implementation đáp ứng tiêu chí tái hiện trong phạm vi này. [Manifest B1](../../artifacts/authz/b2-mutation-coverage-before.json) · [Mapping](../../scripts/authz/mutation-cases.json)

Solution Debug build đạt 0 lỗi và 0 cảnh báo. Publish Release API/Web đạt, với cảnh báo NU1900 ở publish Web khi truy cập NuGet advisory. Smoke HTTPS, WebSocket và Long Polling đạt cùng runner Chrome. Bộ hồi quy gồm 940 Unit, 64 Architecture, 36 JavaScript, 568 Integration và 154 BillingCycle từ project build riêng, tổng **1.762 PASS, 0 FAIL, 0 SKIP**. Integration tăng 7 ca. B1 giữ nguyên framework, SDK, runtime, EF Core, Npgsql, schema và migrations. [Unit](../../artifacts/authz/b1-unit.trx) · [Architecture](../../artifacts/authz/b1-architecture.trx) · [JavaScript](../../artifacts/authz/b1-js.log) · [Integration](../../artifacts/authz/b1-integration.trx) · [BillingCycle](../../artifacts/authz/b1-billing.trx) · [Proxy smoke](../../artifacts/authz/b1-hardening-12-proxy-load-1.json)

Đối chiếu cleanup xác nhận 5 cổng thử nghiệm không còn listener, các profile Chrome và khóa tạm đã dọn, DB fixture của B1 không còn và 9 mục DB có trước được giữ. FE-01 không còn operation mở trong mẫu số B1. Chặn phát hành công khai do runtime vẫn theo đánh giá trước. [Đánh giá runtime](propflow-runtime-security-risk-assessment.md)

## B2 — FE-09 Payment & Debt Management

Mẫu số sau B1 giữ 20 operation FE-09 gồm 4 đã VERIFIED và 16 cần đối chứng. B2 dùng bearer cấp qua login thật, PostgreSQL cô lập, mail stub, VietQR fixture và fake HTTP SePay. Khởi tạo OAuth, callback và cấu hình webhook chạy qua controller thực. Các ca chọn tài khoản dùng OAuth với ID được ràng buộc theo kết nối. [Hợp đồng SePay](../sepay-setup.md) · [Fixture HTTP](../../tests/PropFlow.IntegrationTests/PaymentAuthorizationHttpTests.cs)

Checkpoint đầu đạt đích nhận tiền, assign/dismiss và report-transfer. Đích thủ công trả 409 `sepay_selection_required` cho ADMIN có quyền theo hợp đồng đã duyệt, còn actor ngoài quyền bị từ chối trước action. Deactivate xác minh OTP đúng actor, đích và phiên bản. Assign ghi quyết định rồi để inbox xử lý theo nghiệp vụ. Dismiss giữ lịch sử giao dịch và không tạo payment. Hai thao tác đối soát cạnh tranh trên cùng revision chỉ có một thao tác thành công. Report-transfer chỉ tạo PENDING đúng người và hóa đơn. [TRX checkpoint](../../artifacts/authz/b2-focused-first.trx)

Lượt đầu phát hiện lỗi dựng fixture khi response giả thiếu trường trạng thái SePay, đăng ký typed client của connection probe sai kiểu, ngưỡng request OTP bị dùng hết trong bài thử, và hai hóa đơn fixture trùng khóa kỳ. Các trường hợp này được chỉnh tại test fixture trước vòng đối chứng tiếp theo.

Sau khi chỉnh fixture, đối chứng chặn mã chuyển khoản dùng lại trên hóa đơn khác tái hiện HTTP 500. PostgreSQL từ chối ghi bởi unique index `IX_payments_reference_number`, nhưng PaymentFinanceStore chưa chuyển lỗi này thành kết quả nghiệp vụ cho resident submit. Bản sửa chỉ xử lý đúng unique index đó và trả 409 `transfer_reference_exists`. Test kiểm toàn bộ snapshot liên quan sau lỗi, gồm payment, số dư, dữ liệu hóa đơn, history, outbox và thông báo. [TRX trước sửa](../../artifacts/authz/b2-focused-red.trx) · [TRX sau sửa](../../artifacts/authz/b2-focused-green.trx) · [Mã sửa](../../src/Modules/Payments/Infrastructure/Finance/PaymentFinanceStore.cs)

| Operation dưới `/api/v1` | Actor | Trước | Đối chứng |
| --- | --- | --- | --- |
| PUT `payment-settings/destination` | ADMIN | Một phần | ADMIN nhận 409 theo hợp đồng chọn SePay bắt buộc. Actor khác và phiên revoked bị chặn. Không đổi lịch sử hoặc đích. |
| POST `payment-settings/destination/change-challenge` | ADMIN | Một phần | Challenge DEACTIVATE khớp đích/phiên bản, email verified nhận OTP, actor bị chặn không tạo challenge hoặc mail. |
| POST `payment-settings/destination/deactivate` | ADMIN | Một phần | Từ chối OTP sai, hết hạn, khác Admin và replay. Tắt đúng đích, giữ hướng dẫn đã ghim, hóa đơn và payment. |
| GET `payment-settings/sepay/accounts` | ADMIN | Một phần | Provider giả chỉ được gọi sau guard, hai tài khoản hiển thị dạng che số. Không đổi đích hoặc connection. |
| POST `payment-settings/sepay/accounts/change-challenge` | ADMIN | Một phần | Có OTP cho đúng tài khoản provider và phiên bản. Actor bị chặn không gọi provider hoặc gửi OTP. |
| PUT `payment-settings/sepay/accounts/destination` | ADMIN | Một phần | Không nhận ID ngoài kết nối, ghép OTP tài khoản khác, sai chủ, hết hạn hoặc replay. Lưu phiên bản mới và giữ hướng dẫn của hồ sơ cũ. |
| POST `payment-settings/sepay/accounts/preview` | ADMIN | Một phần | QR tạo cục bộ cho tài khoản hợp lệ, số được che, không commit. Actor bị chặn không gọi provider. |
| POST `payment-settings/sepay/connection/check` | ADMIN | Một phần | Phản ánh fake success và failure, actor bị chặn không gọi provider. Connection và đích giữ nguyên. |
| POST `payment-settings/sepay/oauth/callback` | ADMIN | Một phần | State sai, hết hạn, sai chủ và replay không exchange. Fake failure không lưu token; exchange thành công lưu bản bảo vệ và không trả token. |
| POST `payment-settings/sepay/oauth/challenge/{operation}` | ADMIN | Một phần | CONNECT và WEBHOOK có OTP, action lạ bị từ chối, actor ngoài quyền không tạo challenge hoặc gửi mail. |
| POST `payment-settings/sepay/oauth/start` | ADMIN | Một phần | OTP cùng actor và action tạo state dùng một lần, lưu hash và redirect đã cấu hình. Proof khác actor, sai action, hết hạn hoặc replay không tạo state. |
| POST `payment-settings/sepay/oauth/webhook` | ADMIN | Một phần | Sai chủ/sai OTP/replay không gọi provider. Fake failure không ghi webhook. Thành công ghi ID và đích nối với tài khoản đã chọn. |
| POST `payments/bank-transfers/{id:guid}/assign` | ACCOUNTANT | Một phần | Role/phiên sai, hóa đơn thiếu instruction, vượt dư nợ hoặc vượt số tiền không ghi. Worker xác nhận và thông báo đúng một lần. |
| POST `payments/bank-transfers/{id:guid}/dismiss` | ACCOUNTANT | Một phần | Cạnh tranh với assign chỉ một thắng theo revision. Lưu actor và kết quả dismiss, giữ lịch sử, không thay công nợ. |
| POST `resident/payments` | RESIDENT | Một phần | Chặn hộ khác, chưa liên kết, phiên sai, hóa đơn đã trả, vượt dư nợ. Payload tự khai status/owner không có hiệu lực. Replay một payment và một thông báo PENDING đúng hộ. |
| POST `resident/service-requests/{serviceRequestId:guid}/payment/report-transfer` | RESIDENT | Một phần | Kiểm đúng hộ và invoice yêu cầu, từ chối chưa có hóa đơn/đã hủy/đã thanh toán. Cùng lúc chỉ một báo cáo PENDING; trạng thái yêu cầu và công nợ giữ nguyên. |
| POST `payments/{id:guid}/confirm` | ACCOUNTANT | VERIFIED | Hồi quy xác nhận đúng số tiền, đúng cư dân và receipt một lần. |
| POST `payments/{id:guid}/reject` | ACCOUNTANT | VERIFIED | Hồi quy lý do từ chối đúng người, công nợ nguyên vẹn. |
| POST `payments/collect` | ACCOUNTANT | VERIFIED | Hồi quy thu đúng hóa đơn, ghi một payment và gửi một receipt. |
| POST `payments/webhooks/sepay` | Provider API key/HMAC | VERIFIED | Hồi quy hai chế độ xác thực, duplicate đồng thời và retry sau lỗi PostgreSQL. |

Checkpoint HTTP sau sửa đạt 6/6 ca, bao phủ 16 operation mới. Cộng 4 operation đã có bằng chứng, mốc theo nhóm là 7/20 sau đích nhận tiền, 12/20 sau tài khoản/kết nối, 16/20 sau OAuth, 18/20 sau đối soát và 20/20 sau hai luồng cư dân. Kiểm chứng trên PostgreSQL fixture và provider giả. [TRX tập trung](../../artifacts/authz/b2-focused-green.trx) · [Mapping](../../scripts/authz/mutation-cases.json)

Chrome Release kiểm nộp thanh toán chờ xác minh, kế toán từ chối, chống gửi trùng và thông báo đúng hộ. Smoke HTTPS cùng origin, WebSocket, Long Polling và logout đa tab đều đạt. Cấu hình ADMIN/SePay, OAuth và đối soát ngân hàng được kiểm qua HTTP/PostgreSQL. Không tìm thấy tệp FE-08/FE-09 Gap Analysis riêng trong worktree. Các điều kiện dựa trên đặc tả FE-08/09, FE-15 và quyết định triển khai SePay hiện có. [Chrome Finance](../../artifacts/authz/b2-notification-finance.json) · [Proxy smoke](../../artifacts/authz/b2-hardening-12-proxy-load-1.json)

| Chỉ số | Trước B2 | Sau B2 |
| --- | ---: | ---: |
| FE-09 VERIFIED | 4/20 | 20/20 |
| FE-09 PARTIALLY VERIFIED | 16 | 0 |
| Toàn hệ thống VERIFIED | 52/133 | 68/133 |
| Toàn hệ thống PARTIALLY VERIFIED | 81 | 65 |
| NOT APPLICABLE | 114 | 114 |

Toàn bộ 20 dòng trong bảng FE-09 khớp test PASS ở lượt cuối. FE-01 giữ 17/17 và các dòng ngoài FE-09 giữ nguyên bằng chứng trước B2. [Manifest](../../artifacts/authz/final-authorization-mutation-coverage.json)

Solution Debug build đạt 0 lỗi, 0 cảnh báo. API/Web publish Release đạt. Publish Web ghi NU1900 khi không truy cập được dữ liệu advisory NuGet. Bộ cuối đạt 940 Unit, 64 Architecture, 36 JavaScript, 574 Integration và 154 BillingCycle từ dự án build riêng. Tổng **1.768 PASS, 0 FAIL, 0 SKIP**. Integration tăng sáu kịch bản FE-09. Giữ nguyên framework, SDK, runtime, package, PostgreSQL, schema, migration và công thức công nợ. [Unit](../../artifacts/authz/b2-unit.trx) · [Architecture](../../artifacts/authz/b2-architecture.trx) · [JavaScript](../../artifacts/authz/b2-js.log) · [Integration](../../artifacts/authz/b2-integration.trx) · [BillingCycle](../../artifacts/authz/b2-billing.trx)

Cleanup xác nhận 5 cổng thử nghiệm không còn listener, profile Chrome, chứng chỉ/khóa tạm, ảnh và DB fixture đã dọn. Chín mục DB có từ trước được giữ. FE-09 không còn operation mở trong phạm vi local. Kiểm chứng SePay live và điều kiện chặn phát hành công khai do runtime giữ ở đợt triển khai riêng. [Đánh giá runtime](propflow-runtime-security-risk-assessment.md)

## B3 — FE-05 Service Request Management

Baseline có 1/14 operation FE-05 VERIFIED và 68/133 VERIFIED toàn hệ thống. Fixture tạo hai hộ có tài khoản Resident riêng, tài khoản Manager, ba Staff và các role đối chứng. Các request kiểm quyền dùng JWT từ login thật và thử lại với bearer đã logout. PostgreSQL được tạo riêng cho từng kịch bản. Ảnh PNG đi qua multipart và storage giả. Snapshot đối chiếu request, assignment, activity, ảnh, feedback, hóa đơn, payment, outbox, notification và tín hiệu realtime. [Fixture](../../tests/PropFlow.IntegrationTests/ServiceRequestAuthorizationHttpTests.cs) · [Kịch bản](../../tests/PropFlow.IntegrationTests/ServiceRequestAuthorizationHttpTests.Workflow.cs)

### Checkpoint và lỗi tái hiện

- Vòng đầu trả HTTP 500 khi fixture thiếu hai schema đọc thông tin phân loại và khuyến nghị của chi tiết Manager. Sau khi thêm schema, hai ca còn nhiễu do worker của host fixture gốc tự dispatch trong lúc so snapshot. B3 cấu hình worker tại chính host tạo fixture để kiểm riêng side effect của request. [Vòng đầu](../../artifacts/authz/b3-first.trx) · [Vòng tái hiện](../../artifacts/authz/b3-before-fix.trx)
- HTTP tái hiện lỗi A → B → A. Sau khi nhận đợt mới, Staff A tải được PNG vào kết quả thuộc đợt cũ. Bản sửa đối chiếu theo AssignmentId cho kết quả, ảnh, đề xuất phí và nghiệm thu. Kết quả cũ vẫn có trong lịch sử. Giao diện Staff và Manager dùng thuộc tính bổ sung `isFromCurrentAssignment` để quyết định thao tác trên kết quả hiện hành. [HTTP trước sửa](../../artifacts/authz/b3-before-fix.trx) · [Staff store](../../src/Modules/ServiceRequests/Infrastructure/StaffServiceRequestStore.cs) · [Manager store](../../src/Modules/ServiceRequests/Infrastructure/ManagerServiceRequestStore.cs)
- Các mutation liên quan dùng chung khóa giao dịch theo request với luồng consent. Nếu đổi phân công đã xếp trước, các thao tác tiếp theo đọc assignment sau khi khóa được nhả. Upload kiểm quyền trước khi gọi storage, rồi kiểm lại lúc commit metadata. Tranh chấp sau khi storage bắt đầu dùng compensation hiện có. Kiểm tra hai upload cạnh tranh chỉ lưu một bộ ảnh. [Khóa PostgreSQL](../../src/Modules/ServiceRequests/Infrastructure/Persistence/ServiceRequestsDbContext.cs) · [Kịch bản cạnh tranh](../../tests/PropFlow.IntegrationTests/ServiceRequestAuthorizationHttpTests.Workflow.cs)

### Đối chứng theo operation

| Operation dưới `/api/v1` | Actor | Trước | Test | Đối chứng | Sau |
| --- | --- | --- | --- | --- | --- |
| POST `resident/service-requests` | RESIDENT | VERIFIED | `Resident_can_submit_description_only_over_http_before_manager_classifies_it` | Tạo yêu cầu bằng mô tả, kiểm đúng resident scope và phân loại sau khi gửi. | VERIFIED |
| PUT `manager/service-requests/{id:guid}/assignment` | MANAGER | PARTIALLY VERIFIED | `Manager_assignment_and_staff_accept_progress_require_an_active_assignee` | A → B → A; chỉ một phân công active và outbox tham chiếu đúng AssignmentId. | VERIFIED |
| POST `manager/service-requests/{id:guid}/close` | MANAGER | PARTIALLY VERIFIED | `Manager_resolution_and_close_issue_one_invoice_for_the_consented_amount_and_owner` | Đóng và ghi actor đúng một lần. Hóa đơn giữ nguyên và feedback vẫn cần thanh toán. | VERIFIED |
| PUT `manager/service-requests/{id:guid}/property-context` | MANAGER | PARTIALLY VERIFIED | `Manager_review_and_property_context_preserve_the_reporter_and_require_submitted_state` | Ghi bối cảnh và audit, giữ cư dân gửi yêu cầu và quan hệ cư trú. | VERIFIED |
| POST `manager/service-requests/{id:guid}/resolution/approve` | MANAGER | PARTIALLY VERIFIED | `Manager_resolution_and_close_issue_one_invoice_for_the_consented_amount_and_owner` | Một nghiệm thu và một hóa đơn cho đúng người với phí đã duyệt; không xác nhận thanh toán. | VERIFIED |
| POST `manager/service-requests/{id:guid}/resolution/rework` | MANAGER | PARTIALLY VERIFIED | `Staff_completion_evidence_and_manager_rework_keep_history_private` | Ghi yêu cầu làm lại đúng actor; giữ trạng thái xử lý, phân công và lịch sử bằng chứng. | VERIFIED |
| POST `manager/service-requests/{id:guid}/review` | MANAGER | PARTIALLY VERIFIED | `Manager_review_and_property_context_preserve_the_reporter_and_require_submitted_state` | Chuyển UNDER_REVIEW và ghi audit tiếp nhận đúng Manager. | VERIFIED |
| POST `staff/service-requests/{id:guid}/accept` | STAFF | PARTIALLY VERIFIED | `Manager_assignment_and_staff_accept_progress_require_an_active_assignee` | Đúng đợt và actor chuyển IN_PROGRESS; replay và Staff cũ không ghi. | VERIFIED |
| POST `staff/service-requests/{id:guid}/progress` | STAFF | PARTIALLY VERIFIED | `Manager_assignment_and_staff_accept_progress_require_an_active_assignee` | Chỉ thêm activity tiến độ đúng assignment và actor; không đổi quyền hoặc chi phí. | VERIFIED |
| POST `staff/service-requests/{id:guid}/additional-cost` | STAFF | PARTIALLY VERIFIED | `Additional_cost_replacement_and_resident_decision_use_current_assignment_and_owner` | Đề xuất PENDING, thay thế bằng phiên bản mới và outbox cho đúng cư dân. | VERIFIED |
| POST `staff/service-requests/{id:guid}/complete` | STAFF | PARTIALLY VERIFIED | `Staff_completion_evidence_and_manager_rework_keep_history_private` | Ghi kết quả đúng đợt, chờ ảnh và Manager nghiệm thu; không phát hành hóa đơn. | VERIFIED |
| POST `staff/service-requests/{id:guid}/evidence` | STAFF | PARTIALLY VERIFIED | `Staff_completion_evidence_and_manager_rework_keep_history_private` | Ảnh PNG, metadata và người tải đúng; truy cập riêng, replay và sai scope không gọi storage. | VERIFIED |
| POST `resident/service-requests/{id:guid}/additional-cost/response` | RESIDENT | PARTIALLY VERIFIED | `Additional_cost_replacement_and_resident_decision_use_current_assignment_and_owner` | Ghi quyết định đúng actor và số tiền, replay giữ một lịch sử, không phát sinh hóa đơn hoặc thanh toán. | VERIFIED |
| PUT `resident/service-requests/{id:guid}/feedback` | RESIDENT | PARTIALLY VERIFIED | `Only_the_eligible_owner_can_create_and_update_feedback` | Tạo hoặc sửa đúng một feedback, không đổi chủ, request hay dữ liệu thanh toán. | VERIFIED |

FE-05/10 draft-check tính riêng. Payment report-transfer tính trong FE-09. [Mapping](../../scripts/authz/mutation-cases.json)

### Kết quả B3

| Chỉ số | Trước B3 | Sau B3 |
| --- | ---: | ---: |
| FE-05 VERIFIED | 1/14 | 14/14 |
| FE-05 PARTIALLY VERIFIED | 13 | 0 |
| Toàn hệ thống VERIFIED | 68/133 | 81/133 |
| Toàn hệ thống PARTIALLY VERIFIED | 65 | 52 |
| NOT APPLICABLE | 114 | 114 |

Bộ FE-05 đạt 20/20, gồm 10 kịch bản HTTP B3 và 10 hồi quy về scope ảnh, phân loại, khuyến nghị, consent và notification. Manifest xác nhận PASS của từng operation trong bảng. FE-01 17/17, FE-08 6/6, FE-09 20/20, FE-15 4/4 và FE-05/10 1/1 giữ nguyên bằng chứng. [TRX FE-05](../../artifacts/authz/b3-focused-final.trx) · [Manifest trước B3](../../artifacts/authz/b3-mutation-coverage-before.json) · [Manifest cuối](../../artifacts/authz/final-authorization-mutation-coverage.json)

Chrome Release qua proxy HTTPS xác nhận notification và link đúng người, consent đúng hộ, Staff cũ mất thao tác realtime, Staff mới và Staff quay lại được ghi kết quả của lượt phân công mới. Smoke xác nhận WebSocket, Long Polling và logout STAFF qua nhiều tab. Kiểm tra upload, nghiệm thu, tài chính và feedback chạy qua HTTP/PostgreSQL. [Chrome FE-05](../../artifacts/authz/b3-notification-service-requests.json) · [Chrome session](../../artifacts/authz/b3-hardening-12-browser-proxy-session.json) · [Proxy](../../artifacts/authz/b3-hardening-12-proxy-load-1.json)

Solution Debug build đạt 0 lỗi và 0 cảnh báo. Publish Release API/Web đạt, Web ghi NU1900 khi lấy dữ liệu advisory NuGet. Hồi quy cuối đạt 940 Unit, 64 Architecture, 36 JavaScript, 584 Integration và 154 BillingCycle build từ source cuối. Tổng **1.778 PASS, 0 FAIL, 0 SKIP**, không cộng lại 20 lượt focused. Lượt Architecture đầu hết thời gian kết nối testhost, lượt chạy với loopback đạt 64/64. [Build](../../artifacts/authz/b3-build.log) · [Unit](../../artifacts/authz/b3-unit.trx) · [Architecture](../../artifacts/authz/b3-architecture.trx) · [JavaScript](../../artifacts/authz/b3-javascript.log) · [Integration](../../artifacts/authz/b3-integration.trx) · [BillingCycle](../../artifacts/authz/b3-billing.trx) · [Web Release](../../artifacts/authz/b3-web.log)

Lượt Unit đầu giữ nhầm kết nối database ứng dụng cục bộ cho một ca SePay tổng hợp do cách PowerShell gán thuộc tính của `DbConnectionStringBuilder`. Lượt đó ghi 0 migration pending ở cả 14 DbContext. Test đã tự dọn theo ID, truy vấn chỉ đọc sau đó xác nhận 0 đích nhận tiền, payment và giao dịch mang dấu hiệu fixture đã kiểm. Lượt Unit tính vào tổng chạy lại trên database mới, với tên đích được parse và xác thực trước khi migrate. Đã dọn database, tiến trình, profile, chứng chỉ và ảnh fixture. Kiểm tra 7 cổng đều hết listener và giữ 9 database fixture từ trước. [Unit đầu](../../artifacts/authz/b3-unit-first.trx) · [Migration log đầu](../../artifacts/authz/b3-unit-db-first.log) · [Cleanup](../../artifacts/authz/b3-cleanup.json)

FE-05 không còn operation mở trong phạm vi B3. Kiểm chứng Cloudinary/CDN và SMTP live, cùng điều kiện phát hành công khai liên quan runtime, thuộc phạm vi triển khai riêng. B3 giữ framework, SDK, runtime, EF Core/Npgsql, PostgreSQL, schema, migration và công thức công nợ theo baseline. [Đánh giá runtime](propflow-runtime-security-risk-assessment.md)

## B4 — FE-07 Maintenance Management & Test Database Safety

### An toàn database thử

| Hạng mục | Kết quả |
| --- | --- |
| Nguyên nhân B3 | Gán thuộc tính `ConnectionString` trong PowerShell adapter đã giữ giá trị ban đầu dưới dạng key. Đích được đưa cho Unit vẫn là database ứng dụng. |
| Cấu hình test | `propflow_test` được chỉ định rõ trên endpoint loopback. Pipeline test ưu tiên cấu hình riêng, xác minh tên và endpoint trước khi dùng. |
| Kiểm tra đích | Npgsql parse lại chuỗi kết nối. Connection đã mở chạy `SELECT current_database()` và so chính xác tên. Test host kiểm cấu hình hiệu lực sau khi nạp nguồn cấu hình. |
| Quyền sở hữu và cleanup | Fixture đăng ký mã sở hữu trong catalog sau CREATE. Migrate, verify và drop của CLI cần tên và mã khớp. Drop từ chối khi tên chỉ giống prefix hoặc thuộc lượt khác. |
| Hiệu lực | Unit SePay có PostgreSQL, các fixture Integration/BillingCycle, API test host dùng chung, BrowserFixture, browser runner và diễn tập database. Các lệnh quản trị hoặc deploy PostgreSQL ngoài test giữ cơ chế triển khai riêng. |

Cấu hình test cho B4 truyền rõ tên `propflow_test` vào process, dùng endpoint và tài khoản PostgreSQL loopback đã có. Test database safety xác minh 8 ca, gồm override cấu hình API, nguồn sai, endpoint sai và ownership sentinel. CLI thử migrate sai đích, migrate sai owner và drop sai owner trước khi xác minh rồi dọn sentinel bằng owner đúng. Các lần chặn xảy ra trước quyền ghi. [Guard](../../tests/Shared/TestDatabaseSafety.cs) · [TRX guard](../../artifacts/authz/b4-database-safety-final.trx) · [CLI sentinel](../../artifacts/authz/b4-database-cli.log) · [Cách chạy](../../scripts/authz/README.md)

### Checkpoint FE-07

- Baseline giữ 4/13 VERIFIED và 81/133 VERIFIED toàn hệ thống. Fixture HTTP dùng JWT thực, Manager và ba Staff riêng trên PostgreSQL. Ảnh multipart đi qua storage giả. Snapshot gồm lịch, task, phân công, hoạt động, ảnh, trạng thái tài sản, yêu cầu dịch vụ, tài chính, outbox và tín hiệu realtime. Các ca fixture đầu được hiệu chỉnh để gửi đủ mức ưu tiên, ngày bắt đầu, hạn và tài sản theo contract tạo task. [Kịch bản HTTP](../../tests/PropFlow.IntegrationTests/MaintenanceAuthorizationHttpTests.Workflow.cs) · [Lượt fixture đầu](../../artifacts/authz/b4-fe07-first.trx)
- Hai lệnh Manager cùng liên kết một task với hai yêu cầu đều trả 200 trong ca PostgreSQL có điều phối khóa hàng. Code cũ đọc `SourceServiceRequestId` trước khi cập nhật và thiếu khóa theo task. Bản sửa dùng khóa đang có của FE-07 xuyên suốt đọc, tạo activity và commit. Hai lệnh giờ chỉ có một 200, lệnh sau trả 409. [TRX tái hiện](../../artifacts/authz/b4-fe07-red.trx) · [Bản sửa](../../src/Modules/Maintenance/Application/MaintenanceService.cs)
- Rà evidence phân công cho thấy ca tranh chấp cũ có đối chứng Staff gọi chính endpoint assignment. Mapping được tăng cường bằng ca JWT thật đối chiếu actor sai, assignment, activity và outbox. Ca cũ tiếp tục kiểm reassignment thắng trước upload, 403 và kho ảnh chưa được gọi. [Mapping](../../scripts/authz/mutation-cases.json) · [Ca tranh chấp](../../tests/PropFlow.IntegrationTests/Fe04StaffHttpTests.cs)
- Ca A → B → A phân biệt hai khoảng tham gia của A và khoảng của B. Staff giữ lịch sử đúng khoảng tham gia, Manager giữ quyền đọc ảnh đúng task/result/attachment với `no-store`. Ca thử xác minh ảnh ngoài scope trả 404. Cancel xếp trước làm progress và upload bị từ chối. Cancel xếp sau chờ upload hợp lệ commit và giữ ảnh. Hai nghiệm thu cùng kết quả chỉ có một transition. [Quyết định FE-07](../requirements/FE-07-Historical-Access.md)
- Chrome Release kiểm lịch làm đổi trạng thái tài sản và hiển thị realtime, hai workspace đổi theo assignment, Staff cũ chỉ nhận tín hiệu làm mới rỗng, lịch sử không lộ đợt của người khác. Linkage trả 200 cho Manager, 403 cho Staff và 409 khi lặp. Các thao tác bảo trì trong smoke điều khiển qua API trong Chrome. [Chrome FE-07](../../artifacts/authz/b4-notification-operations.json) · [Proxy HTTPS](../../artifacts/authz/b4-hardening-12-proxy-load-1.json)

### Đối chứng 13 operation

Đối chứng Manager dùng cùng payload hợp lệ khi gọi bằng JWT đúng quyền và bằng tài khoản sai vai trò, phiên đã thu hồi hoặc request không có token. Snapshot kiểm tra tác động của các lượt bị chặn. [Test theo operation](../../scripts/authz/mutation-cases.json) · [TRX FE-07](../../artifacts/authz/b4-focused-final.trx)

| Operation dưới `/api/v1/maintenance` | Actor | Trước B4 | Đối chứng | Sau B4 |
| --- | --- | --- | --- | --- |
| POST `/schedules` | MANAGER | Một phần | Tạo lịch ACTIVE đúng tài sản. Từ chối sai quan hệ tiện ích–thiết bị hoặc khoảng ngày. | VERIFIED |
| PUT `/schedules/{id:guid}` | MANAGER | Một phần | Giữ ID và người tạo khi cập nhật. Giới hạn trường sửa sau khi đã có task và từ chối sửa lịch đã hủy. | VERIFIED |
| POST `/schedules/{id:guid}/extend` | MANAGER | Một phần | Gia hạn cộng đúng thời lượng. Từ chối đơn vị sai, lượng âm hoặc lịch đã hủy. | VERIFIED |
| PATCH `/schedules/{id:guid}/status` | MANAGER | Một phần | Hủy lịch và kết thúc task, assignment thuộc lịch. Chặn thao tác lặp và giữ thiết bị không liên quan. | VERIFIED |
| POST `/tasks` | MANAGER | Một phần | Task mới OPEN, actor từ JWT. Tạo khẩn cấp cập nhật đúng trạng thái tài sản. Từ chối tài sản sai cha và lịch nguồn không tồn tại. | VERIFIED |
| POST `/tasks/{taskId:guid}/assignment` | MANAGER | VERIFIED | Bổ sung JWT thực cho đối chứng Staff gọi chính endpoint. Gán đúng Staff và outbox. Snapshot phân công và outbox giữ nguyên khi bị từ chối. | VERIFIED |
| PATCH `/tasks/{taskId:guid}/asset` | MANAGER | Một phần | Gắn đúng cặp tiện ích–thiết bị. Chặn thiết bị sai cha, tài sản không tồn tại và task đã hủy. | VERIFIED |
| POST `/tasks/{taskId:guid}/cancel` | MANAGER | Một phần | Hủy đúng task, kết thúc phân công và ghi audit. Hủy trước chặn các lượt ghi sau. Upload đã commit giữ được file và metadata. | VERIFIED |
| POST `/tasks/{taskId:guid}/results/{resultId:guid}/review` | MANAGER | Một phần | Kiểm đúng task và result, chặn kết quả ngoài phạm vi hoặc đã xử lý. Hai lượt nghiệm thu đồng thời chỉ ghi một chuyển trạng thái. | VERIFIED |
| POST `/tasks/{taskId:guid}/service-request` | MANAGER | Một phần | Giữ đúng một liên kết và một activity khi hai ghi cạnh tranh. Liên kết không mở quyền sửa Service Request. | VERIFIED |
| POST `/my-tasks/{taskId:guid}/start` | STAFF | VERIFIED | Staff hiện hành bắt đầu đúng task. Staff đã bị gỡ bị chặn trước khi ghi. | VERIFIED |
| POST `/my-tasks/{taskId:guid}/progress` | STAFF | VERIFIED | Staff hiện hành ghi tiến độ. Staff đã bị gỡ hoặc task đã hủy không ghi thêm. | VERIFIED |
| POST `/my-tasks/{taskId:guid}/result` | STAFF | VERIFIED | Upload đúng phân công. Reassignment hoặc cancel thắng trước thì không gọi kho ảnh. Upload đã commit giữ nguyên ảnh. | VERIFIED |

### Kết quả B4

| Chỉ số | Trước B4 | Sau B4 |
| --- | ---: | ---: |
| FE-07 VERIFIED | 4/13 | 13/13 |
| FE-07 PARTIALLY VERIFIED | 9 | 0 |
| Toàn hệ thống VERIFIED | 81/133 | 90/133 |
| Toàn hệ thống PARTIALLY VERIFIED | 52 | 43 |

Manifest chỉ thay 13 dòng FE-07 áp dụng cho mutation. Các dòng khác giữ bằng chứng B3. [Manifest trước](../../artifacts/authz/b4-mutation-coverage-before.json) · [Manifest cuối](../../artifacts/authz/final-authorization-mutation-coverage.json)

Build solution cuối đạt 0 lỗi, 0 cảnh báo. API và Web publish Release đạt. Web và BrowserFixture ghi NU1900 khi lấy dữ liệu advisory NuGet. Hồi quy cuối đạt **1.792 PASS, 0 FAIL, 0 SKIP** gồm Unit 940, Architecture 64, JavaScript 36, Integration 598 và BillingCycle 154. Bộ focused đạt 18/18, gồm 8 ca an toàn database, đã nằm trong Integration. Lượt Integration đầu dùng kỳ vọng 403 cho ảnh ngoài phạm vi. Sau khi đối chứng chính sách hiện hành trả 404, đã sửa kỳ vọng và chạy lại toàn bộ suite. [Build cuối](../../artifacts/authz/b4-build-final.log) · [Integration cuối](../../artifacts/authz/b4-integration-final.trx) · [BillingCycle](../../artifacts/authz/b4-billing.trx) · [Unit](../../artifacts/authz/b4-unit.trx) · [Architecture](../../artifacts/authz/b4-architecture.trx) · [JavaScript](../../artifacts/authz/b4-javascript.log) · [Web Release](../../artifacts/authz/b4-web.log)

Chrome qua proxy xác nhận thu hồi phiên, HTTPS, WebSocket và Long Polling. Không còn tiến trình đã ghi nhận, listener ở 5 cổng thử, profile, chứng chỉ, ảnh tạm hay database đã đăng ký của B4. Chín database đã ghi nhận từ B3 còn đủ. Hai database ngoài danh sách B3 có tên dạng fixture nhưng thiếu marker sở hữu, nên được giữ nguyên. [Chrome session](../../artifacts/authz/b4-hardening-12-browser-proxy-session.json) · [Cleanup](../../artifacts/authz/b4-cleanup.json)

FE-07 không còn operation mở trong phạm vi B4. Framework, SDK, runtime, EF Core/Npgsql, PostgreSQL, schema và migrations giữ theo baseline. Chrome dùng loopback và dữ liệu giả. Cloudinary/CDN và SMTP live chưa được xác minh. Điều kiện chặn phát hành công khai liên quan runtime vẫn áp dụng. [Đánh giá runtime](propflow-runtime-security-risk-assessment.md)

## B5 — FE-13 Communication Authorization Coverage Completion

B5 chạy trên một PostgreSQL loopback tạm với data directory riêng, dùng bản cài đã có trên máy. Cấu hình `PROPFLOW_TEST_DATABASE` trỏ tới `propflow_test` trên cổng 45485. Preflight đối chiếu data directory và PID, xác nhận database thực tế, tạo fixture có marker rồi chạy guard B4 trước migration. Fixture HTTP dùng tài khoản tổng hợp, JWT cấp qua login và WebSocket của TestServer. Kiểm tra ghi và phạm vi người nhận dựa trên snapshot PostgreSQL cùng recorder realtime. Chrome Release dùng transport và hub thật trên loopback. [Preflight](../../artifacts/authz/b5-db-preflight.log) · [Guard 8/8](../../artifacts/authz/b5-safety.trx) · [Fixture HTTP](../../tests/PropFlow.IntegrationTests/CommunicationAuthorizationHttpTests.cs)

### Checkpoint FE-13

- Support Chat có đối chứng đúng resident, đúng Manager được phân, resident chưa có cư trú, vai trò sai, phiên đã logout, quyền gửi, reply, thu hồi một hoặc nhiều tin, ẩn cá nhân, receipt và chuyển tiếp. Snapshot bao gồm presence, connection, conversation, message, notification, email và người nhận invalidation. [Đối chứng Chat](../../tests/PropFlow.IntegrationTests/CommunicationAuthorizationHttpTests.Chat.cs)
- Emergency Calls có đối chứng đúng resident và Manager đã chọn. Kiểm start dùng connection của người khác, retry đồng thời, end đồng thời, retry cạnh tranh với end, revision cũ và tab Manager không được chọn. RTC kiểm fallback STUN và chữ ký TURN tổng hợp cùng hạn dùng và `no-store`. Credential và signaling payload tổng hợp chỉ nằm trong tiến trình test. [Đối chứng Emergency và RTC](../../tests/PropFlow.IntegrationTests/CommunicationAuthorizationHttpTests.Emergency.cs)

- Lượt focused đầu có một lỗi kỳ vọng khi hai phản hồi end lệch 200 ns. PostgreSQL lưu timestamp theo microsecond, còn phản hồi đầu có thể chứa tick .NET chưa bị cắt. Assertion cuối đối chiếu cả hai với cùng mốc đã lưu và kiểm actor kết thúc, trạng thái, người tham gia và connection. Không có lỗi implementation cần thay đổi từ các ca chạy. [TRX lần đầu](../../artifacts/authz/b5-focused-first.trx) · [TRX cuối 27/27](../../artifacts/authz/b5-focused-final.trx)
- Chrome Communication xác minh draft đến đúng Manager, typing đến đúng cư dân, lỗi gửi vẫn giữ draft, reply, receipt, recall và hide cập nhật giao diện. Chuyển tiếp từ hội thoại đã đóng đến hội thoại mới của Manager chỉ tới người nhận đích. Lượt đầu hết thời gian chờ WebSocket, lần chạy lại đạt. Chrome Emergency với micro giả chỉ gửi offer/answer đến đúng tab đã chọn. Retry tăng revision và end giải phóng audio element. [Chrome Chat](../../artifacts/authz/b5-communication-notification-communication.json) · [Chrome Emergency](../../artifacts/authz/b5-emergency-notification-emergency.json)

### Đối chứng 18 operation

Các operation mới được thử với anonymous, ADMIN, STAFF, ACCOUNTANT và hai vai trò nghiệp vụ có bearer đã thu hồi. Ca actor ngoài participant scope dùng đúng payload trên tài nguyên có thật. [Mapping](../../scripts/authz/mutation-cases.json) · [TRX focused](../../artifacts/authz/b5-focused-final.trx)

| Operation dưới `/api/v1` | Actor | Trước B5 | Đối chứng | Sau B5 |
| --- | --- | --- | --- | --- |
| POST `/emergency-calls` | RESIDENT | VERIFIED | Giữ mapping kiểm thu hồi phiên khi media đã kết nối. Thêm JWT đúng cư dân, sai role và connection mượn từ cư dân khác. Hai lượt start giữ một call. | VERIFIED |
| POST `/emergency-calls/{id:guid}/end` | MANAGER, RESIDENT | Một phần | Cư dân hoặc Manager được chọn của cuộc gọi. Hai lệnh end giữ một mốc kết thúc và actor; signaling sau khi ended bị chặn. | VERIFIED |
| POST `/emergency-calls/{id:guid}/retry` | MANAGER, RESIDENT | Một phần | Participant của cuộc gọi chưa kết thúc. Retry cạnh tranh tăng revision trên cùng call; tab khác và revision cũ không phát signaling, end không bị đảo ngược. | VERIFIED |
| GET `/emergency-calls/rtc` | MANAGER, RESIDENT | Một phần | Manager có quyền hoặc cư dân đủ điều kiện, không cần cuộc gọi đang mở. Fallback STUN, TURN tổng hợp ký đúng actor và TTL, no-store; actor bị chặn tại guard không gọi provider. | VERIFIED |
| PUT `/support-chat/availability` | MANAGER | Một phần | Tài khoản Manager hiện hành có kết nối chat khi bật. Chỉ ghi presence của caller; tự phân hội thoại chờ cho Manager đủ điều kiện, tắt không đóng cuộc đang active. | VERIFIED |
| POST `/support-chat/conversations` | RESIDENT | Một phần | Cư dân có liên kết cư trú và hội thoại của mình. Hai lần gửi tin đầu cùng khóa giữ một hội thoại và một tin cư dân. | VERIFIED |
| POST `/support-chat/conversations/{id:guid}/close` | MANAGER, RESIDENT | Một phần | Cư dân sở hữu hoặc Manager được phân. Close cạnh tranh chỉ tạo một tin hệ thống, giữ lịch sử và chuyển đúng cuộc chờ kế tiếp. | VERIFIED |
| POST `/support-chat/conversations/{id:guid}/messages` | MANAGER, RESIDENT | Một phần | Participant của hội thoại, reply phải trỏ tin khả dụng trong cùng hội thoại. Giữ actor từ JWT, khóa tin và reply; send sau close bị chặn, không ghi ngoài scope. | VERIFIED |
| PUT `/support-chat/conversations/{id:guid}/messages/{messageId:guid}/recall` | MANAGER, RESIDENT | Một phần | Người gửi chính tin trong hội thoại được phép. Hai lần thu hồi cùng tin giữ một trạng thái; sai actor hoặc ID khác hội thoại bị chặn. | VERIFIED |
| PUT `/support-chat/conversations/{id:guid}/messages/recall` | MANAGER, RESIDENT | Một phần | Người gửi toàn bộ tin thuộc hội thoại được phép. Danh sách lẫn tin ngoài quyền bị chặn toàn bộ; replay không thay snapshot. | VERIFIED |
| POST `/support-chat/conversations/{id:guid}/messages/forward` | MANAGER, RESIDENT | Một phần | Participant phải có quyền trên cả hội thoại nguồn và đích. Chỉ chép tin còn khả dụng; chặn đích ngoài scope, bản sao giữ nội dung khi thu hồi nguồn và replay không tạo thêm. | VERIFIED |
| POST `/support-chat/conversations/{id:guid}/messages/hide` | MANAGER, RESIDENT | Một phần | Participant với tin thuộc hội thoại của mình. Chỉ ẩn với caller, giữ tin và receipt phía còn lại; batch khác hội thoại không thay snapshot. | VERIFIED |
| POST `/support-chat/conversations/{id:guid}/messages/receipts` | MANAGER, RESIDENT | Một phần | Participant nhận tin từ phía đối diện trong cùng hội thoại. Lưu delivered/seen bằng thời gian server, bỏ qua tin tự gửi hoặc khác hội thoại; retry và cập nhật đến muộn giữ dấu mốc. | VERIFIED |
| POST `/announcements` | MANAGER | VERIFIED | Giữ evidence publish đúng vai trò và audience. | VERIFIED |
| POST `/notifications/{id:guid}/read` | Account có quyền notification | VERIFIED | Đánh dấu đúng notification của tài khoản, cross-owner bị chặn. | VERIFIED |
| POST `/notifications/read-all` | Account có quyền notification | VERIFIED | Chỉ cập nhật unread của người gọi. | VERIFIED |
| POST `/resident/notifications/{id:guid}/read` | RESIDENT | VERIFIED | Alias giữ scope notification của cư dân. | VERIFIED |
| POST `/resident/notifications/read-all` | RESIDENT | VERIFIED | Alias bulk chỉ cập nhật unread của cư dân gọi. | VERIFIED |

### Kết quả B5

| Chỉ số | Trước B5 | Sau B5 |
| --- | ---: | ---: |
| FE-13 VERIFIED | 6/18 | 18/18 |
| FE-13 PARTIALLY VERIFIED | 12 | 0 |
| Toàn hệ thống VERIFIED | 90/133 | 102/133 |
| Toàn hệ thống PARTIALLY VERIFIED | 43 | 31 |

RTC GET tiếp tục nằm trong mẫu số. Manifest xác nhận đúng 18 operation FE-13 và giữ nguyên từng dòng ngoài phạm vi. [Manifest trước](../../artifacts/authz/b5-mutation-coverage-before.json) · [Manifest cuối](../../artifacts/authz/final-authorization-mutation-coverage.json)

Build cuối đạt 0 lỗi và 0 cảnh báo. API/Web publish Release đạt, Web ghi NU1900 khi truy cập nguồn advisory NuGet. Hồi quy cuối đạt **1.799 PASS, 0 FAIL, 0 SKIP** gồm Unit 940, Architecture 64, JavaScript 36, Integration 605 và BillingCycle 154. Bộ focused 27/27 và guard 8/8 đã nằm trong Integration. HTTPS, WebSocket, thu hồi phiên và Long Polling đạt qua proxy loopback. [Build](../../artifacts/authz/b5-build-final.log) · [Unit](../../artifacts/authz/b5-unit.trx) · [Architecture](../../artifacts/authz/b5-architecture.trx) · [JavaScript](../../artifacts/authz/b5-javascript.log) · [Integration](../../artifacts/authz/b5-integration-final.trx) · [BillingCycle](../../artifacts/authz/b5-billing.trx) · [Proxy smoke](../../artifacts/authz/b5-communication-hardening-12-proxy-load-1.json)

Cleanup xác nhận PID PostgreSQL riêng và các PID Chrome/API/Web/proxy của B5 đã dừng. Sáu cổng đã giải phóng, không còn fixture con, profile, chứng chỉ, ảnh tạm hoặc thư mục cluster B5. Truy vấn chỉ đọc xác nhận database ứng dụng và đủ 11 database được giữ qua B4. [Cleanup](../../artifacts/authz/b5-cleanup.json)

B5 giữ implementation, workflow, schema, migrations, SDK, runtime và package. Không còn operation FE-13 mở trong phạm vi cục bộ. TURN live và cuộc gọi giữa hai mạng Internet chưa được xác minh. Điều kiện chặn phát hành công khai do runtime vẫn áp dụng. [Đánh giá runtime](propflow-runtime-security-risk-assessment.md)

## B6 — Core Property Operations Authorization Coverage Completion

PostgreSQL thử dùng data directory riêng và cổng loopback 45486. Preflight xác minh data directory, cổng, tên database hiệu lực và marker sở hữu trước khi chạy migration trên fixture. Bộ guard đạt 8/8. Fixture HTTP cấp JWT qua login thật, thu hồi qua logout, kiểm tra các đối chứng trên PostgreSQL và dùng WebSocket cho thông báo đóng phản ánh. Weather và kho ảnh dùng provider giả. [Preflight](../../artifacts/authz/b6-db-preflight.json) · [Guard](../../artifacts/authz/b6-safety.trx) · [Fixture HTTP](../../tests/PropFlow.IntegrationTests/PropertyOperationsAuthorizationHttpTests.cs)

### Checkpoint B6.1, B6.2 và B6.3

| Checkpoint | Feature | Trước | Sau | Toàn hệ thống sau checkpoint |
| --- | --- | ---: | ---: | ---: |
| B6.1 | FE-02 | 1/6 | 6/6 | 108/133 |
| B6.1 | FE-03 | 7/8 | 8/8 | 108/133 |
| B6.2 | FE-04 | 0/10 | 10/10 | 118/133 |
| B6.3 | FE-06 | 0/7 | 7/7 | 125/133 |

- B6.1 dùng lại ca kiểm tra căn hộ, sở hữu và vòng đời FE-02/03. Bằng chứng mới bao gồm STAFF và ACCOUNTANT được đọc theo projection, ACCOUNTANT không thấy email và điện thoại, email đăng nhập độc lập với email hồ sơ, quyền nghiệp vụ theo trạng thái cư dân và hóa đơn đã phát hành có thanh toán một phần. [Cư dân và căn hộ](../../tests/PropFlow.IntegrationTests/PropertyOperationsAuthorizationHttpTests.Residents.cs)
- B6.2 xác minh Manager qua từng route, tạo loại trùng đồng thời, duy trì building singleton và quan hệ tài sản. Tái hiện việc PATCH tiện ích đang bảo trì khẩn cấp trả 200. Route đã ghi đè trạng thái sau khi chỉ chặn giá trị đích `UNDER_MAINTENANCE`. Status PATCH của cả hai loại tài sản đọc lại bản ghi dưới khóa hàng và từ chối đổi trạng thái bảo trì với 409. Giao diện vô hiệu hóa nút trên hàng đang bảo trì. [TRX tái hiện](../../artifacts/authz/b6-asset-maintenance-red.trx) · [Ca hồi quy tài sản](../../tests/PropFlow.IntegrationTests/PropertyOperationsAuthorizationHttpTests.Assets.cs)
- B6.3 dùng lại hồi quy transaction đóng phản ánh và notification. Ca HTTP mới kiểm tra người gửi lấy từ JWT, thiết bị thuộc đúng tiện ích, STAFF chỉ sửa follow-up được phân công, concurrent review/resolve/close và một thông báo realtime chỉ tới cư dân gửi phản ánh. [Phản ánh](../../tests/PropFlow.IntegrationTests/PropertyOperationsAuthorizationHttpTests.Complaints.cs) · [Transaction](../../tests/PropFlow.IntegrationTests/ComplaintCommunicationPostgresTests.cs)

Trong fixture tài sản, lần chạy đầu dùng so khớp chuỗi trực tiếp trên cột JSONB, thiếu provider kho ảnh và gửi payload tạo nhiệm vụ thiếu trường bắt buộc. Lượt tái hiện dùng đối chứng hợp lệ trước khi sửa implementation. [TRX chẩn đoán](../../artifacts/authz/b6-asset-diagnostic.trx) · [TRX tập trung 55/55](../../artifacts/authz/b6-focused.trx)

### Đối chứng 23 operation

Mỗi dòng có đối chứng dương qua HTTP và xác minh tác động đã lưu. Đối chứng âm dùng payload nghiệp vụ hợp lệ với anonymous, sai role và bearer đã thu hồi. Các route có quan hệ cha–con hoặc điều kiện workflow có thêm ca sai phạm vi tương ứng. Tên test và chính sách chính xác của từng operation nằm trong mapping và manifest. [Mapping](../../scripts/authz/mutation-cases.json) · [Manifest](../../artifacts/authz/final-authorization-mutation-coverage.json)

| Feature | Operation dưới `/api/v1` | Actor | Phạm vi và tác động | Sau B6 |
| --- | --- | --- | --- | --- |
| FE-02 | PUT `/residents/{id:guid}` | MANAGER | Hồ sơ cư dân theo ID trên route. Chỉ Manager sửa hồ sơ. Giữ account linkage, email đăng nhập, role và quan hệ. ACCOUNTANT nhận projection đã che contact. | VERIFIED |
| FE-02 | PATCH `/residents/{id:guid}/status` | MANAGER | Trạng thái hồ sơ cư dân có tài khoản liên kết. Inactive và restore thay eligibility nghiệp vụ, giữ danh tính, lịch sử cư trú và chứng từ. Payload enum lỗi và actor trái quyền không ghi. | VERIFIED |
| FE-02 | POST `/residents/{id:guid}/residencies` | MANAGER | Cư dân và căn hộ đích hợp lệ. Ghi đúng resident và apartment. Trùng quan hệ trả 409. Giữ hóa đơn và thanh toán đã xác nhận. | VERIFIED |
| FE-02 | PATCH `/residents/{id:guid}/residencies/{residencyId:guid}/end` | MANAGER | Quan hệ cư trú thuộc cư dân trên route. Hai lần kết thúc để lại một chuyển trạng thái và đúng actor. Sai parent và ngày lỗi không ghi. Giữ lịch sử tài chính. | VERIFIED |
| FE-02 | POST `/residents/{id:guid}/residencies/move` | MANAGER | Quan hệ nguồn của cư dân và căn hộ đích hợp lệ. Hai đích cạnh tranh chỉ tạo một quan hệ hiện hành. Cổng cư dân thấy đúng căn đích, hồ sơ cũ và hóa đơn được giữ. | VERIFIED |
| FE-03 | PATCH `/apartments/{id:guid}/owners/{ownershipId:guid}/end` | MANAGER | Quyền sở hữu hiện hành thuộc căn hộ trên route. Hai lệnh cạnh tranh ghi một mốc, đúng actor. Sai căn hộ hoặc ngày không hợp lệ không ghi; giữ đồng sở hữu và chứng từ. | VERIFIED |
| FE-04 | PUT `/buildings/current` | MANAGER | Hồ sơ singleton của chung cư hiện hành. Manager cập nhật thuộc tính hợp lệ và giữ ID. Từ chối sai vai trò, bearer đã thu hồi và số tầng sai. | VERIFIED |
| FE-04 | POST `/equipment-types` | MANAGER | Danh mục loại thiết bị của chung cư. Hai lệnh trùng tên tạo một loại active với creator từ JWT. | VERIFIED |
| FE-04 | POST `/Equipments` | MANAGER | Thiết bị với loại và tiện ích hợp lệ. Tạo đúng liên kết và trạng thái ACTIVE. Giữ creator từ JWT, từ chối sai vai trò và phiên. | VERIFIED |
| FE-04 | PUT `/Equipments/{id:guid}` | MANAGER | Thiết bị hiện hữu và quan hệ với tiện ích, loại thiết bị. Giữ ID, liên kết hợp lệ và trạng thái. Loại tham chiếu sai và actor trái quyền không ghi. | VERIFIED |
| FE-04 | PATCH `/Equipments/{id:guid}/status` | MANAGER | Thiết bị hiện hữu với trạng thái vận hành được phép. Trạng thái INACTIVE tạo một sự kiện. Không vào hoặc thoát bảo trì qua FE-04, kể cả khi có nhiệm vụ bảo trì khẩn cấp. | VERIFIED |
| FE-04 | POST `/facility-types` | MANAGER | Danh mục loại tiện ích của chung cư. Hai lệnh trùng tên tạo một loại active với creator từ JWT. | VERIFIED |
| FE-04 | POST `/Facilities` | MANAGER | Tiện ích với loại và vị trí hợp lệ. Tạo ACTIVE đúng actor. Trùng mã tự sinh hoặc actor trái quyền không thay bản ghi và outbox. | VERIFIED |
| FE-04 | PUT `/Facilities/{id:guid}` | MANAGER | Tiện ích hiện hữu và loại tiện ích hợp lệ. Ghi đúng tên và actor, giữ thiết bị liên kết, trạng thái và định danh. | VERIFIED |
| FE-04 | PATCH `/Facilities/{id:guid}/status` | MANAGER | Tiện ích hiện hữu với trạng thái vận hành được phép. Đổi trạng thái tạo đúng sự kiện. Nhiệm vụ khẩn cấp giữ trạng thái tiện ích, thiết bị và task khi gọi PATCH trực tiếp. | VERIFIED |
| FE-04 | GET `/resident/weather` | RESIDENT | Vị trí lấy từ cấu hình chung cư, qua tài khoản RESIDENT. Provider giả trả thời tiết, cache và fallback đúng địa chỉ. Sai role, anonymous và bearer đã thu hồi không gọi provider. | VERIFIED |
| FE-06 | POST `/complaints` | RESIDENT | Cư dân có liên kết cư trú hiện hành. Tạo đúng resident, residency và căn hộ từ JWT. Bỏ qua định danh chèn sai. Hộ khác không đọc được. | VERIFIED |
| FE-06 | POST `/complaints/{id:guid}/review` | MANAGER | Phản ánh đang chờ tiếp nhận. Hai lệnh đồng thời tạo một activity đúng actor và một transition. | VERIFIED |
| FE-06 | PUT `/complaints/{id:guid}/context` | MANAGER | Phản ánh hiện hữu và cặp tiện ích, thiết bị hợp lệ. Ghi đúng ngữ cảnh, giữ reporter và residency. Thiết bị thuộc tiện ích khác bị chặn. | VERIFIED |
| FE-06 | POST `/complaints/{id:guid}/followups` | MANAGER | Phản ánh đang xem xét và STAFF đủ điều kiện. Ghi đúng phân công. ACCOUNTANT không được chọn làm STAFF. Caller ngoài MANAGER không làm thay đổi snapshot. | VERIFIED |
| FE-06 | PUT `/complaints/{id:guid}/followups/{followupId:guid}` | STAFF | STAFF hiện hành được giao công việc trên đúng phản ánh. Ghi đúng tiến độ, kết quả, assignee và actor. Chặn STAFF khác, sai parent, bearer đã thu hồi và cập nhật lặp. | VERIFIED |
| FE-06 | POST `/complaints/{id:guid}/resolve` | MANAGER | Phản ánh đã hoàn thành các công việc đang mở. Chặn resolve sớm. Hai lệnh đồng thời giữ một phản hồi chính thức và giữ lịch sử. | VERIFIED |
| FE-06 | POST `/complaints/{id:guid}/close` | MANAGER | Phản ánh đã giải quyết và cư dân đích theo reporter. Hai lệnh đồng thời giữ một lần đóng và một notification. WebSocket chỉ gửi cư dân đích. | VERIFIED |

Manifest chỉ tăng 23 dòng từ PARTIALLY VERIFIED lên VERIFIED, giữ 114 NOT APPLICABLE và các dòng ngoài phạm vi. Tám operation còn một phần thuộc FE-10/11/12. Chúng gồm classification review, recommendation và recommendation review, bốn route knowledge documents, và chatbot ask. [Manifest trước B6](../../artifacts/authz/b6-mutation-coverage-before.json)

### Chrome Release và kiểm tra kết nối

Manager đã sửa hồ sơ và kết thúc sở hữu từ giao diện. STAFF và ACCOUNTANT đọc được hồ sơ với quyền tương ứng. Chrome của cư dân phản ánh căn hộ mới sau khi Manager gọi API chuyển cư trú. Manager thấy dữ liệu thiết bị vừa cập nhật và nút trạng thái bị vô hiệu hóa khi FE-07 đưa thiết bị vào bảo trì. Gọi HTTP trực tiếp xác nhận STAFF nhận 403 và Manager cố ghi đè trạng thái bảo trì nhận 409. Chuỗi phản ánh đi từ gửi, tiếp nhận, hoàn thành follow-up, giải quyết và đóng qua giao diện. Phân công được thực hiện qua API. Chỉ trình duyệt của cư dân gửi phản ánh nhận sự kiện đóng.

Lượt Chrome đầu thiếu thông tin giấy tờ bắt buộc khi sửa hồ sơ. Lượt chạy lại điền dữ liệu tổng hợp đủ điều kiện và hoàn tất cả ba nhóm. Chrome qua HTTPS proxy cũng xác nhận thu hồi phiên qua hai tab và bearer cũ trả 401. [Lượt Chrome đầu](../../artifacts/authz/b6-browser-first.log) · [Chrome cuối](../../artifacts/authz/notification-property-operations.json) · [Phiên qua proxy](../../artifacts/authz/b6-hardening-12-browser-proxy-session.json) · [HTTPS và SignalR](../../artifacts/authz/b6-hardening-12-proxy-load-1.json)

### Hồi quy

Solution build cuối đạt 0 lỗi và 0 cảnh báo. Publish Release API và Web đạt. Publish Web ghi NU1900 khi truy cập nguồn advisory NuGet. Focused đạt 55/55, Integration 612/612, Unit 940/940, Architecture 64/64 và JavaScript 36/36. [Build cuối](../../artifacts/authz/b6-build-final.log) · [API Release](../../artifacts/authz/b6-api.log) · [Web Release](../../artifacts/authz/b6-web.log) · [Integration](../../artifacts/authz/b6-integration.trx) · [Unit](../../artifacts/authz/b6-unit.trx) · [Architecture](../../artifacts/authz/b6-architecture.trx) · [JavaScript](../../artifacts/authz/b6-javascript.log)

Trong lượt BillingCycle đầu, một ca muốn thử thông báo khi đổi trạng thái thường nhưng dùng thiết bị đang có bảo trì khẩn cấp. HTTP 409 sau sửa là hành vi mong đợi của B6. Ca đã tách thiết bị vận hành khỏi thiết bị bảo trì, kiểm 409 không tạo thông báo và kiểm các chuyển trạng thái hợp lệ phát đúng một thông báo cho mỗi cư dân. Ca chạy lại đạt. [Lượt đầu](../../artifacts/authz/b6-billing.trx) · [Ca hồi quy](../../artifacts/authz/b6-billing-notice.trx)

Lượt BillingCycle toàn bộ sau sửa đạt 154/154. Tổng hồi quy là **1.806 PASS, 0 FAIL, 0 SKIP**. Focused và guard là lượt chọn ca từ Integration nên chỉ được tính một lần. [BillingCycle cuối](../../artifacts/authz/b6-billing-final.trx)

### Cleanup và phạm vi

Đã dừng PostgreSQL tạm, API, Web, proxy và Chrome của B6, giải phóng sáu cổng test và dọn cluster, fixture, profile, chứng chỉ, khóa và dữ liệu tổng hợp. Đối chiếu catalog chỉ đọc theo đúng danh sách B5 xác nhận database ứng dụng và đủ 11 database được bảo vệ. [Cleanup](../../artifacts/authz/b6-cleanup.json)

Thay đổi B6 tập trung vào guard trạng thái tài sản, nút trạng thái tương ứng và bằng chứng kiểm thử. B6 không cần migration hoặc nâng cấp thành phần. Weather dùng provider giả và release smoke chạy cục bộ. Weather live và điều kiện phát hành công khai vẫn theo đánh giá runtime hiện hành. [Đánh giá runtime](propflow-runtime-security-risk-assessment.md)

## B7 — AI Authorization Coverage Completion

Baseline của B7 là 125/133 operation VERIFIED. Phạm vi là ba route xử lý đề xuất của Manager, bốn route quản lý tài liệu và một route hỏi chatbot của cư dân. Fixture B7 dùng account tổng hợp, JWT qua đăng nhập chuẩn, PostgreSQL trên loopback 45487 và hai transport HTTP Gemini giả. Preflight kiểm data directory, tên database hiệu lực và marker sở hữu trước khi migration fixture. Guard database đạt 8/8. [Preflight](../../artifacts/authz/b7-db-preflight.log) · [Guard](../../artifacts/authz/b7-safety.trx) · [Fixture HTTP](../../tests/PropFlow.IntegrationTests/AiAuthorizationHttpTests.cs)

| Feature | Operation dưới `/api/v1` | Actor và phạm vi | Đối chứng |
| --- | --- | --- | --- |
| FE-10 | PUT `/manager/service-requests/{id:guid}/classification/review` | Manager, đề xuất mới nhất thuộc đúng yêu cầu và danh mục còn hiệu lực | Giữ đề xuất gốc, ghi actor từ JWT và chỉ đổi danh mục chính thức. Sai phiếu hoặc danh mục bị chặn. Hai review khác quyết định chỉ giữ một bản ghi. |
| FE-11 | POST `/manager/service-requests/{id:guid}/recommendation` | Manager, yêu cầu hiện hữu còn ở trạng thái xử lý | Đề xuất chỉ nhập bảng AI. Lệnh cạnh tranh bị chặn trong lúc gọi provider. Retry dùng lại kết quả, output thiếu trường giữ ưu tiên cũ. |
| FE-11 | PUT `/manager/service-requests/{id:guid}/recommendation/review` | Manager, đề xuất đúng yêu cầu và phiên bản hiện hành | Ghi đúng người duyệt và giá trị chính thức. Chặn sai phiếu, đề xuất cũ, thiếu lý do và priority sai. STAFF được giao chỉ nhận phương án đã duyệt. |
| FE-12 | POST `/knowledge/documents` | ADMIN hoặc MANAGER có quyền quản lý tài liệu chung cư | Tạo bản nháp v1 và lịch sử đúng actor. STAFF không đọc được bản nháp. Không tin creator, revision hay trạng thái do payload chèn. |
| FE-12 | PUT `/knowledge/documents/{id:guid}` | ADMIN hoặc MANAGER, đúng token tài liệu | Hai lượt sửa đồng thời chỉ tạo một revision. Giữ bản và audience đã xuất bản khi bản nháp thay đổi. |
| FE-12 | POST `/knowledge/documents/{id:guid}/publication` | ADMIN hoặc MANAGER, token thuộc tài liệu và bản mới nhất đã lưu | Xuất bản theo token. Cạnh tranh và replay token cũ bị chặn, STAFF nhận đúng bản đã xuất bản. |
| FE-12 | POST `/knowledge/documents/{id:guid}/unpublish` | ADMIN hoặc MANAGER, token đúng tài liệu đang xuất bản | Thu hồi bản công bố một lần khi cạnh tranh. Giữ lịch sử và chặn STAFF đọc lại bằng ID cũ. |
| FE-12 | POST `/resident/chatbot/ask` | RESIDENT có cư trú hiện hành, nguồn RESIDENT đang xuất bản | Chặn user ngoài scope trước Gemini. Chỉ gửi nguồn hiện hành; fallback timeout và citation giả giữ nguồn hợp lệ; thu hồi trong lúc chạy bỏ kết quả cũ. |

Các yêu cầu bị từ chối dùng payload trên tài nguyên tổng hợp có thật. Đối chiếu database trước và sau, số lượt gọi provider, số sự kiện realtime và các bản ghi trực tiếp liên quan. Ca PF-AUTHZ-08 đối chiếu metadata danh sách và chi tiết của STAFF trước và sau khi Manager lưu bản nháp. [FE-10/11](../../tests/PropFlow.IntegrationTests/AiAuthorizationHttpTests.ServiceRequests.cs) · [Tài liệu FE-12](../../tests/PropFlow.IntegrationTests/AiAuthorizationHttpTests.Knowledge.cs) · [Chatbot FE-12](../../tests/PropFlow.IntegrationTests/AiAuthorizationHttpTests.Chat.cs)

**Lỗi production đã tái hiện.** Credential tổng hợp trong mô tả phiếu đã tới transport phân loại. Hàm che dữ liệu chung trước đó che email và dãy số dài, còn JWT và các giá trị JWT, API key, mật khẩu, OTP có nhãn vẫn có thể đi vào request. B7 bổ sung bộ lọc theo định dạng và nhãn vào hàm dùng chung, qua đó áp dụng cả cho classification, recommendation, câu hỏi chatbot và đoạn nguồn gửi sang Gemini. Ca đầu gặp 429 do dùng chung lượt yêu cầu của một Manager, nên tách actor thử để giữ giới hạn production. Sau đó ca tái hiện ghi nhận lỗi payload trên đối chứng hợp lệ. [Lượt đầu](../../artifacts/authz/b7-focused-first.trx) · [Lượt tái hiện](../../artifacts/authz/b7-focused-red.trx) · [Bộ che dữ liệu](../../src/Modules/AiChatbot/Domain/KnowledgeChunk.cs)

Manager xác nhận đề xuất trước khi danh mục hoặc mức ưu tiên chính thức thay đổi. Phương án bảo trì vẫn là dữ liệu để Manager duyệt. Chatbot chỉ lấy bản RESIDENT đang xuất bản, từ chối nguồn đã thu hồi trong khi đợi provider và đưa câu hỏi hóa đơn cá nhân về đúng giao diện. Các ca injection không mở rộng phạm vi truy xuất, không tạo tác nghiệp và không ghi dữ liệu tài chính. Các mẫu che dữ liệu đã kiểm không bao quát mọi cách người dùng nhập thông tin cá nhân tự do. Việc duyệt nội dung nguồn, mục đích gửi dữ liệu và chính sách lưu giữ bên provider vẫn thuộc nghiệm thu privacy.

| Checkpoint | Sau checkpoint | Tổng VERIFIED |
| --- | --- | ---: |
| B7.1 | FE-10 2/2, FE-11 2/2 | 128/133 |
| B7.2 | Bốn route kho tài liệu FE-12 | 132/133 |
| B7.3 | FE-12 5/5, gồm chatbot | 133/133 |

Nhóm AI chạy từ source sau bản sửa đạt 28/28. Manifest có 247 operation HTTP, gồm 133 VERIFIED, 114 NOT APPLICABLE, 0 PARTIALLY VERIFIED và 0 NOT VERIFIED. Đối chiếu tên test và khóa method/route của 133 dòng áp dụng. Chỉ tám dòng B7 đổi trạng thái, bằng chứng trước B7 được giữ. NOT APPLICABLE là phân loại cho mẫu số kiểm thử mutation. [TRX AI](../../artifacts/authz/b7-ai-final.trx) · [Mapping](../../scripts/authz/mutation-cases.json) · [Manifest](../../artifacts/authz/final-authorization-mutation-coverage.json)

| Feature | VERIFIED | Feature | VERIFIED | Feature | VERIFIED |
| --- | ---: | --- | ---: | --- | ---: |
| FE-01 | 17/17 | FE-05/10 | 1/1 | FE-10 | 2/2 |
| FE-02 | 6/6 | FE-06 | 7/7 | FE-11 | 2/2 |
| FE-03 | 8/8 | FE-07 | 13/13 | FE-12 | 5/5 |
| FE-04 | 10/10 | FE-08 | 6/6 | FE-13 | 18/18 |
| FE-05 | 14/14 | FE-09 | 20/20 | FE-15 | 4/4 |

### Chrome Release và hồi quy

Chrome Release qua HTTPS proxy dùng hook fixture để thay HTTP transport Gemini. Manager phân tích và xác nhận classification từ giao diện, rồi giao diện tải recommendation đã duyệt qua API. STAFF nhận 403 khi thử duyệt hoặc công bố. Manager lưu nháp từ giao diện, ADMIN công bố và thu hồi qua API, STAFF chỉ đọc đúng bản công bố. Cư dân đặt câu hỏi trong giao diện, nhận câu trả lời từ Gemini giả với nguồn hiện hành rồi hỏi lại sau khi thu hồi. Lượt đầu hết thời gian chờ DevTools ở smoke phiên. Lượt chạy lại sau khi các suite database đã kết thúc đạt ca AI và smoke HTTPS/SignalR. [Chrome AI](../../artifacts/authz/notification-ai-authorization.json) · [Chrome lượt đầu](../../artifacts/authz/b7-browser-first.log) · [HTTPS và SignalR](../../artifacts/authz/b7-hardening-12-proxy-load-1.json)

Solution build, publish API/Web Release, Unit 940/940, Architecture 64/64 và JavaScript 36/36 đạt. Publish Web và build BrowserFixture ghi NU1900 khi tải dữ liệu advisory NuGet. Lượt Integration toàn bộ có 617 ca PASS và thêm 65 kết quả FAIL trùng tên từ hai class hết thời gian khi dọn fixture. Lượt BillingCycle toàn bộ có 150 PASS và 4 timeout tại cùng bước `DROP DATABASE`. Trace xác định lỗi ở `TestDatabaseSafety.DropAsync`. [Build](../../artifacts/authz/b7-build-final.log) · [Unit](../../artifacts/authz/b7-unit.trx) · [Architecture](../../artifacts/authz/b7-architecture.trx) · [JavaScript](../../artifacts/authz/b7-javascript.log) · [Integration](../../artifacts/authz/b7-integration.trx) · [BillingCycle](../../artifacts/authz/b7-billing.trx)

Các ca timeout được chạy lại theo nhóm sau khi Chrome dừng, với timeout dài hơn chỉ trên kết nối thử. Integration đạt 65/65 ở hai class bị ảnh hưởng. BillingCycle đạt 10/10 ở hai test theo tham số, gồm cả bốn trường hợp từng timeout. Đối chiếu theo tên ca cho kết quả **1.811 ca phân biệt PASS, 0 ca còn FAIL, 0 SKIP**. Ca focused và các lượt chạy lại không được cộng thêm vào tổng. Đã khớp bằng chứng PASS thực tế trên source cuối với toàn bộ 133 khóa method/route và mapping test của manifest. [Integration chạy lại](../../artifacts/authz/b7-integration-retry.trx) · [BillingCycle chạy lại](../../artifacts/authz/b7-billing-retry.trx)

### Cleanup và các điều kiện còn mở

Tám fixture từng timeout được dọn qua `TestDatabaseSafety` sau khi xác minh cluster, PID, marker sở hữu và kiểm không còn kết nối. Đã dừng PostgreSQL tạm và các tiến trình API, Web, proxy, Chrome. Sáu cổng thử đã được giải phóng, certificate, profile, key và dữ liệu tổng hợp đã được dọn. Truy vấn catalog chỉ đọc xác nhận database ứng dụng và đủ 11 database được bảo vệ. [Cleanup](../../artifacts/authz/b7-cleanup.json)

B7 giữ baseline framework, SDK, runtime, EF Core, Npgsql, PostgreSQL, model Gemini, schema và migrations. Phần source production thay đổi là bước che thông tin xác thực trước khi gửi payload sang AI. Gemini live, nghiệm thu các provider ngoài, toàn bộ ma trận PF-AUTHZ-04, Final Local Acceptance và điều kiện phát hành công khai về runtime tiếp tục được theo dõi riêng. [Điều kiện triển khai](../deployment/README.md) · [Đánh giá runtime](propflow-runtime-security-risk-assessment.md)
