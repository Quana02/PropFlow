# Trang thông tin công khai — Triển khai và kiểm thử

## A. Tổng quan

Thêm bốn tuyến thông tin công khai bằng Blazor WASM. Hai văn bản pháp lý hiển thị trạng thái dự thảo trong mọi môi trường. Help lọc nội dung tĩnh tại client. Contact phân tuyến theo nghiệp vụ và nêu rõ thông tin kênh công khai chưa được xác nhận.

Mốc Git khi bắt đầu là `cbbf2eb`. Kết quả bảo mật và hồi quy của các lượt trước được ghi riêng. [Bảo mật và hồi quy](security-privacy-regression.md) · [Rà soát pháp lý](legal-privacy-review.md) · [Điều kiện phát hành](release-readiness.md).

## B. Tệp và tác động

| Vị trí | Mục đích |
| --- | --- |
| `Features/PublicInformation/{Terms,Privacy,Help,Contact}.razor` | Tuyến và nội dung public. Terms/Privacy dùng PolicyDocument và PolicySection cho mục lục, heading và văn bản. |
| `Features/PublicInformation/PublicInformationLayout.razor(.css)`, `InformationHeader.razor` | Header, điều hướng công khai, panel và footer. Tái dùng PublicHeader, CSS theo phạm vi component. |
| `Features/PublicInformation/InformationLinks.razor(.css)` | Liên kết dùng chung, hỗ trợ tab mới với noopener và accessible label. |
| `Features/PublicInformation/HelpContent.cs`, `HelpSearch.cs`, `Help.razor.css`, `Contact.razor.css`, `PolicyDocument.razor.css` | FAQ, chuẩn hóa dấu tiếng Việt và CSS theo trang. |
| `_Imports.razor`, `AuthBootstrap.razor` | Đăng ký namespace và cho các tuyến thông tin render trong lúc restore session. Role-home redirect vẫn giới hạn ở Landing/Login. |
| `Home.razor`, `Login.razor`, `Register.razor`, `ForgotPassword.razor`, `ResetPassword.razor`, `Otp.razor`, `ResumeRegistration.razor`, `Account.razor` | Thêm liên kết ở điểm chạm công khai và tài khoản. |
| `AppShell.razor`, `ResidentPortalLayout.razor` | Thêm liên kết trong footer hiện có của mỗi layout. |
| `tests/PropFlow.UnitTests/PublicInformationTests.cs` | Metadata route, access, rendering, draft, link và tìm có/không dấu. |
| `docs/public-information/` | Bàn giao nội dung, FAQ, checklist duyệt và phân tuyến. |

## C. Tuyến và quyền đọc

| Tuyến | Anonymous / không đăng nhập được | RESIDENT | STAFF | ACCOUNTANT | MANAGER | ADMIN |
| --- | --- | --- | --- | --- | --- | --- |
| /terms | Public | Public | Public | Public | Public | Public |
| /privacy | Public | Public | Public | Public | Public | Public |
| /help | Public | Public | Public | Public | Public | Public |
| /contact | Public | Public | Public | Public | Public | Public |

Chrome đã mở trực tiếp từng tuyến khi đăng xuất và trong phiên của cả năm vai trò. Nội dung là dữ liệu tĩnh đóng gói trong client. Không có public content API, endpoint biên tập, CTA gửi contact, thêm claim, migration hoặc lưu acceptance.

## D. Vị trí liên kết

| Điểm chạm | Liên kết | Mở |
| --- | --- | --- |
| Landing | 4 link trong footer hiện có | Cùng tab |
| Login | 4 link dưới form | Cùng tab |
| Register | 4 link dưới form | Tab mới |
| Recovery, OTP, Resume | Help và Contact cạnh hướng dẫn | Tab mới |
| Account của cả 5 role | 4 link dưới phần phiên làm việc | Tab mới |
| Resident Portal | 4 link trong footer của Portal | Tab mới |
| Internal AppShell | 4 link trong footer, rail giữ thứ tự cũ | Tab mới |
| 4 trang public | Điều hướng ngang và footer | Cùng tab |

Giữ draft form bằng cách giữ nguyên tab gốc, không lưu thêm thông tin hồ sơ, mật khẩu hoặc OTP vào browser storage.

## E. Giao diện

Dùng Inter qua `--pf-font`, màu xanh theo `--pf-accent` và các token `--pf-menu-surface`, `--pf-list-heading`, `--pf-line`, `--pf-text`, `--pf-muted`. Panel dùng viền mảnh và góc bo tương tự Landing/Auth và các danh sách quản lý. Mục lục và cột liên quan chuyển về một cột trên màn hình nhỏ. FAQ dùng nút filter có aria-pressed, input có label, kết quả thông báo qua role status và accordion native details/summary. Không thêm thư viện icon/CSS. Header dùng lại icon hiện có.

## F. Độ chính xác nội dung

