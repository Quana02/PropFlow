# PropFlow — Kiểm chứng hồi quy thông báo realtime

## Kiến trúc

Workflow sở hữu nghiệp vụ và tạo outbox tại transaction nguồn. OperationalNoticeWorker và các dispatcher chuyên biệt chuyển sự kiện sang Communication. Publisher ghi notification in-app và hàng đợi email nếu có địa chỉ phù hợp. Khóa event/recipient chống nhân bản khi retry. Sau commit, publisher gửi `FinanceNotificationCreated` qua user SignalR. Hai alias SignalR dùng cùng lớp hub, hai alias HTTP dùng cùng controller và lọc bằng `sub`. Bell dùng state chung, nhận realtime, tải lại dữ liệu đã lưu khi kết nối lại và khi mở panel. Announcement, workspace invalidation, typing và signaling có vòng đời riêng. Xem đầy đủ nhóm event và actor tại [ma trận tương tác](propflow-notification-interaction-matrix.md).

## Lỗi đã tái hiện

Khi worker FE-05 chạy sau khi đổi phân công A → B → A, dispatcher cũ chỉ xác minh B vẫn là STAFF active. B đã mất phân công từ lần thay đổi sau nhưng vẫn nhận notification, gồm action path tới yêu cầu. Ca `Submission_and_assignment_publish_to_the_authorized_reviewer_and_the_assignee_only` thất bại đúng assertion không có record của B. [TRX trước sửa](../../artifacts/authz/final-delayed-assignment-red.trx)

Luồng giao việc mới ghi `AssignmentId` vào payload outbox. Dispatcher giữ khóa hàng phân công hiện hành trong transaction và so ID trước khi gọi publisher. Sự kiện cũ thiếu ID dùng timestamp đã ghi cùng lúc với assignment để nhận biết đợt cũ. Quyền role/account tiếp tục do `WorkflowNotificationDelivery` kiểm. Hàng đợi cũ được xử lý với dữ liệu hiện hữu, không cần DDL. Ca hồi quy PASS sau sửa. [Nguồn outbox](../../src/Modules/ServiceRequests/Infrastructure/ManagerServiceRequestStore.cs) · [Dispatcher](../../src/PropFlow.Api/Composition/ServiceRequestCostNoticeDispatcher.cs) · [TRX sau sửa](../../artifacts/authz/final-delayed-assignment-green.trx)

Fixture chạy bổ sung giữ chung timestamp cho hai đợt của cùng STAFF. Worker cũ phát cả sự kiện hiện hành có ID lẫn bản legacy không xác định được đợt, làm số record tăng lên 7 thay vì 6. Bản sửa bỏ payload thiếu ID nếu lịch sử chứa thêm đợt của cùng STAFF và yêu cầu với cùng timestamp. Sự kiện có ID đợt hiện hành vẫn được giao. Test kiểm bảo toàn các thông báo hợp lệ và replay. [TRX trước](../../artifacts/authz/notification-fe05-ambiguous-legacy-red.trx) · [TRX sau](../../artifacts/authz/notification-fe05-ambiguous-legacy-green.trx)

## Chrome, WebSocket và persistence

Runner dùng Chrome CDP qua HTTPS reverse proxy local, tài khoản giả đăng nhập bằng form Blazor. Mỗi actor dùng context cookie riêng. STAFF A mở hai tab trong cùng context để kiểm thông báo và trạng thái đọc. Manager thực hiện phân công qua API công khai từ trình duyệt. Người nhận thao tác chuông và link trong giao diện thật. Runner đối chiếu frame `FinanceNotificationCreated` bằng notification ID với record HTTP và kiểm badge tự cập nhật. Script chỉ ghi assertion, số đo và kết quả đã lược định danh. [Runner](../../scripts/authz/notification-browser.mjs) · [Bằng chứng chạy](../../artifacts/authz/final-notification-browser.json)

Các lỗi ban đầu trong runner gồm dựng task thiếu các trường bắt buộc, trông đợi `sourceId` trong frame và kỳ vọng sai mã 403 cho GET task không thuộc phạm vi. Contract thực tế trả 404 cho GET này. Tách việc sửa kịch bản khỏi lỗi ứng dụng ở dispatcher FE-05.

