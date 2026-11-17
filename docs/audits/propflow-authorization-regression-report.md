# Kết quả sửa lỗi và kiểm thử phân quyền

Các bảng bên dưới giữ bằng chứng của Full Authorization Audit. Kết quả sau khi thay đổi chính sách lịch sử, phiên và SignalR được ghi theo từng checkpoint trong [Hardening 1.1](propflow-authorization-hardening-1.1-report.md).

Môi trường chạy gồm .NET 8, PostgreSQL loopback, Web/API HTTPS local, Chrome headless qua CDP và tài khoản giả của năm vai trò. Browser dùng một database tạo riêng từ migration và dọn sau phiên. Suite tích hợp dùng `tests/postgres.runsettings` theo Phase 1.1. Kho ảnh ở các ca access dùng ảnh fixture hoặc kho giả. Connector ngoài loopback của API Chrome bị chặn qua cấu hình tiến trình.

Kết quả chạy mới nhất và các phạm vi kế thừa được ghi ở [báo cáo hồi quy Hardening 1.2](propflow-authorization-hardening-1.2-report.md).

Xem [checkpoint kiểm chứng cuối kỳ](propflow-final-authorization-coverage-report.md) để đối chiếu TRX, scope browser và các hạn chế.

## Lệnh và bằng chứng

Thư mục build trong các lệnh là đường dẫn tuyệt đối đến `artifacts/authz/build`. Thư mục `artifacts/` chứa log, ảnh giả và TRX ở máy kiểm thử.

```powershell
$auditBuild = Join-Path (Get-Location).Path 'artifacts/authz/build'
dotnet build PropFlow.sln --artifacts-path $auditBuild --disable-build-servers -m:1 -p:NuGetAudit=false -nologo -v quiet
dotnet build tests/PropFlow.BillingCycleTests/PropFlow.BillingCycleTests.csproj --artifacts-path $auditBuild --disable-build-servers -m:1 -p:NuGetAudit=false -nologo -v quiet
$auditBilling = Join-Path (Get-Location).Path 'artifacts/authz/billing-final'
dotnet test tests/PropFlow.UnitTests/PropFlow.UnitTests.csproj --artifacts-path $auditBuild --no-build --no-restore --settings tests/postgres.runsettings --logger 'trx;LogFileName=UnitTests-latest.trx' --results-directory artifacts/authz
dotnet test tests/PropFlow.ArchitectureTests/PropFlow.ArchitectureTests.csproj --artifacts-path $auditBuild --no-build --no-restore --settings tests/postgres.runsettings --logger 'trx;LogFileName=ArchitectureTests-latest.trx' --results-directory artifacts/authz
node --test tests/frontend/*.test.cjs
dotnet test tests/PropFlow.IntegrationTests/PropFlow.IntegrationTests.csproj --artifacts-path $auditBuild --no-build --no-restore --settings tests/postgres.runsettings --logger 'trx;LogFileName=IntegrationTests-acceptance.trx' --results-directory artifacts/authz
dotnet test tests/PropFlow.BillingCycleTests/PropFlow.BillingCycleTests.csproj --artifacts-path $auditBilling --no-build --no-restore --settings tests/postgres.runsettings --logger 'trx;LogFileName=BillingCycleTests-final.trx' --results-directory artifacts/authz
git diff --check
```

Các biến kết nối kiểm thử được đọc trong tiến trình từ cấu hình local. Dữ liệu kết nối, JWT, private key và mật khẩu test không nằm trong báo cáo. Lượt có VSTest, browser và PostgreSQL chạy ngoài giới hạn loopback của sandbox. Chrome được điều khiển bởi [launcher local](../../artifacts/authz/browser.ps1) và [kịch bản CDP](../../artifacts/authz/browser.mjs). [Fixture Chrome](../../artifacts/authz/fixture/Program.cs) mở rộng fixture Phase 1.1 thành hai nhóm tài nguyên giả độc lập. Các script này nằm trong thư mục bằng chứng local.

## Các bộ kiểm thử

