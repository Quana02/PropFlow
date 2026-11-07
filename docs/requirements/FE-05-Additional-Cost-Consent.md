# FE-05 — Cư dân chấp thuận chi phí phát sinh

Theo thay đổi nghiệp vụ được xác nhận ngày 01/11/2026:

1. Manager giữ bước ghi phí dịch vụ ban đầu và phân công Staff.
2. Trong khi xử lý, Staff phải gửi số tiền và lý do phát sinh **trước khi thực hiện phần việc phát sinh**. Đề xuất và thông báo cho đúng cư dân được ghi cùng giao dịch.
3. Trong lúc chờ phản hồi, Staff không được cập nhật tiến độ, hoàn thành hoặc tải bằng chứng; Manager không được nghiệm thu.
4. Cư dân mở yêu cầu từ thông báo hoặc chi tiết yêu cầu, xem phí ban đầu, phí phát sinh, tổng dự kiến và lý do. Quyết định gắn với một phiên bản đề xuất cụ thể.
5. Đồng ý: Staff được tiếp tục xử lý; Manager nghiệm thu cuối cùng và duyệt không vượt số tiền cư dân đã chấp thuận. Hoàn thành không còn là bước khai thêm phí.
6. Từ chối: yêu cầu tự động chuyển sang trạng thái kết thúc `CANCELLED` hiện có, hủy các phân công đang hoạt động, ghi lịch sử cư dân từ chối phí. Giao diện giải thích yêu cầu bị từ chối do không chấp nhận chi phí. Không phát hành hóa đơn mới hay mở đánh giá cho yêu cầu này.
7. Chưa trả lời: tiếp tục chờ, không tự hết hạn hoặc tự từ chối theo thời gian.
8. Đổi số tiền/lý do: tạo phiên bản mới và yêu cầu đồng ý lại; phản hồi phiên bản cũ trả409. Gửi lại cùng phản hồi đã ghi không tạo thêm quyết định.

Phân quyền: Staff có phân công đang hoạt động; Resident được resolve từ tài khoản và quan hệ cư trú hợp lệ rồi đối chiếu đúng Resident của yêu cầu. Không nhận ResidentId/StaffId từ body làm bằng chứng quyền sở hữu.

Thông báo là thông báo trong ứng dụng, có hàng đợi giao dịch và retry; cập nhật thời gian thực gửi cho cư dân, Staff và Manager. Không có thao tác gửi email ra ngoài trong thay đổi này.

Đề xuất của yêu cầu còn đang xử lý trước migration được chuyển sang chờ cư dân và tạo thông báo. Yêu cầu đã nghiệm thu/đóng và hóa đơn đã phát hành được giữ nguyên lịch sử. Migration không tạo khoản thu hoặc đổi số dư hóa đơn.

API:

- `POST /api/v1/staff/service-requests/{id}/additional-cost`: Amount, Note, ExpectedProposalId.
- `POST /api/v1/resident/service-requests/{id}/additional-cost/response`: ProposalId, Accepted.

Kiểm thử: `AdditionalCostConsentTests`, `AdditionalCostConsentUiTests` và kiểm thử tích hợp PostgreSQL cho phiên bản báo giá/quyền sở hữu/khóa đồng thời.