| Luồng đã chạy | Người nhận và đối chứng | Trigger đến frame, hai mẫu (ms) | P50 / P95 / max (ms) | HTTP thành công đến API / frame / badge, lượt cuối (ms) |
| --- | --- | ---: | ---: | ---: |
| Manager giao việc FE-07 | Hai tab cùng phiên STAFF A lên badge. STAFF B và Manager không nhận. B bị 404 khi mở tài nguyên hoặc đánh dấu đã đọc thông báo của A. Sau khi chuyển việc sang B, A bị 403 khi bắt đầu tác vụ. | 1.316 và 831 | 1.073,5 / 1.316 / 1.316 | 402 / 408 / 486 |
| Resident gửi yêu cầu FE-05 | Manager nhận và link mở đúng yêu cầu. Resident và STAFF ngoài phạm vi không nhận frame. | 513 và 1.074 | 793,5 / 1.074 / 1.074 | 779 / 727 / 783 |
| Accountant ghi thu tiền mặt fixture FE-09 | Hộ nhận hóa đơn có badge +1. Hộ khác không nhận frame hoặc record. Link mở đúng hóa đơn. | 452 và 333 | 392,5 / 452 / 452 | 17 / −4 / 25 |

Hai lượt đo chạy trên bản Release với Chrome, HTTPS loopback và PostgreSQL fixture. FE-07 và FE-05 có worker outbox chạy mỗi giây, FE-09 đã có thể tới browser trước lúc actor nhận xong phản hồi HTTP. Mốc API là lần poll đầu nhìn thấy bản ghi. P50 là trung vị, P95 dùng nearest rank với hai mẫu mỗi luồng. Lượt cuối của baseline có tối đa bảy context đăng nhập trong toàn bài. Các số đo mô tả từng lượt chạy và có độ phân giải thấp. Lượt tải 50 phiên STAFF đo HTTP và hub bảo trì, chạy trước các ca Chrome. [Lượt đầu](../../artifacts/authz/final-notification-browser-base.json) · [Baseline trước đợt cuối](../../artifacts/authz/final-notification-browser-before-final-nov17.json) · [Worker](../../src/PropFlow.Api/Composition/OperationalNoticeWorker.cs) · [Lượt tải](../../artifacts/authz/hardening-12-proxy-load-50.json)

Ảnh lưu khi chuông đang mở và chỉ chứa dữ liệu tổng hợp. [Chuông STAFF](../../artifacts/authz/final-staff-assignment-bell.png) · [Chuông Manager](../../artifacts/authz/final-manager-service-request-bell.png) · [Chuông cư dân](../../artifacts/authz/final-resident-payment-bell.png)

Độ phủ mới trên 23 nhóm đã kiểm kê gồm **3/22 nhóm áp dụng có đối chứng Chrome và SignalR**, **4/23 nhóm có đối chứng backend/recipient mới hoặc tăng cường** gồm ba luồng trên và FE-05 giao việc trễ, **4/15 nhóm thông báo hoặc announcement lưu bền có đối chứng persistence mới hoặc tăng cường**. Ba nhóm Chrome có kiểm unread và link, FE-07 có đối chứng mất quyền link và offline. Tỷ lệ này chỉ đếm nhóm có bằng chứng mới trực tiếp, không tính các kết quả lịch sử hoặc coi một trigger duy nhất đại diện toàn bộ các actor/biến thể của nhóm. [Ma trận và bằng chứng từng lớp](propflow-notification-interaction-matrix.md)

## Quyền, hồi phục và giới hạn