| Bộ | Đạt | Trượt | Bỏ qua | Bằng chứng |
| --- | ---: | ---: | ---: | --- |
| Solution build | PASS | 0 lỗi | — | `PropFlow.sln`, 0 cảnh báo |
| Unit/component | 939 | 0 | 0 | [TRX](../../artifacts/authz/UnitTests-latest.trx) |
| Architecture | 64 | 0 | 0 | [TRX](../../artifacts/authz/ArchitectureTests-latest.trx) |
| Frontend JavaScript | 36 | 0 | 0 | `node --test tests/frontend/*.test.cjs` |
| PostgreSQL integration | 549 | 0 | 0 | [TRX nghiệm thu](../../artifacts/authz/IntegrationTests-acceptance.trx) |
| BillingCycle | 150 | 0 | 0 | [TRX](../../artifacts/authz/BillingCycleTests-final.trx) |

## Tái hiện và bản sửa

| Kiểm tra | Trước sửa | Sau sửa hoặc kết quả |
| --- | --- | --- |
| FE-07 SignalR, STAFF bị gỡ assignment | FAIL, vẫn nhận ID nhiệm vụ từ group cũ | PASS trong integration. MANAGER và STAFF mới vẫn nhận. [TRX trước sửa](../../artifacts/authz/realtime-red.trx) |
| FE-07 ảnh và activity, STAFF ngoài phạm vi | FAIL, 500 thay cho 403. Rà soát thấy thiếu no-store ở hai URL ảnh. | PASS. 401/403/404 đúng trường hợp, không đọc kho khi bị chặn và STAFF/MANAGER đủ quyền vẫn tải ảnh. [TRX trước sửa](../../artifacts/authz/evidence-red.trx) · [TRX sau sửa](../../artifacts/authz/authorization-fixes.trx) |
| FE-09/15 route SePay OTP | Bốn ca HTTP đều 404. | 401/403 cho các actor thiếu quyền. ADMIN đủ quyền tới xử lý yêu cầu OTP với provider tắt và nhận mã nghiệp vụ dự kiến. [TRX trước sửa](../../artifacts/authz/sepay-route-red.trx) · [TRX sau sửa](../../artifacts/authz/IntegrationTests-final.trx) |
| FE-12 metadata nháp | FAIL, token và thời điểm trên bản STAFF đang xem thay đổi khi lưu nháp. | PASS ở unit và PostgreSQL. So sánh list/detail trước và sau khi lưu nháp, cùng đối chứng token mới cho người quản lý. [TRX trước sửa](../../artifacts/authz/knowledge-metadata-red.trx) · [Unit](../../artifacts/authz/UnitTests-latest.trx) |
| Hợp đồng public API | Kiểm kê OpenAPI và mã nguồn cùng 247 method/route | Architecture test xác nhận allowlist public FE-01 và webhook, các action còn lại có metadata auth. |
| Schema DB Phase 1.1 | Integration lần đầu 543 đạt, 1 trượt vì fixture kỳ vọng 72 bảng | Đối chiếu chỉ đọc schema DB migration mới và `propflow_test`: cùng 73 bảng, cùng danh sách. Cập nhật kiểm thử schema giữ assertion số lượng và xác nhận bảng `payments.sandbox_sepay_receipts`. |
| Fixture BillingCycle header cư dân | Hai ca render thuộc commit hiện có trượt sau khi header thêm weather/avatar và đổi sang tải phí khi mở menu | Fixture dùng DI cho child component và kích hoạt click thật qua renderer. Giữ kiểm tra đường dẫn, số tiền theo căn hộ, bỏ tiền thuê, trạng thái lỗi và dữ liệu ví. |
| Fixture SignalR gọi khẩn cấp | Một lượt integration trả 400 ngay sau khi SignalR client hoàn thành handshake. | Fixture đợi Heartbeat của hai kết nối để bảo đảm đã đăng ký presence trước khi kiểm tra phân quyền. Hai ca realtime chạy lại đạt. Độ ổn định thao tác gọi ngay khi WebSocket vừa kết nối chưa được kiểm chứng riêng trên Chrome. [TRX chạy lại](../../artifacts/authz/EmergencyRealtime-recheck.trx) |

