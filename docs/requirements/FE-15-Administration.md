# FE-15 — Quản trị hệ thống và nhật ký

Quyền cố định bổ sung cho quản lý email tài khoản cư dân trong FE-01.6:

| Quyền | MANAGER | ADMIN / STAFF / ACCOUNTANT / RESIDENT |
|---|---|---|
| `READ_RESIDENT_ACCOUNTS` | Xem danh sách tài khoản gắn với hồ sơ FE-02 của deployment | — |
| `REQUEST_RESIDENT_EMAIL_CHANGE` | Khởi tạo yêu cầu sau khi đối chiếu danh tính trực tiếp | — |

Quyền được seed theo migration Administration và gắn duy nhất với MANAGER. Bước xác minh dành cho chính tài khoản RESIDENT có yêu cầu còn hiệu lực. [Hợp đồng FE-01](FE-01-API-and-Deployment.md#đổi-email-cư-dân-theo-yêu-cầu-ban-quản-lý)

Ngày 04/11/2026. Người dùng phê duyệt mở rộng màn hình nhật ký quản trị thành nhật ký các thao tác quan trọng trên website sau khi xem phạm vi chỉ gồm ba loại thay đổi tài khoản nội bộ. Quyết định này mở rộng phạm vi FE-15.9 trong đặc tả cũ; không cấp Admin quyền CRUD nghiệp vụ.

## FE-15.1 — Xem và chỉnh sửa hồ sơ tài khoản Ban quản lý

Trong **Tài khoản Ban quản lý**, ADMIN có quyền `MANAGE_INTERNAL_ACCOUNTS` mở **Xem chi tiết tài khoản** và chọn **Chỉnh sửa thông tin**. Quyền hiện có áp dụng cho tài khoản MANAGER, STAFF và ACCOUNTANT, kể cả khi tài khoản đích bị khóa hoặc vô hiệu hóa. ADMIN dùng **Tài khoản của tôi** của FE-01 để sửa hồ sơ của chính mình.

| API | Yêu cầu | Kết quả |
| --- | --- | --- |
| `GET /api/v1/administration/internal-accounts/{userId}` | ID tài khoản nội bộ | Hồ sơ chỉ gồm `id`, `username`, `displayName`, `email`, `phoneNumber`, `role`, `status`, `emailVerified` |
| `PUT /api/v1/administration/internal-accounts/{userId}/profile` | JSON có đủ `displayName` và `phoneNumber` | Hồ sơ sau khi lưu, cùng định dạng với GET |

Tên hiển thị bắt buộc và tối đa 150 ký tự. Số điện thoại tối đa 20 ký tự, được trim theo FE-01 và phải duy nhất giữa các tài khoản. Gửi `null` hoặc chuỗi trắng để xóa số điện thoại. Trường ngoài hợp đồng, trường bắt buộc bị thiếu và dữ liệu sai trả 400. Số điện thoại trùng trả 409 với `phone_conflict`. API trả 401/403 theo quyền truy cập và 404 khi ID không thuộc phạm vi.

Username, email đăng nhập, vai trò, trạng thái và xác minh email hiển thị ở chế độ chỉ đọc. Đổi email tiếp tục theo FE-01.6. Đổi vai trò và trạng thái tiếp tục qua thao tác FE-15 tương ứng. Form hồ sơ không hỗ trợ thay đổi mật khẩu, OTP hoặc thông tin phiên của tài khoản khác.

Authentication sở hữu thao tác ghi hồ sơ. FE-01 và FE-15 dùng cùng khóa giao dịch theo tài khoản. Khi có hai yêu cầu cập nhật đồng thời, mỗi lần lưu áp dụng trọn cặp tên và điện thoại, với giá trị cuối theo lần lưu thành công sau cùng. Giao dịch FE-15 ghi audit `internal_account_profile_updated` cùng lần lưu, gồm actor, target, thời điểm, kết quả, correlation ID và tên trường thay đổi. Giá trị tên và số điện thoại được che trong audit. Phiên và quyền đang hiệu lực tiếp tục dùng được sau khi lưu hồ sơ.

## FE-15.8 — Liên hệ hỗ trợ công khai

Ngày 24/11/2026: owner chốt P0 trong tối đa ba ngày phát triển. Chỉ hỗ trợ `support.contact_email` và `support.contact_phone`; không generic editor, AI/notification/security/billing/SMTP/Building settings. P1 history/restore chuyên biệt hoãn. Preview trước–sau nằm trong confirmation của form, không có endpoint preview riêng.

- ADMIN phải có fixed permission `MANAGE_SYSTEM_CONFIGURATION`, seed chỉ cho ADMIN. Không tái sử dụng quyền quản lý tài khoản. Bốn role còn lại không được đọc API quản trị hoặc sửa.
- `GET/PUT /api/v1/administration/system-configuration/support-contacts`: typed DTO `email`, `phone`, `expectedVersion` khi PUT; phản hồi `email`, `phone`, `version`. Version là SHA256 của hai row identity/version tokens, không chứa nội dung liên hệ. Trường lạ bị từ chối; body tối đa4096bytes.
- `GET /api/v1/public/support-contacts`: anonymous, chỉ `email`, `phone`, no-store. Consumer thật ở `/contact` và `/help`; login/register tiếp tục dùng liên kết trợ giúp hiện hữu. Lỗi API khác trạng thái chưa công bố.
- Email trim, tối đa254ký tự, validation email và không whitespace/control. Phone cho phép dấu cách/chấm/gạch ngang/ngoặc làm dấu phân cách; chuẩn hóa thành8–15chữ số với dấu+ đầu tùy chọn, input tối đa32ký tự. Không tự suy email/số điện thoại từ tài khoản hoặc hồ sơ.
- Null/whitespace nghĩa là chưa công bố; lưu empty string cho row đã tồn tại, không xóa lịch sử. Chưa từng cấu hình thì không tạo row rỗng. Không thay SMTP/sender/OTP/email đăng nhập/phone cư dân.
- Reuse `administration.system_configurations`; thêm UUID `version` concurrency token. Update bundle dùng transaction + advisory lock cố định, kiểm expectedVersion sau lock, cập nhật cả hai trường hoặc không trường nào. First creation cũng được khóa. Stale version trả409; no-op không đổi token hay tạo audit thay đổi giả.
- Audit hiện có ghi actor/time/correlation và trước–sau của đúng hai key public. Key khác vẫn mask, không mở tất cả ConfigValue. Không có trang lịch sử/khôi phục chuyên biệt trong P0; dùng Nhật ký hệ thống hiện hữu.
- Đọc trực tiếp DB mỗi request, không giữ singleton cache; có hiệu lực lần tải tiếp theo và tồn tại qua host restart. Form ADMIN có validation, loading/error, confirmation/preview, toast và bắt buộc reload sau409.
- Migration mới `AddSupportContactConfiguration` chỉ thêm version column và fixed permission/grant; không thay dữ liệu Hiyori, secret hoặc tài chính. Chưa apply vào DB ứng dụng/AWS trong tác vụ phát triển.

## FE-15.9 — Phạm vi nhật ký và dữ liệu

- Giữ các bản ghi tạo tài khoản nội bộ, đổi vai trò và đổi trạng thái cũ.
- Ghi thêm thay đổi dữ liệu khi xử lý request của website trong các module đã có: cư dân, căn hộ, tòa nhà/tài sản, yêu cầu dịch vụ, khiếu nại, bảo trì, phí/hóa đơn, thanh toán, thông báo, AI và cấu hình được hỗ trợ.
- Ghi kết quả các request thay đổi dữ liệu, bao gồm validation thất bại và bị từ chối quyền. Các request đã có audit thay đổi dữ liệu thành công không sinh thêm một dòng kết quả trùng lặp.
- Ghi các bước đăng ký/xác minh, đăng nhập, đăng xuất, đổi/đặt lại mật khẩu và cập nhật tài khoản. Bỏ qua refresh, CSRF và các thao tác xem trước/kiểm tra kết nối được xác định là chỉ đọc.
- Không ghi mọi GET, lần tải trang hoặc lần bấm nút vào bảng audit. Luồng security logging kỹ thuật hiện hành vẫn độc lập.

Một bản ghi gồm thời điểm UTC, phân hệ, hành động, loại/mã đối tượng, mã người thực hiện, kết quả, mã liên kết thao tác; khi thích hợp có mã HTTP và danh sách trường thay đổi trước–sau. Actor lấy từ authenticated principal hoặc `AuthAuditUserId` do luồng đăng nhập đặt, không lấy từ request body. Đăng nhập thất bại ẩn danh không lưu username/email người dùng đã nhập.

## Bảo vệ nội dung và lịch sử

Giá trị trước–sau được allowlist, gồm trạng thái, vai trò/hình thức cư trú, ngày hiệu lực, mã quan hệ, số căn/tầng và các thông số căn hộ. Nội dung các trường khác được thay bằng `[Đã ẩn nội dung]`. Không copy request/response body, query string, header, cookie, password, OTP, token hoặc payload giấy tờ/ghi chú vào audit. UserAccount và các bảng bảo mật không được snapshot bởi interceptor; các sự kiện xác thực chỉ có metadata kết quả.

Không có chức năng sửa/xóa audit trên giao diện hoặc API này. Không tự đặt retention hoặc xóa lịch sử. Bản ghi cũ vẫn là dữ liệu cũ; không dựng lại các hoạt động quá khứ chưa được ghi nhận.

## Giao dịch và ranh giới module

- `ISystemAuditWriter` là public contract do Administration sở hữu. Composition Root gắn interceptor vào các DbContext; module nghiệp vụ không đọc/ghi bảng của module khác.
- Audit thay đổi EF được thêm trong cùng transaction với `SaveChangesAsync` nguồn; tôn trọng transaction ngoài đã có. Nếu ghi audit lỗi, thay đổi nguồn không được commit bởi transaction do interceptor sở hữu. Nếu workflow ngoài rollback, audit trong transaction đó cũng rollback.
- Các thao tác dùng `ExecuteUpdate`, SQL trực tiếp hoặc không có tracked change vẫn có bản ghi kết quả request; không suy diễn snapshot trước–sau khi không có dữ liệu đáng tin cậy.
- Kết quả request được ghi bằng kết nối riêng trước khi phản hồi được phát hành. Nếu kho audit không ghi được, hệ thống ghi Critical event kỹ thuật với loại lỗi và mã liên kết, không log exception message hoặc payload nhạy cảm. Bản ghi kết quả không thay đổi giao dịch nghiệp vụ đã hoàn tất.
- Không thêm giao dịch toàn request, không giữ transaction khi gọi AI/provider và không thêm cross-module EF navigation. Dùng bảng `administration.audit_logs`/JSONB đã có, không cần migration cho chức năng này.

## API và giao diện

`GET /api/v1/administration/system-activity` chỉ cho ADMIN có policy `ViewAdministrationActivity`; API cũ `/activity` được giữ tương thích với phạm vi tài khoản nội bộ cũ.

Bộ lọc: `module`, `action`, `outcome` (`SUCCESS`, `FAILED`, `DENIED`), `actor` (username/display name hoặc UUID), `dateFrom`, `dateTo`, `page`, `pageSize`. Phân trang phía server, thứ tự timestamp + ID giảm dần. Khoảng ngày bao gồm hai ngày đầu/cuối theo timezone tòa nhà; API trả `timeZoneId` để frontend hiển thị đúng múi giờ. Khoảng ngày sai trả 400.

Màn hình gồm bảng phân hệ/hành động/người thực hiện/đối tượng/kết quả, bộ lọc có nhãn và nút Áp dụng/Đặt lại/Làm mới, trạng thái loading/empty/error, phân trang và hộp chi tiết. Nội dung chi tiết hỗ trợ trước–sau, kết quả HTTP, actor/entity/correlation IDs; hỗ trợ Escape và quản lý focus của dialog. Nhãn menu đổi thành “Nhật ký hệ thống”.

Theo yêu cầu giao diện tiếp theo của người dùng: các điều kiện lọc được gom vào popup “Bộ lọc nhật ký hệ thống”, mở từ nút Bộ lọc trên toolbar gọn. Nút hiển thị số điều kiện đã áp dụng; có Xóa bộ lọc và Làm mới ngoài popup. Mở popup lấy lại điều kiện đang dùng, chỉnh bản nháp; Hủy/Đóng/Escape không áp dụng bản nháp. Đặt lại trong popup chỉ xóa bản nháp; Áp dụng mới cập nhật danh sách và đóng popup. Khoảng ngày sai hiện lỗi trong popup. Đã nghiệm thu Apply/Cancel/Reset, giữ lựa chọn, đếm điều kiện, Escape/focus và viewport 390px; ảnh `filter-popup.png`, `filter-popup-mobile.png` trong thư mục bằng chứng.

## Giới hạn cần hiểu đúng

Phạm vi này ghi các thao tác xử lý qua website/API và các thay đổi EF thuộc request đó. Nó không phải log toàn bộ SQL, SIEM, theo dõi mọi lượt đọc, hay bộ thu thập thay đổi từ thao tác DB/CLI bên ngoài hoặc background job không có HTTP request. Các workflow nền cần event audit riêng nếu được yêu cầu bổ sung. Với thao tác SQL trực tiếp, bản ghi kết quả không chứa snapshot trước–sau giả định.

Các luồng thực tế có thể ghi nhiều dòng cho các đối tượng khác nhau cùng bị thay đổi; dùng correlation ID để nhận biết cùng một thao tác. Nội dung PII và giá trị tài chính ngoài allowlist được ẩn; cần xem hồ sơ nguồn theo quyền nghiệp vụ để đọc nội dung đó.

## Kiểm chứng

Kiểm thử PostgreSQL cô lập bao phủ tạo/sửa loại căn hộ và trước–sau, che PII cư dân, mutation bị từ chối, đăng nhập sai không lộ secret, bốn role không phải Admin bị chặn, đọc log không sinh thêm log, lọc actor/phân hệ/ngày theo timezone và phân trang, cùng trường hợp kho audit lỗi làm rollback thay đổi nguồn. Chạy lại các bộ HTTP FE-01/02/03/15, các unit Admin và architecture. Log và bằng chứng giao diện đặt trong `.codex-tmp/system-audit`, không chứa credential thật.

- Integration hồi quy cuối: **233/233 đạt**, gồm 11 ca mới cho audit.
- Unit Admin/tổng quan/biểu đồ: **34/34 đạt**.
- Architecture: **62/62 đạt**.
- Browser nghiệm thu dùng chính `SystemActivityPanel`, API thật và PostgreSQL cô lập: bộ lọc phân hệ/kết quả/người thực hiện/hành động/ngày, empty state, chi tiết diện tích trước–sau, Tab/Shift+Tab/Escape và phục hồi focus đạt. Viewport 390px không tràn trang; bảng có thể cuộn ngang.
- Bằng chứng: `overview.png`, `detail.png`, `mobile.png`; dữ liệu trong ảnh là dữ liệu thử nghiệm, không phải lịch sử được thêm vào database ứng dụng.

Áp dụng bằng cách build và khởi động lại API cùng Web. Không cần migration mới. Các host có sẵn của người dùng không bị agent dừng; host nghiệm thu và database thử nghiệm được dọn sau kiểm tra.


## Thống nhất giao diện quản trị

- Biểu đồ yêu cầu dịch vụ mới trong 30 ngày hiển thị mỗi ngày một cột, tổng số, số ngày có yêu cầu và mức cao nhất; tooltip cho ngày/số lượng. Bố cục theo chiều rộng container, không tạo thanh cuộn ngang cho biểu đồ và không bỏ ngày khi màn hình nhỏ.
- Link menu chọn đúng tab ngay lần bấm đầu, hỗ trợ query navigation và Back/Forward. `paymentEnvironment` được đọc tại routed page `AdminDashboard`, truyền qua parameter cho component con để tránh thay đổi query subscriber khi Blazor .NET 8 đang thông báo điều hướng. Xem [kịch bản hồi quy](../testing/Admin-Navigation-Regression.md).
- Phân trang nhật ký thống nhất với Tài khoản Ban quản lý: tổng số bên trái; mũi tên trước, số trang hiện tại và mũi tên sau bên phải. Cùng kích thước/màu sắc, trạng thái disabled và bố cục thích ứng; vẫn dùng phân trang server.