- Kiểm đọc/read-all trên cả hai alias. Anonymous bị 401. STAFF khác nhận 404 khi đánh dấu notice của người khác. Snapshot row và unread count đối chiếu theo đúng actor. Ca read-all chỉ cập nhật các dòng của người gọi. Trong Chrome, tab đọc cập nhật badge ngay sau khi mở link, tab thứ hai cập nhật sau 58.320 ms qua cơ chế đối soát định kỳ một phút đang dùng. [Chu kỳ đối soát](../../src/PropFlow.Web.Client/Features/ServiceRequests/Services/RealtimeReconciliationPulse.cs)
- Payload realtime dùng ID thông báo, title/message và đường dẫn nghiệp vụ. Tài nguyên đích kiểm quyền lại. Trạng thái cũ khi rời hẳn tab được lấy từ HTTP khi quay lại. Đây là kiểm tra phục hồi persisted state, khác một phép thử transient reconnect trong cùng tab.
- Khi logout từ UI của STAFF B, WebSocket probe thuộc cùng SID đóng, negotiate và HTTP notification bằng bearer cũ trả 401. STAFF A vẫn đọc được notification của mình. Context đăng nhập riêng của chính tài khoản STAFF B còn trả 200 và nhận frame private cho lần phân công kế tiếp. Context đã logout không nhận frame đó. Lượt Chrome cookie lifecycle riêng cũng xác nhận tab thứ hai cùng phiên thoát và protected route yêu cầu đăng nhập lại. [Bằng chứng phiên Chrome](../../artifacts/authz/hardening-12-browser-proxy-session.json)
- Ca PostgreSQL hiện có kiểm retry/idempotency vận hành và rollback khi gửi yêu cầu FE-05. Kiểm thử SePay mới dùng API thật cùng worker processing chủ động, giả lập lỗi ghi rồi xử lý lại. SMTP, Cloudinary, live SePay, relay TURN và media qua hai mạng thực tế vẫn mở. Chưa suy ra độ trễ cho workload nhiều loại event từ các số đo Chrome đơn lẻ.

## Chạy lại

### Checkpoint đợt kiểm chứng nhóm FE-05

Chrome qua HTTPS loopback đạt ba luồng API-to-browser mới. Manager chuyển yêu cầu từ STAFF B sang A, A thấy thông báo đúng một lần và link mở chi tiết. Workspace của A thêm yêu cầu và workspace của B bỏ yêu cầu mà không tải lại. Frame invalidation chỉ mang ID. B bị 404 khi mở tài nguyên và khi đánh dấu thông báo của A. Sau đó A nhận việc và đề xuất chi phí, đúng cư dân nhận thông báo và thấy khối xác nhận trên chi tiết đang mở. STAFF cũ và hộ khác bị 404 ở các lần truy cập tương ứng. [Kết quả Chrome](../../artifacts/authz/notification-service-requests.json) · [Chuông STAFF](../../artifacts/authz/notification-fe05-assignment.png) · [Chuông cư dân](../../artifacts/authz/notification-fe05-cost.png)

Ca PostgreSQL mở rộng xác nhận B đã được đặt lịch retry trước khi chuyển lại A, legacy thiếu `AssignmentId` chỉ gửi cho timestamp đợt hiện hành trong fixture, lịch sử hợp lệ vẫn còn, không phát bản trùng khi chạy dispatcher lần nữa. Tài khoản bị disable và tài khoản mất grant trước dispatch đều không nhận. Ca chạy bổ sung dùng `pg_blocking_pids` thấy dispatcher chờ transaction đang đổi B về A. Sau commit chỉ A có thông báo mới. Proposal bị từ chối trước dispatch không có thông báo và chỉ proposal thay thế còn hiệu lực được gửi. Không có thay đổi production trong nhóm này. Số nhóm có đối chứng Chrome trực tiếp tăng từ 3/22 lên 6/22. [TRX delayed](../../artifacts/authz/notification-fe05-delayed.trx) · [TRX khóa và proposal](../../artifacts/authz/notification-fe05-lock-and-proposal.trx)

Tiếp tục nhóm FE-04/06/07 với runner `Operations`. Giữ riêng bằng chứng của từng nhóm để tái chạy mà không ghi đè JSON baseline.

### Checkpoint đợt kiểm chứng nhóm FE-04/06/07

Chrome đạt cả sáu luồng đại diện. Manager nhấn nút đóng phản ánh trên UI và chỉ cư dân sở hữu nhận thông báo, mở đúng chi tiết. Lịch đến hạn đổi trạng thái thiết bị, Manager nhận invalidation rỗng và hàng thiết bị tự cập nhật. Hai cư dân hợp lệ nhận thông báo tài sản với link về tab thông báo chung. STAFF bắt đầu bảo trì, Manager và assignee nhận cập nhật có ID, hai cư dân nhận thông báo công việc đã lược tiêu đề nội bộ. Khi đổi phân công, STAFF cũ nhận invalidation rỗng, hai danh sách tự cập nhật, GET tác vụ cũ bị 404 và progress bị 403. Thứ tự hai tab cùng tab mặc định được đối chiếu trên Chrome. [Kết quả Chrome](../../artifacts/authz/notification-operations.json) · [Chuông khi đóng phản ánh](../../artifacts/authz/notification-fe06-closed.png)