Lượt integration nghiệm thu chạy sau BillingCycle, trên fixture đã đồng bộ presence. Điểm sửa FE-12 được xác nhận ở unit và PostgreSQL. Smoke Chrome dùng DB theo phiên chạy. API, Web và Chrome đã dừng sau từng lượt, và hai cổng kiểm toán đã được giải phóng.

## Kết quả theo bề mặt

- **Chrome 378/378 lượt mở URL**. 63 route ứng dụng với năm vai trò và lượt ẩn danh, gồm 147 kết quả cho phép hoặc chuyển tiếp hợp lệ và 231 kết quả từ chối hoặc yêu cầu đăng nhập. Hai route kỹ thuật được ghi riêng. STAFF nhập URL `/admin/dashboard` bị chặn ở direct URL, tải lại, quay tới qua lịch sử và tab mới. Log của ca STAFF không có request nghiệp vụ ADMIN và không có lỗi JS được ghi nhận. [Ma trận Chrome](../../artifacts/authz/browser-matrix.json) · [Ảnh tab mới](../../artifacts/authz/staff-admin-new-tab-denied.png).
- **HTTP 1.073/1.073 probe từ chối đúng status**. Có 236 kết quả 401 và 837 kết quả 403. Các probe kiểm lớp chính sách trên API chạy thật với token local. Các ô ALLOW, payload mutation đầy đủ và so sánh side effect cần ca nghiệp vụ riêng. [Log probe](../../artifacts/authz/api-matrix.json) · [Ma trận tài nguyên](propflow-resource-authorization-matrix.md).
- **JWT 35/35**. Mỗi role có một đối chứng token hợp lệ và sáu ca lỗi gồm malformed, role bị sửa, chữ ký sai, issuer sai, audience sai và hết hạn. Artifact chỉ lưu kết quả status. [Log JWT](../../artifacts/authz/jwt-matrix.json).
- **Session STAFF**. Ghi role ADMIN và permission vào browser storage vẫn bị chặn ở URL ADMIN. Logout từ giao diện trả 204. Mở lại trang STAFF bằng URL trực tiếp đưa về đăng nhập. Đây là bằng chứng thu hồi refresh session và điều hướng client. [Log vòng đời](../../artifacts/authz/browser-session-lifecycle.json) · [Ảnh sau logout](../../artifacts/authz/staff-after-logout.png).
- **Cross-account 28/28**. Hai STAFF dùng assignment riêng cho yêu cầu dịch vụ, phản ánh và bảo trì. Hai RESIDENT dùng căn hộ, yêu cầu dịch vụ, phản ánh, hóa đơn và hội thoại riêng. Mỗi tài khoản thử cùng tài nguyên hợp lệ của mình và ID của tài khoản bên kia. [Log quyền theo ID](../../artifacts/authz/horizontal-matrix.json) · [ID fixture giả](../../artifacts/authz/browser-matrix-resources.json).
- **UI FE-07**. Tab mặc định vẫn là Nhiệm vụ của tôi, tab Tất cả nhiệm vụ mở được và bảng STAFF không hiển thị các thao tác quản lý đã kiểm. Bố cục vừa viewport 390, 768 và 1440 px trong ảnh light/dark. [Log thao tác và bố cục](../../artifacts/authz/browser-ui-actions.json).
- [Ảnh STAFF bị chặn khi nhập URL ADMIN](../../artifacts/authz/STAFF-denied.png). [Ảnh STAFF mở trang được giao](../../artifacts/authz/STAFF-allowed.png). [Ảnh ẩn danh mở điều khoản](../../artifacts/authz/anonymous-terms.png).

## Mức sẵn sàng

- Demo local có bằng chứng Chrome và API trên dữ liệu giả. Các ca quyền, lỗi hạ tầng và các đường dẫn còn thiếu bằng chứng nằm trong ma trận tương ứng.
- Nghiệm thu phân quyền toàn phần cần tiếp tục fixture cho mỗi mutation và từng quan hệ cross-account, cùng kiểm chứng đóng kết nối đang mở sau thu hồi.
- Pilot dữ liệu thật và production cần xác nhận Cloudinary legacy/provider, TURN, SMTP và SePay ngoài loopback theo [điều kiện phát hành](release-readiness.md). Quyền lịch sử FE-07 sau thu hồi phân công cần quyết định nghiệp vụ.
