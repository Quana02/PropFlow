# FE-05 — Duyệt và chấp thuận chi phí phát sinh

Cập nhật nghiệp vụ theo yêu cầu ngày 18/11/2026. Áp dụng cho các use case phân công Staff, thực hiện yêu cầu, theo dõi/nghiệm thu và theo dõi yêu cầu của cư dân trong FE-05. Đây là điều chỉnh luồng chi phí theo yêu cầu người dùng, không bổ sung vai trò hoặc quyền mới.

1. Manager chỉ phân công hoặc điều phối Staff, không nhập phí khi phân công. API giữ trường BaseServiceCost để tương thích dữ liệu gửi cũ nhưng từ chối mọi giá trị khác 0; không xóa các khoản phí lịch sử.
2. Staff tiếp nhận, khảo sát. Khi có chi phí phát sinh, Staff nhập số tiền VND nguyên dương và lý do, gửi Manager duyệt trước khi làm phần việc phát sinh. Trạng thái báo giá: AWAITING_MANAGER. Giao dịch đồng thời ghi lịch sử và sự kiện thông báo cho hàng đợi Manager đang hoạt động có quyền quản lý vận hành.
3. Manager kiểm tra số tiền và lý do, bấm “Duyệt và gửi cư dân xác nhận”. Báo giá chuyển sang PENDING; giao dịch ghi nhận người duyệt và một sự kiện thông báo cho đúng cư dân. Duyệt lại cùng báo giá không gửi trùng thông báo.
4. Khi chờ Manager hoặc cư dân, Staff không được cập nhật tiến độ, hoàn thành hoặc tải bằng chứng; Manager chưa được nghiệm thu. Giao diện cư dân chỉ hiển thị quyết định khi báo giá đã được Manager duyệt. API cũng từ chối quyết định trước bước này.
5. Cư dân đồng ý: báo giá ACCEPTED; Staff tiếp tục thực hiện và gửi kết quả kèm bằng chứng. Manager nghiệm thu; chi phí cuối không được vượt báo giá cư dân chấp thuận. Giao diện nghiệm thu dùng số tiền đã chấp thuận, không khai báo phí mới.
6. Nghiệm thu yêu cầu có phí: Billing phát hành một hóa đơn cho yêu cầu. Khi liên kết hóa đơn, cùng giao dịch ghi sự kiện thông báo cư dân xem hóa đơn và thanh toán. Gửi lại không tạo hóa đơn hoặc thông báo trùng. Cư dân báo đã thanh toán vẫn phải qua luồng đối soát; thao tác đó không tự xác nhận đã nhận tiền. Yêu cầu không có phí không tạo hóa đơn.
7. Cư dân từ chối: báo giá REJECTED, yêu cầu chuyển CANCELLED, hủy các phân công đang hoạt động, lưu lịch sử; không phát hành hóa đơn cho yêu cầu bị hủy.
8. Chưa trả lời: tiếp tục chờ; không tự hết hạn hoặc tự hủy theo thời gian.
9. Staff đổi số tiền hoặc lý do: tạo phiên bản mới về AWAITING_MANAGER và cần Manager duyệt, cư dân chấp thuận lại. Duyệt/phản hồi phiên bản cũ trả409. Công việc khu vực chung không thu phí cư dân.

Phân quyền: Staff cần quyền xử lý và phân công đang hoạt động; Manager dùng quyền quản lý yêu cầu hiện có trong phạm vi quản lý chung cư hiện hành. Resident được resolve từ tài khoản và cư trú hợp lệ, đối chiếu đúng Resident của yêu cầu. Không nhận danh tính diễn viên từ body làm bằng chứng quyền sở hữu. Các thay đổi báo giá/quyết định dùng khóa giao dịch theo yêu cầu, kiểm tra phiên bản và concurrency token.

Thông báo trong ứng dụng dùng transactional outbox có retry; cập nhật thời gian thực tới cư dân, Staff và Manager. Không có email trong thay đổi này.

Migration `RequireManagerReviewForAdditionalCosts` mở rộng ràng buộc trạng thái. Báo giá cũ đang chờ cư dân của yêu cầu chưa kết thúc, chưa phát hành hóa đơn được đưa về chờ Manager; thông báo cư dân còn trong hàng đợi được ngưng, thay bằng thông báo Manager. Các quyết định cư dân đã ghi, khoản phí lịch sử và hóa đơn đã phát hành được giữ nguyên.

API:

- `PUT /api/v1/manager/service-requests/{id}/assignment`: StaffUserId, Note; BaseServiceCost chỉ chấp nhận 0.
- `POST /api/v1/staff/service-requests/{id}/additional-cost`: Amount, Note, ExpectedProposalId.
- `POST /api/v1/manager/service-requests/{id}/additional-cost/approve`: ProposalId.
- `POST /api/v1/resident/service-requests/{id}/additional-cost/response`: ProposalId, Accepted.
- API nghiệm thu, hóa đơn và thanh toán giữ luồng hiện có; hóa đơn phát hành sau nghiệm thu, không phải sau thao tác đồng ý báo giá.

Kiểm thử: `AdditionalCostConsentTests`, `AdditionalCostConsentUiTests`, `ServiceRequestPaymentTests`, `AdditionalCostConsentDatabaseTests`, `ServiceRequestAuthorizationHttpTests` cho quyền sở hữu, phân quyền HTTP, các chốt chặn, phiên bản, duyệt lặp, từ chối, hóa đơn và thông báo.