12/22 nhóm có đối chứng Chrome trực tiếp, gồm một luồng UI-to-UI mới. Không thay đổi production. Assertion đầu tiên của runner dự đoán sai action path của thông báo tài sản, contract dùng tab thông báo chung. Đã sửa kỳ vọng và bấm link trong lần chạy đạt. Tiếp tục nhóm FE-08/09 với runner `Finance`.

### Checkpoint đợt kiểm chứng nhóm FE-08/09

Sáu ca API-to-browser đạt. Manager lưu giá dịch vụ theo quyền `billing.configure`, hai cư dân nhận giá mới và mỗi hộ nhận hóa đơn dịch vụ tháng riêng. Accountant bị 403 khi thử sửa giá. Accountant gửi hóa đơn và nhắc nợ thủ công, replay cùng key/request ID giữ đúng một thông báo. Resident báo đã chuyển khoản với dữ liệu giả và nhận trạng thái chờ xác minh. Accountant từ chối, đúng cư dân thấy lý do trong chuông và bấm mở đúng hóa đơn. Hộ khác không nhận frame hoặc record riêng và không đọc được chứng từ. Chi tiết hóa đơn phía Accountant không chứa email fixture của người nhận. [Kết quả Chrome](../../artifacts/authz/notification-finance.json) · [Chuông thanh toán bị từ chối](../../artifacts/authz/notification-fe09-rejected.png)

18/22 nhóm có đối chứng Chrome trực tiếp cho ít nhất một biến thể tại checkpoint tài chính. Lượt Finance bổ sung đã đạt đổi giá thuê, phát hành hóa đơn thuê đúng hộ và nhắc nợ thủ công trả 0 người nhận cho hóa đơn đã trả đủ. Với hóa đơn yêu cầu, fixture có kết quả, ảnh và phí cơ bản. Manager gọi API nghiệm thu, worker thông báo cho đúng cư dân, hộ khác bị 404 khi mở chứng từ, và link trong chuông mở đúng invoice được gắn vào yêu cầu. Bản điều chỉnh được kiểm qua dịch vụ và PostgreSQL. Thao tác tạo bản điều chỉnh chưa có route HTTP hoặc UI để chạy hành trình Chrome tương ứng. [Kết quả Chrome bổ sung](../../artifacts/authz/notification-finance.json)

### Checkpoint đợt kiểm chứng nhóm FE-13

Đã dùng form Manager đăng thông cáo và thấy hai cổng cư dân tự cập nhật. SignalR chỉ mang GUID rỗng, ADMIN bị 403 khi đọc và không có record riêng trong chuông. Đã kiểm trên UI rằng draft của cư dân hiện cho Manager được phân công, trạng thái Manager đang gõ hiện ở đúng cư dân, conversation khác không nhận frame, draft không nằm trong danh sách tin nhắn đã lưu. [Kết quả Chrome](../../artifacts/authz/notification-communication.json) · [Thông cáo trên cổng cư dân](../../artifacts/authz/notification-fe13-announcement.png)

Ca chạy lại đã đạt gửi và trả lời tin bằng UI, hiển thị quote đúng tin ở phía cư dân và thu hồi qua API cập nhật UI peer. Conversation ngoài phạm vi nhận 404 ở API, không nhận frame ID của cuộc trò chuyện này. Runner đã chỉnh selector vì nút trả lời nằm cạnh bubble. 21/22 nhóm có bằng chứng Chrome trực tiếp cho ít nhất một biến thể tại checkpoint thông cáo và chat. [Chuỗi chat trên Chrome](../../artifacts/authz/notification-fe13-chat.png)

Lượt bổ sung kiểm hủy trả lời giữ nội dung soạn, sender hiện biên nhận Đã xem, hide-for-me chỉ ảnh hưởng actor thao tác, và forward từ hội thoại đã đóng sang hội thoại vừa chuyển từ chờ sang đang hoạt động. Chỉ cư dân đích thấy bản chuyển tiếp. Cư dân ngoài nguồn bị 404 và replay vẫn giữ đúng một bản. Cư dân chưa có liên kết nhận 403 khi đọc danh sách và chi tiết thông cáo. Lỗi HTTP 503 giả lập khi gửi cho thấy trạng thái lỗi, giữ draft, không tạo hàng hoặc frame riêng; bấm gửi lại hiển thị đúng tin. Giao diện hiện hành chưa có badge đếm chưa đọc dành riêng cho chat. [Chrome](../../artifacts/authz/notification-communication.json)