FAQ có nguồn theo từng nhóm trong [bản đồ nội dung](../public-information/resident-help-content-map.md). Điều khoản dùng quy tắc owner hoặc head TENANT đủ điều kiện, xác minh tài khoản và đối soát. Privacy tách email đăng nhập khỏi liên hệ, nêu điều kiện dùng các tích hợp và ghi rõ điểm chưa xác minh. Contact tách kênh public khỏi chức năng cần đăng nhập. [Phân tuyến](../public-information/support-contact-routing.md).

FAQ hướng dẫn cư dân hỏi Ban quản lý về kênh bổ sung tệp đã được xác nhận. Chi tiết giới hạn của biểu mẫu FE-05 nằm ở mục K.

## G. Trạng thái pháp lý

Dự thảo chưa ban hành. Chưa có đơn vị vận hành, kênh chính thức, ngày hiệu lực hoặc điều kiện công bố đã duyệt. [Checklist duyệt](../public-information/legal-content-review-checklist.md). Đối chiếu văn bản chính thức [Luật 91/2025/QH15](https://vanban.chinhphu.vn/?docid=214590&pageid=27160) và [Nghị định 356/2025/NĐ-CP](https://vanban.chinhphu.vn/?docid=216387&pageid=27160).

## H. Build và kiểm thử

Solution build thành công, 0 cảnh báo và 0 lỗi. Lệnh dùng `--no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -p:BaseOutputPath=bin/phase2-final/ -p:DefaultItemExcludesInProjectFolder=bin/**`. Output riêng giải phóng xung đột tệp build trên Windows với runner trước đó.

| Bộ kiểm thử | PASS | FAIL | SKIPPED | Bằng chứng |
| --- | ---: | ---: | ---: | --- |
| Full unit/component | 909 | 0 | 0 | `artifacts/phase2/unit-confirmed.trx` |
| Architecture | 63 | 0 | 0 | `artifacts/phase2/architecture-confirmed.trx` |
| Frontend JavaScript | 33 | 0 | 0 | Runner Node trong `tests/frontend` |
| Full PostgreSQL integration | 539 | 0 | 0 | `artifacts/phase2/integration.trx` |
| Full BillingCycle | 150 | 0 | 0 | `artifacts/phase2/billing-continued.trx` |

Hai suite PostgreSQL dùng cấu hình Phase 1.1 tại `tests/postgres.runsettings`. Unit có kiểm thử SePay trên PostgreSQL local dành cho test. Hai suite database dùng output riêng `bin/resident-summary-final`. BillingCycle được chạy lại và đạt 150/150. Bản build sau sửa câu hướng dẫn FE-05 đã chạy full unit/component, Architecture và Chrome. Các TRX lịch sử vẫn được giữ để phân biệt kết quả ban đầu với kết quả cuối. Lỗi component test ban đầu đến từ việc truyền `default(ParameterView)` vào bUnit khi kiểm các trang public. Fixture dùng `ParameterView.Empty` cho trường hợp thiếu tham số và giữ nguyên các assertion. Có một timeout của RealtimeRefreshQueue trong lượt cũ. Hai lượt full unit kế tiếp đều đạt 909/909. `git diff --check` đạt.

## I. Browser E2E và giao diện

Google Chrome thật ở chế độ headless qua Chrome DevTools Protocol. API, Web và Interactive WASM dùng PostgreSQL local với DB cô lập, năm tài khoản giả và kho ảnh giả. Log local lưu ở `artifacts/phase2/browser-acceptance.log`. Ảnh và log nằm trong thư mục artifacts bị Git ignore. Chrome, API và Web đã dừng, hai cổng 7101/7201 đã được giải phóng.

| Hạng mục | Kết quả quan sát |
| --- | --- |
| Anonymous và lịch sử | Mở trực tiếp và refresh cả bốn tuyến, theo liên kết Landing → Terms, quay lại Landing và tiến tới Terms. Mục lục đưa đúng phần nội dung vào vùng nhìn. |
| Auth | Login có bốn liên kết và mở được Help. Register mở Terms và Privacy ở tab mới bằng click thật và giữ dữ liệu đã nhập. Forgot, Reset và OTP hiển thị liên kết hỗ trợ trên màn hình truy cập trực tiếp. |
| Account và layout | Đăng nhập bằng cả năm role. Account hiển thị liên kết, bốn tuyến công khai mở được trong phiên, truy cập lại dashboard hoặc Resident Portal thành công. Kiểm tra DOM thấy đủ bốn liên kết trong footer của Resident Portal và Internal AppShell, rail bên phải không chứa bốn tuyến. Ghi ảnh từng Account và từng workspace. `artifacts/phase2/admin-account-links.png` · `artifacts/phase2/resident-account-links.png`. |
| FAQ | Tìm có dấu và không dấu, mở accordion bằng phím Space, đổi nhóm, nhận empty state, xóa từ khóa và thấy nội dung của nhóm đang chọn. |
| Contact và pháp lý | Contact hiển thị hướng dẫn thực tế và các liên kết chức năng, ghi rõ chưa có kênh public công bố. Terms/Privacy đều có nhãn “Chưa ban hành” khi kiểm tra. |
| Network | Lượt E2E năm vai trò ghi nhận 0 lỗi JavaScript không bắt, 0 lỗi HTTP tải tài liệu Web và 0 HTTP 5xx từ API local. |

Icon Trang chủ trong header công khai dùng SVG để tránh hiện tên icon nếu font tải chậm. Nút vẫn giữ nhãn trợ năng. Chụp 24 ảnh theo tổ hợp bốn trang × ba chiều rộng 390/768/1440 px × hai chế độ sáng/tối. Ở 390 px, các liên kết xuống dòng, mục lục và card xếp một cột. Ở 768 px, Contact hiển thị hai cột, khối FAQ và mục lục vẫn đọc theo chiều dọc. Ở 1440 px, mục lục và khối nội dung pháp lý cạnh nhau, FAQ có cột thông tin liên quan. Đối chiếu với Landing/Login về Inter, tiêu đề, nền theo theme, đường viền, khoảng cách, icon header và liên kết. Khối search và accordion dùng cùng màu chữ, nền, viền và trạng thái đang chọn với các phần còn lại. Đối chiếu ảnh chụp sau khi hiệu ứng chuyển theme ổn định. `artifacts/phase2/visual-390.png` · `artifacts/phase2/visual-768.png` · `artifacts/phase2/visual-1440.png`.

## J. Hồi quy xuyên tính năng

| Phạm vi | Cách kiểm tra |
| --- | --- |
| FE-01, AuthBootstrap, InformationLinks | Full component và Chrome. Đối chiếu điều kiện restore session, route được phép hiển thị và role-home redirect. |
| FE-02 ACCOUNTANT privacy, FE-05 evidence xác thực | Đối chiếu đường gọi và các ca tương ứng trong full unit/integration. |
| FE-08/09 phân giải người nhận và enqueue, FE-13 giữ draft | Full unit/integration/BillingCycle gồm các ca recipient snapshot, retry, quyền và draft. |
| Shared Toast/Dialog/Filter/Pagination | Full unit/component và frontend JavaScript, đối chiếu selector/layout dùng chung. |
| RBAC và rail | Full architecture/integration. Bốn tuyến public dùng quyền anonymous. Kiểm tra vị trí footer và rail trên Chrome với cả năm role. |

## K. FE-05 Resident attachment

`ResidentServiceRequestDialog.razor` thực sự hiển thị bộ chọn tệp và vùng thả tệp. Code-behind giữ đối tượng `IBrowserFile` trong `_selectedFiles` và chỉ hiển thị tên sau khi kiểm số lượng, kích thước. Lệnh tạo gửi JSON theo `CreateResidentServiceRequestRequest`, gồm loại yêu cầu, nội dung và thời gian hẹn. DTO và POST trong `ResidentServiceRequestsController` hiện chưa nhận trường tệp. API evidence của Staff là luồng riêng có kiểm tra quyền.

Giao diện cũ dễ khiến cư dân tưởng ảnh đi kèm yêu cầu vì chỉ giải thích sau khi chọn. Đưa thông báo lên trước thao tác, đổi nhãn thành chọn tệp để xem tên và nối thông báo với input qua `aria-describedby`. Ca component FE-05 kiểm phần thông báo, liên kết trợ năng và việc submit vẫn hoạt động. Lượt này giữ hợp đồng API và quyền hiện có.

**Backlog P1 trước pilot có nhu cầu gửi ảnh.** Chốt hành vi với Ban quản lý. Nếu cần nhận ảnh từ cư dân, thiết kế hợp đồng gửi ảnh gắn với yêu cầu, kiểm tra quyền và quyền đọc, giới hạn tệp, lưu trữ riêng, retry và kiểm thử báo lỗi cho người gửi. Nếu chức năng tiếp tục ở trạng thái xem tên cục bộ, bỏ bộ chọn khỏi biểu mẫu gửi.

## L. Việc còn mở và mức sẵn sàng

- **NOT VERIFIED trên Chrome:** Luồng đặt lại mật khẩu với OTP hợp lệ qua email thật, màn hình Reset/OTP có challenge hợp lệ, phiên tài khoản bị khóa và toàn bộ thao tác nghiệp vụ FE-02/05/08/09/13 bằng UI. Các kết quả suite và browser được ghi riêng để thấy phạm vi kiểm chứng.
- **Phê duyệt nội dung:** Xác nhận đơn vị vận hành, thông tin và đầu mối hỗ trợ, nội dung cuối, ngày hiệu lực và quyết định công bố theo [checklist duyệt](../public-information/legal-content-review-checklist.md). Terms/Privacy tiếp tục hiện trạng thái dự thảo.
- **Điều kiện vận hành:** Cloudinary tenant và ảnh public cũ, SMTP thật, SePay live, AI thật, TURN, hosting và log. [Điều kiện phát hành](release-readiness.md).
- **Demo:** Sẵn sàng trình diễn bằng dữ liệu giả với hai văn bản ở trạng thái dự thảo.
- **Pilot và production:** Chờ các điều kiện công bố, vận hành và dữ liệu thật nêu trên. Backlog FE-05 áp dụng cho pilot cần nhận ảnh từ cư dân.
