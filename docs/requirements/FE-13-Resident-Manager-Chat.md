# FE-13 — Liên hệ Ban quản lý qua chat

Scope extension theo yêu cầu người dùng ngày 06/11/2026; không tự thêm UC vào catalogue 56 UC cũ. Liên quan FE-12 ở bước tiếp nhận nhưng Communication sở hữu hội thoại giữa người dùng. Không gọi LLM để quyết định người nhận.

## Kế hoạch và luồng nghiệm thu

1. Nút Liên hệ BQL trong danh mục dịch vụ dẫn tới `/resident/contact-management`. Khung chat chào và hỏi cư dân cần hỗ trợ gì, sau đó gửi mô tả vào hàng chờ.
2. Manager có mục `/manager/support-chat` và thanh trạng thái ở giao diện vận hành. Bật **Sẵn sàng nhận chat** khi muốn tiếp nhận. Một Manager xử lý một cuộc trò chuyện đang hoạt động; khi kết thúc được nhận cuộc tiếp theo.
3. Manager rảnh = tài khoản ACTIVE, đúng role/quyền hiện hành, đã bật sẵn sàng, có kết nối SignalR còn heartbeat và chưa có hội thoại ACTIVE. Chọn người lâu chưa nhận chat nhất; hàng chờ theo thứ tự tạo. Ghép trong giao dịch khóa PostgreSQL, có unique index để chống nhận trùng.
4. Không có Manager rảnh: cư dân thấy đang chờ, vẫn gửi thêm tin hoặc kết thúc yêu cầu chờ được. Manager bật sẵn sàng hoặc kết thúc phiên sẽ kích hoạt ghép tiếp.
5. Tin đã gửi lưu database, có phân trang và khóa chống gửi trùng. Cư dân chỉ xem hội thoại của chính tài khoản/liên kết cư trú hợp lệ; Manager chỉ xem hội thoại được phân cho mình. Các role còn lại không được đọc hoặc tham gia.
6. Người dùng xác nhận Manager được xem **nội dung cư dân đang gõ trước khi gửi**. UI cư dân thông báo rõ. Bản nháp chỉ gửi qua SignalR đến Manager đã được phân; không ghi database, log, thông báo hoặc chuyển sang OpenAI. Gửi trạng thái mới nhất theo nhịp khoảng 300 ms, kể cả khi gõ liên tục; giới hạn độ dài/tần suất và tự xóa ở giao diện Manager khi hết hạn, gửi tin, mất kết nối hoặc kết thúc phiên. Manager chỉ truyền chỉ báo đang nhập cho cư dân.
7. Mỗi bên có thể kết thúc phiên. Khi Manager mất kết nối, cuộc đang xử lý vẫn giữ người phụ trách; cư dân được báo tình trạng và có thể kết thúc rồi mở phiên mới. Không tự chuyển nội dung cho Manager khác.
8. Realtime là tín hiệu cập nhật; API/database là nguồn sự thật. Reconnect tải lại trạng thái, có đối soát định kỳ. Tin nhắn không bị mất khi push lỗi hoặc trình duyệt tải lại. Tín hiệu thay đổi không chứa nội dung; draft trực tiếp phải kiểm tra lại quyền từng lần.
9. Không triển khai thoại, file đính kèm, chat nhóm hoặc tự tạo yêu cầu dịch vụ trong thay đổi này. Không gửi email/Slack ra ngoài.

## Contracts

Communication.Contracts chia sẻ DTO và policy với Web.Client. Quyền sẵn có: RESIDENT + USE_RESIDENT_SERVICES; MANAGER + MANAGE_OPERATIONS. Fresh directory qua public contracts Authentication/Administration/Residents và timezone hiện hành; không map entity xuyên module.

API `/api/v1/support-chat`: GET conversations (phân trang), POST conversations (tin đầu), GET conversations/{id}, GET conversations/{id}/messages (cursor), POST conversations/{id}/messages, POST conversations/{id}/close. Manager GET availability / PUT availability. Hub `/hubs/support-chat`: heartbeat, ResidentDraft, ManagerTyping. Không nhận danh tính hoặc Manager đích từ client.