Ca gọi khẩn cấp qua Chrome đã đạt với microphone giả và STUN cục bộ. Một trong hai kết nối độc lập của cùng Manager có giao diện cuộc gọi. Offer/answer chỉ tới hai participant đã chọn, cư dân ngoài cuộc gọi bị 404. Thử lại tăng revision, Manager thấy kết thúc, phía cư dân mất điều khiển cuộc gọi và giải phóng audio stream. Tổng cộng 22/22 nhóm áp dụng có ít nhất một trigger Chrome/SignalR trực tiếp. Media hoặc TURN qua hai mạng và thu hồi role giữa cuộc gọi trên Chrome vẫn mở. [Chrome](../../artifacts/authz/notification-emergency.json) · [Ảnh giao diện](../../artifacts/authz/notification-fe13-emergency.png)

| Chỉ số có bằng chứng trực tiếp mới hoặc được tăng cường | Baseline | Sau |
| --- | ---: | ---: |
| Nhóm có ca Chrome/SignalR đại diện | 3/22 | 22/22 |
| Nhóm còn thiếu ca Chrome/SignalR đại diện | 19 | 0 |
| Backend hoặc định tuyến đến client nhận theo nhóm | 4/23 | 22/23 |
| Thông báo hoặc thông cáo có đối chiếu dữ liệu lưu bền | 4/15 | 15/15 |
| Nhóm có trigger và kết quả quan sát trên hai UI | 0 | 5 |

Năm nhóm UI-to-UI gồm đóng phản ánh, thông cáo, gửi/trả lời chat, draft/typing và gọi khẩn cấp. Số trên đếm từng nhóm có biến thể được chứng minh. Ma trận ghi riêng các tiêu chí chưa có bằng chứng tương ứng. FE-01 OTP chỉ áp dụng cho kênh email trong kiểm kê này. [Ma trận](propflow-notification-interaction-matrix.md)

### Checkpoint đồng bộ trạng thái đọc

Đo baseline Chrome ở lượt trước là 58.320 ms ở tab còn lại. Test mới gọi hai alias HTTP qua PostgreSQL và nghe hai alias SignalR. Bản cũ hết thời gian khi đợi invalidation. Bản sửa dùng cập nhật SQL có điều kiện chỉ đổi dòng thuộc người gọi còn chưa đọc, sau khi commit gửi event rỗng `NotificationReadStateChanged` đến user qua hub hiện có. Hai lần đọc cùng ID chỉ một lần thay đổi. Client lấy lại danh sách và unread count từ API, dùng cơ chế gộp request và đối soát mỗi phút đã có. No-op và actor ngoài phạm vi không phát tín hiệu. [TRX trước sửa](../../artifacts/authz/notification-read-state-red.trx) · [TRX sau sửa](../../artifacts/authz/notification-read-state-green.trx)

Chrome đo từ lúc thao tác đến khi badge ở ba trang phản ánh cùng kết quả đã lưu. Bản Release cuối đạt 320 ms khi đọc một thông báo và 184 ms khi đọc tất cả. Lượt ngay trước đo 796 ms và 371 ms. Bên nhận gồm hai tab cùng cookie và phiên độc lập cùng tài khoản. Tài khoản khác không nhận invalidation. Replay qua alias không nhân đôi sự kiện. Sau khi đăng xuất riêng phiên độc lập, WebSocket probe của SID đó đóng, HTTP và negotiate của bearer cũ bị 401, phiên còn hiệu lực vẫn nhận sự kiện mới. Khi gửi đồng thời yêu cầu đọc tất cả và giao việc mới, notification mới được lưu một lần, hai tab khớp unread count của API và phiên đã thu hồi không nhận thêm tín hiệu. Các mẫu đo dùng fixture loopback. [Chrome bản cuối](../../artifacts/authz/notification-read-state.json) · [Lượt trước](../../artifacts/authz/notification-read-state-before-final-nov17.json)

