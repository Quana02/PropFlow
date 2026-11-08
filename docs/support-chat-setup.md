# Chạy thử liên hệ Ban quản lý

## Chuẩn bị

- Áp dụng migration `20261105195224_AddResidentManagerSupportChat` cho `CommunicationDbContext` (schema migration history `communication`). Migration tạo bốn bảng chat riêng, không xóa hoặc chỉnh sửa hồ sơ dịch vụ/thanh toán.
- Chạy lại API và Web sau khi cập nhật code/migration.
- Dùng hai trình duyệt hoặc hai hồ sơ trình duyệt riêng: một tài khoản MANAGER có MANAGE_OPERATIONS, một tài khoản RESIDENT có USE_RESIDENT_SERVICES và liên kết cư trú đang hiệu lực. Hai tab cùng hồ sơ dùng chung refresh cookie nên không phù hợp để giả lập hai người.
- Không cần OpenAI API key hoặc credit cho luồng tiếp nhận và điều phối chat này.

## Thử luồng đầy đủ

1. Manager đăng nhập, bật **Sẵn sàng nhận chat** ở thanh trạng thái trên giao diện vận hành. Mở mục **Chat với cư dân**, đường dẫn `/manager/support-chat`.
2. Cư dân vào trang dịch vụ, chọn **Liên hệ BQL**, hoặc mở `/resident/contact-management`. Khung chat chào và hỏi nội dung cần hỗ trợ.
3. Cư dân gửi một mô tả, ví dụ “Tôi cần Ban quản lý hướng dẫn gửi yêu cầu sửa vòi nước”. Hệ thống tự phân Manager sẵn sàng chưa có cuộc đang hoạt động. Tên người phụ trách xuất hiện trong khung chat.
4. Cư dân tiếp tục gõ nhưng chưa gửi. Manager nhìn thấy nội dung ở ô riêng **Cư dân đang soạn — chưa gửi** phía trên ô trả lời. Nội dung cập nhật theo nhịp khoảng 300 ms khi tiếp tục gõ, không chờ cư dân dừng gõ hoàn toàn.
5. Khi cư dân bấm **Gửi tin nhắn**, tin được lưu vào lịch sử; vùng nháp được xóa. Manager trả lời trong cùng khung; cư dân nhận tin và chỉ báo Manager đang nhập.
6. Mỗi bên có thể **Kết thúc phiên**. Lịch sử vẫn xem lại được. Nếu Manager vẫn sẵn sàng thì hệ thống tự phân cư dân tiếp theo trong hàng chờ.

## Thử hàng chờ và giới hạn người nhận

- Tạm dừng nhận chat hoặc đóng trình duyệt Manager; cư dân mới gửi nội dung phải thấy **Đang chờ Manager**, không tự gán một tài khoản offline.
- Bật sẵn sàng trở lại: yêu cầu chờ được phân tự động, theo thứ tự tạo.
- Một Manager chỉ nhận một cuộc đang hoạt động. Hai cư dân gửi cùng lúc: một người được phân, người còn lại chờ nếu không có Manager khác.
- Manager thứ hai không được xem tin hoặc bản nháp của cuộc đã phân cho người thứ nhất, kể cả biết ID.
- Mở hai tab của cùng Manager, đóng một tab: tab còn lại vẫn được coi là kết nối. Khi toàn bộ tab ngắt, trạng thái online hết hiệu lực theo heartbeat/lease.
- Làm mới trang hoặc mất kết nối rồi quay lại: tin đã gửi vẫn tải từ database. Gửi lại cùng mã gửi không tạo tin trùng.

## Bản nháp trực tiếp và bảo mật

Theo yêu cầu đã xác nhận, Manager được đọc nội dung cư dân đang gõ trước khi gửi. Giao diện cư dân có thông báo rõ. Tính năng chỉ chia sẻ sau khi đã phân người phụ trách và kết nối còn hợp lệ.

Bản nháp không nằm trong database, log, email, thông báo bền vững hoặc prompt AI. Nó chỉ tồn tại trong đường truyền/memory và ô xem tạm của Manager; hết hạn sau khoảng 10 giây không nhận cập nhật, hoặc được xóa khi gửi/kết thúc/mất kết nối. Tải lại không phục hồi bản nháp chưa gửi. Tin đã bấm Gửi được lưu để hai bên xem lịch sử.

Backend kiểm tra lại role/quyền/tài khoản/cư trú và người tham gia cho từng thao tác; Manager không được tự chọn hoặc đọc hàng chờ của cư dân chưa được phân. Chưa có chức năng gọi thoại, gửi file hoặc tự tạo phiếu dịch vụ từ cuộc chat.

## Vị trí code

- UI, client API và kết nối: `src/PropFlow.Web.Client/Features/Communication/SupportChat/`.
- Dịch vụ/điều phối: `src/Modules/Communication/Infrastructure/SupportChatService.cs`.
- Hub/API: `src/Modules/Communication/Presentation/SupportChatHub.cs`, `SupportChatController.cs`.
- Quyền và DTO: `src/Modules/Communication.Contracts/`.
- Kế hoạch và phạm vi: `docs/requirements/FE-13-Resident-Manager-Chat.md`.

Baseline chạy một API process. Khi deploy nhiều instance cần SignalR backplane/dịch vụ SignalR chung để bản nháp đi giữa các instance. Khóa PostgreSQL đảm bảo điều phối không trùng; dữ liệu bền vững và đối soát API vẫn là nguồn sự thật khi tín hiệu realtime bị bỏ lỡ.