Trạng thái hội thoại WAITING/ACTIVE/CLOSED. Kết thúc không xóa lịch sử. Tin đầu và các tin sau có ClientMessageId; cùng ID khác nội dung trả 409. Mỗi tài khoản cư dân tối đa một cuộc chưa kết thúc; mỗi Manager tối đa một cuộc ACTIVE. Dữ liệu thuộc một deployment chung cư, không thêm Building selector.

Heartbeat mỗi 20 giây, lease 75 giây; dọn lease hết hạn và điều phối mỗi 5 giây. Nhiều tab Manager được tính chung một người; đóng một tab không làm mất kết nối các tab còn lại. Tắt sẵn sàng chặn nhận mới, không kết thúc cuộc đang hỗ trợ.

## Thao tác trên tin nhắn

- Manager và cư dân có thể trả lời tin trong cùng hội thoại, xem chi tiết và chọn nhiều tin để sao chép, chuyển tiếp, thu hồi hoặc xóa ở phía mình. Chỉ người gửi mới có thể thu hồi tin. Thu hồi ẩn nội dung và phần trích dẫn ở cả hai phía. Xóa ở phía mình chỉ ẩn tin trong lịch sử của người thực hiện.
- Chuyển tiếp tối đa 20 tin mỗi lần, từ lịch sử được phép xem đến hội thoại hỗ trợ đang mở mà người gửi tham gia. Bản chuyển tiếp chỉ mang nội dung và nhãn chuyển tiếp. Bản đó có vòng đời riêng nếu tin gốc được thu hồi sau này.
- **Đã gửi** khi API xác nhận lưu. **Đã nhận** khi ứng dụng bên kia tải được tin trong hội thoại. **Đã xem** khi tin hiện trong vùng đọc và tab hội thoại có tiêu điểm. Chi tiết tin hiển thị thời điểm các xác nhận đã có.
- Dưới `conversations/{id}/messages`, `POST` chấp nhận `replyToId` tùy chọn. `GET {messageId}` đọc chi tiết, `PUT {messageId}/recall` thu hồi một tin. `PUT recall` thu hồi tối đa 50 tin trong một giao dịch sau khi xác minh quyền với toàn bộ danh sách. `POST hide` và `POST receipts` nhận tối đa 50 ID. `POST forward` nhận hội thoại đích và cặp ID tin gốc / mã gửi để chống tạo bản sao khi thử lại.

## Kiểm thử và bàn giao

Unit: lifecycle, validation, quyền, idempotency, không lưu draft. PostgreSQL: hai cư dân tranh cùng Manager, hai Manager tranh cùng hàng chờ, chỉ mục unique, rollback, phân trang và quyền đoán ID. Hub: draft chỉ tới Manager được phân, đóng phiên/đổi quyền chặn truyền tiếp. UI: lời chào, thông báo bản nháp, gửi/nhận, hàng chờ, mất kết nối và kết thúc. Build API/Web, chạy migration có sao lưu; dừng mọi tiến trình thử do agent tạo.

Tham khảo kỹ thuật: [SignalR users/groups](https://learn.microsoft.com/en-us/aspnet/core/signalr/groups?view=aspnetcore-8.0), [SignalR authorization](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-8.0). Group không thay thế kiểm tra quyền; token hết hạn đóng kết nối và mọi thao tác nhạy cảm kiểm tra lại tài khoản hiện hành.

Bản triển khai này theo baseline một API process. Nếu deploy nhiều API instance, cần cấu hình SignalR backplane hoặc dịch vụ SignalR dùng chung trước khi sử dụng bản nháp trực tiếp giữa các instance. Tin đã gửi vẫn nằm trong PostgreSQL và có thể đối soát qua API; khóa điều phối database không thay thế backplane.