### Checkpoint offline, kết nối lại và giao diện

Runner ngắt đúng WebSocket của tab qua proxy fixture và mô phỏng mất mạng bằng Chrome DevTools. STAFF nhận lại phân công và danh sách bảo trì tự cập nhật. Cư dân nhận lại trạng thái thu tiền. Manager thấy tin chat đã lưu lúc mất kết nối và nhận tiếp tin gửi sau khi socket trở lại. Các tab giữ nguyên `performance.timeOrigin`. Một phiên STAFF khác đánh dấu đã đọc trong lúc tab ngắt kết nối, và badge của tab lấy lại đúng số sau reconnect. STAFF chưa đăng nhập lấy được thông báo từ API khi vào hệ thống. Ca thu tiền kế tiếp xác nhận một frame và một WebSocket thông báo. [Chrome](../../artifacts/authz/notification-reconnect.json)

Chuông trên AppShell và cổng cư dân đạt 12 cấu hình giao diện ở 390, 768 và 1440 px, cả sáng và tối. Kiểm panel nằm gọn trong viewport, vùng danh sách cho phép cuộn, timestamp có `datetime`, và các trạng thái tải, trống, lỗi khi offline có nút thử lại hiển thị. Phân trang của API được kiểm qua PostgreSQL trên hai alias với dữ liệu vượt một trang. Chuông hiện tải tối đa 100 mục và không có điều khiển chuyển trang. [Màn nhỏ nội bộ](../../artifacts/authz/notification-panel-internal-dark.png) · [Màn nhỏ cư dân](../../artifacts/authz/notification-panel-resident-dark.png) · [BillingCycle TRX](../../artifacts/authz/notification-billing-regression.trx)

Chrome Routes đạt 378/378 đối chứng trên 63 tuyến, bao gồm anonymous và năm vai trò. Ca riêng kiểm đăng xuất STAFF từ UI, tab còn lại của cùng SID cũng thoát, protected route yêu cầu đăng nhập và bearer cũ bị 401. Bản Release sau sửa đã chạy lại baseline và cả bảy kịch bản ServiceRequests, Operations, Finance, Communication, Emergency, ReadState và Reconnect qua proxy HTTPS. [Ma trận route](../../artifacts/authz/browser-matrix.json) · [Phiên STAFF](../../artifacts/authz/hardening-12-browser-proxy-session.json)

### Hồi quy sau khi sửa

Solution Debug build thành công với 0 cảnh báo và 0 lỗi. Publish Release cho API và Web thành công. Publish Web có cảnh báo NU1900 khi truy cập dữ liệu advisory NuGet. Unit 940, Architecture 64, JavaScript 36, Integration PostgreSQL 561 và BillingCycle PostgreSQL 154 đều đạt trên mã cuối, tổng 1.755 ca, 0 fail, 0 skip. BillingCycle tăng một ca xác minh cập nhật đã đọc đồng thời và phát invalidation sau commit. Dự án BillingCycle nằm ngoài solution và được biên dịch bằng lệnh riêng. Lượt 150 ca từ DLL đã tồn tại được loại khỏi kết quả. JS dùng assertion hiện hành cho bộ chuyển theme dùng chung. Runner Release kiểm HTTPS cùng origin và cả WebSockets lẫn Long Polling trên proxy loopback. [Unit](../../artifacts/authz/notification-UnitTests-nov17.trx) · [Architecture](../../artifacts/authz/notification-ArchitectureTests-nov17.trx) · [JavaScript](../../artifacts/authz/notification-js-nov17.log) · [Integration](../../artifacts/authz/notification-IntegrationTests-nov17.trx) · [BillingCycle](../../artifacts/authz/notification-BillingCycleTests-current-nov17.trx)

`pwsh -NoProfile -File scripts/authz/browser.ps1 -Scope Proxy -UseRelease -LoadUsers 1 -ChromeNotifications -ChromeSession` dùng artifact Release, PostgreSQL test loopback và Chrome. Sau khi dừng runner, kiểm các cổng 47101, 47201 và 47301. Hồi quy PostgreSQL tập trung dùng filter `Submission_and_assignment_publish`, `Notification_api_paginates` và `SePay_http_requires`. [Hướng dẫn audit](../../scripts/authz/README.md)
