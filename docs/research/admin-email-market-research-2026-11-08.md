# Quyền thay đổi email trong phần mềm quản lý chung cư và bất động sản

Ngày đối chiếu: 08/11/2026. Nghiên cứu bên dưới ghi nhận các phương án thị trường và lịch sử đề xuất. **Quyết định mới nhất của người dùng:** đơn giản hóa thành mật khẩu hiện tại + xác minh email mới; không yêu cầu OTP email cũ. Quyết định triển khai được ghi trong [FE-01](../requirements/FE-01-API-and-Deployment.md#admin-tự-đổi-email--quyết-định-08112026). Các mục đề xuất OTP kép bên dưới đã được thay thế bởi quyết định này, không phải yêu cầu triển khai hiện hành.

## Câu hỏi và giới hạn

Phân biệt hai nhu cầu: (1) quản trị viên/nhân viên tự đổi email tài khoản của mình; (2) quản trị viên đổi email của người khác. Đồng thời phân biệt email đăng nhập/khôi phục tài khoản với email liên hệ trong hồ sơ nghiệp vụ.

Nguồn là tài liệu do nhà cung cấp công bố. Không đăng nhập sản phẩm để thử nghiệm. Đây là mẫu đối chiếu có chủ đích, không phải thống kê thị phần; không đủ cơ sở nói “đa số thị trường”. Tài liệu không đề cập một quyền không có nghĩa sản phẩm không hỗ trợ quyền đó. Vai trò giữa các sản phẩm không tương đương hoàn toàn.

## Kết quả đã xác minh

| Sản phẩm | Tự đổi email | Đổi email của người khác | Điều kiện và giới hạn nguồn |
|---|---|---|---|
| DoorLoop | Có luồng tự đổi **login email** cho User. | Có luồng phục hồi email của User khác cho account owner/full access. | Tự đổi yêu cầu mật khẩu hiện tại và mã SMS. Đổi hộ phải inactive tài khoản, sửa email rồi active lại, xác minh SMS của người thao tác và gửi activation mới; không áp dụng tài khoản account owner. [Tự đổi](https://support.doorloop.com/en/articles/8250163-update-your-login-email), [đổi hộ](https://support.doorloop.com/en/articles/8392378-reset-a-user-s-email-address). |
| TurboTenant | Có. Account owner, Team Admins và Assistants có thể vào profile của chính mình để cập nhật email. | Bài này không xác nhận admin đổi email của thành viên khác. | Cần xác nhận mật khẩu và mở email mới để hoàn tất; email chưa cập nhật đầy đủ trước khi xác nhận. Tài liệu tách email profile với thông tin liên hệ trên tin cho thuê. [Nguồn](https://support.turbotenant.com/en/articles/4003976-how-do-i-update-my-contact-information-as-a-landlord). |
| StuRents | Có đối với **landlord/agent user**, email này dùng đăng nhập. | Nguồn không xác nhận quyền đổi hộ. | Phải xác minh email mới bằng mã hoặc link; **owner user đã active** là ngoại lệ không đổi được theo bài. Giữ nguyên cách gọi vai trò của nhà cung cấp, không đồng nhất landlord và owner. [Nguồn](https://support.sturents.com/kb/guide/en/change-my-email-address-N32eCfTVMT/Steps/2842436). |
| TownSq | Nguồn được chọn không xác nhận admin tự đổi email. | Có: admin sửa email cư dân **chưa từng đăng nhập**, rồi gửi lời mời đến email mới. | Release ngày 24/10/2023; người đã đăng nhập ít nhất một lần được hỗ trợ reset mật khẩu. Không suy diễn thành quyền đổi email mọi tài khoản đã active. [Nguồn](https://www.townsq.io/new-releases-and-updates/ability-to-edit-homeowners-email-and-resend-their-password). |
| uCondo | Chưa xác minh cho admin. | FAQ hướng dẫn cư dân liên hệ síndico/administradora để cập nhật email truy cập. | Có **mâu thuẫn tài liệu**: bài quản lý danh sách người nói email không sửa được, phải xóa đăng ký và tạo lại. Chỉ kết luận có hướng xử lý qua BQL, chưa khẳng định cơ chế sửa trực tiếp hay điều kiện xác minh. [FAQ cư dân](https://suporte.ucondo.com.br/d%C3%BAvidas-frequentes-moradores), [bài chỉnh sửa hồ sơ](https://suporte.ucondo.com.br/cadastros-como-editar-informa%C3%A7%C3%B5es-das-pessoas-do-condom%C3%ADnio). |

DoorLoop còn nói rõ không thể sửa login email khi User đang active, củng cố việc đổi hộ được tách khỏi form sửa hồ sơ thông thường. [Edit Users on Your DoorLoop Account](https://support.doorloop.com/en/articles/6349865-edit-users-on-your-doorloop-account).

TenantCloud cũng có luồng đổi email đăng nhập và xác minh email mới, nhưng bài tìm được nằm trong nhóm **Tenant**. Vì vậy không dùng bài này làm chứng cứ cho quyền admin. [How do I change my email address?](https://support.tenantcloud.com/en/articles/11931494-how-do-i-change-my-email-address).

## Giới hạn đối chiếu sản phẩm Việt Nam

HomeID mô tả cư dân tự thay đổi email trong thông tin cá nhân; nội dung này chưa chứng minh đó là email đăng nhập hoặc quyền của admin. [Quản lý cư dân trên HomeID, mục III](https://homeid.asia/quan-ly-cu-dan-chung-cu-chuc-nang-tren-phan-mem-homeid/).

Building Care mô tả quản lý tài khoản nhân viên/cư dân, tìm theo email và khôi phục mật khẩu, nhưng bài này không xác nhận quyền đổi email. Không suy luận là sản phẩm không hỗ trợ. [Hướng dẫn quản lý tài khoản](https://buildingcare.biz/huong-dan-quan-ly-tai-khoan-nhan-vien-va-cu-dan-cho-ban-quan-ly-toa-nha/).

## Nhận định từ mẫu khảo sát

Các ví dụ trên xác nhận việc cho người quản lý tự đổi email tài khoản là một lựa chọn thực tế trên thị trường. Quyền đổi email người khác có thể tồn tại nhưng phụ thuộc cấp quyền, trạng thái kích hoạt và luồng phục hồi; không có bằng chứng về một quy tắc thống nhất cho mọi sản phẩm. Đây là suy luận từ các nguồn đã liệt kê, không phải kết luận định lượng.

## Đối chiếu bổ sung: xác minh email cũ và email mới

Theo yêu cầu làm rõ của người dùng, phương án cần xem xét cho PropFlow là **mật khẩu hiện tại + OTP gửi email cũ + OTP gửi email mới**. Đây là lựa chọn sản phẩm của PropFlow; không được diễn đạt thành quy tắc chung của thị trường.

| Nguồn chính thức | Bằng chứng | Giới hạn áp dụng |
|---|---|---|
| [DoorLoop — tự đổi login email](https://support.doorloop.com/en/articles/8250163-update-your-login-email) | Mật khẩu hiện tại và mã xác minh gửi điện thoại. | Cùng ngành bất động sản nhưng không chứng minh OTP email cũ + email mới. |
| [TurboTenant — cập nhật profile](https://support.turbotenant.com/en/articles/4003976-how-do-i-update-my-contact-information-as-a-landlord) | Mật khẩu tài khoản và xác nhận email mới trước khi hoàn tất. | Chứng minh xác minh hộp thư mới; bài không nêu OTP gửi email cũ. |
| [Zoom — Changing the email associated with your account](https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0063624) | Người dùng phải xác nhận ở cả email cũ và mới; email giữ nguyên nếu chưa xác nhận cả hai. Luồng bắt đầu ở profile, nhập email mới và mật khẩu đối với work-email login, xác nhận email cũ rồi email mới. | SaaS hội nghị trực tuyến, **khác ngành**. Zoom sử dụng liên kết xác nhận, không phải bằng chứng về hai OTP số. SSO có quy trình riêng. |

Trong mẫu đã xác minh, chưa có nguồn đủ chắc để kết luận một sản phẩm quản lý bất động sản bắt buộc OTP số ở **cả hai** hộp thư cho admin tự đổi email. Zoom là tiền lệ rõ cho nguyên tắc xác nhận hai hộp thư, còn cách dùng OTP số là thiết kế được đề xuất cho PropFlow. Không dùng tài liệu Notion cũ làm bằng chứng: tài liệu hiện tại nói thêm email đã xác minh và chọn primary, không chứng minh OTP email cũ.

## Đối chiếu PropFlow và đề xuất chưa duyệt

Hiện tại [FE-01](../requirements/FE-01-API-and-Deployment.md) quy định hồ sơ tự sửa chỉ gồm tên hiển thị và điện thoại; username/email/role/status chỉ đọc. Login dùng username/password; email tham gia xác minh và khôi phục tài khoản. [Contract hiện tại](../../src/Modules/Authentication.Contracts/AuthContracts.cs) không có email trong `UpdateProfileRequest`. Nghiên cứu thị trường không tự thay thế quyết định nghiệp vụ này.

Đề xuất để thảo luận:

1. Phạm vi đề xuất trước mắt là **ADMIN tự đổi email của chính mình**, bằng hành động riêng trong “Tài khoản của tôi”, với mật khẩu hiện tại, OTP email cũ và OTP email mới. Không mở rộng mặc định cho mọi vai trò ban quản lý.
2. Quyền đổi email người khác nên là luồng quản trị/phục hồi riêng với phạm vi người được thao tác xác định rõ, không mặc định cấp cho mọi thành viên ban quản lý. DoorLoop và TownSq là hai ví dụ về hạn chế theo quyền hoặc trạng thái tài khoản.
3. Nên bổ sung thông báo email cũ/mới, nhật ký người thao tác và kiểm tra email trùng. Đây là **khuyến nghị thiết kế cho PropFlow**, không khẳng định toàn bộ đối thủ đều làm như vậy.
4. Với cư dân, cần quyết định rõ quan hệ giữa email của `UserAccount` và email hồ sơ `Resident`; không tự đồng bộ chỉ vì hai trường cùng tên, vì hiện tại email còn tham gia đối chiếu đăng ký.

Chưa triển khai, chưa bổ sung endpoint/permission, chưa chỉnh FE-01. Nếu chọn hỗ trợ đổi email, cần cập nhật quyết định nghiệp vụ và contract trước khi sửa mã.

## Luồng cụ thể đề xuất cho ADMIN tự đổi email

Trang profile ADMIN đề xuất hiển thị username, email hiện hành, vai trò và trạng thái ở chế độ chỉ đọc; tên hiển thị và số điện thoại được chỉnh sửa theo FE-01. “Đổi email” và “Đổi mật khẩu” là hai hành động riêng, tránh lưu email nhạy cảm chung với form cập nhật thông tin liên hệ.

1. ADMIN đang đăng nhập mở “Tài khoản của tôi” → “Đổi email”, nhập email mới và mật khẩu hiện tại. Email hiện tại phải đã xác minh; email mới phải hợp lệ, khác email hiện tại và không xung đột tài khoản khác.
2. Gửi OTP đến **email cũ** để chấp thuận yêu cầu. Email hiện hành chưa thay đổi.
3. Sau khi OTP email cũ hợp lệ, gửi OTP khác đến **email mới** để chứng minh quyền truy cập hộp thư mới.
4. Khi cả hai bước hợp lệ, cập nhật email và trạng thái đã xác minh trong một giao dịch; kiểm tra lại email hiện hành, trạng thái/quyền ADMIN và tính duy nhất tại thời điểm ghi. OTP/proof dùng một lần. Đổi email đích hoặc hủy yêu cầu sẽ hủy bằng chứng gắn với yêu cầu cũ.
5. Ghi nhật ký thao tác, gửi thông báo kết quả đến cả hai địa chỉ, thu hồi các phiên và yêu cầu đăng nhập lại bằng **username hiện có**. Cần thực thi việc thu hồi cả quyền sử dụng access token còn hạn, không chỉ refresh token. Username, quyền và lịch sử nghiệp vụ không đổi.

Đề xuất dùng lại chính sách OTP FE-01: 6 chữ số, hết hạn sau 5 phút, tối đa 5 lần thử, gửi lại sau 60 giây. **Tổng tối đa 5 lần gửi mỗi giờ trên tài khoản cho toàn bộ luồng đổi email**, cộng cả hai bước và lần gửi lại; không tách thành 5 lần cho mỗi hộp thư. Đây là đề xuất cho luồng mới, chưa khẳng định cơ chế hiện tại đã áp dụng giới hạn tổng này. [Chính sách FE-01](../requirements/FE-01-API-and-Deployment.md).

Mã hiện có [PrivilegedActionOtp](../../src/Modules/Authentication/Application/PrivilegedActions/PrivilegedActionOtp.cs) gửi OTP đến email hiện tại đã xác minh và ràng buộc user/purpose/payload hash. Có thể tham khảo/tái sử dụng nguyên tắc này cho bước email cũ; mã hiện tại **không phải luồng đổi email hoàn chỉnh**, không tự chứng minh đã hỗ trợ gửi OTP email mới. Bằng chứng của hai bước cần gắn với cùng tài khoản, email cũ, email mới và một yêu cầu đổi email; không dùng chéo giữa các yêu cầu.

Nếu ADMIN mất quyền truy cập email cũ, luồng tự đổi ở trên không hoàn tất được. Quy trình khôi phục riêng (ai xác minh danh tính, ai có quyền xử lý, bằng chứng nào được chấp nhận) là **quyết định còn thiếu**, chưa có trong đề xuất triển khai; không tự cho phép bỏ OTP cũ hoặc hứa rằng hỗ trợ viên có thể đổi hộ. Chính Zoom cũng tách trường hợp mất email cũ khỏi luồng thông thường. [Nguồn Zoom](https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0063624).
