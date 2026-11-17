# FE-07 — Quyền xem lịch sử bảo trì

Mỗi `MaintenanceAssignment.Id` ghi một đợt tham gia. Khi STAFF A rời nhóm rồi được giao lại, bản ghi mới mở đợt thứ hai. Lịch sử của A dùng hợp của hai đợt. MANAGER giữ quyền quản lý và đọc hồ sơ đầy đủ. STAFF hiện được giao giữ quyền bàn giao trong chi tiết nhiệm vụ hiện hành.

| Dữ liệu | Phạm vi lịch sử STAFF |
| --- | --- |
| Hồ sơ | Nhiệm vụ có phân công của mình, với thông tin tổng quát và thời điểm tham gia. Lọc theo người tham gia trước khi đếm và phân trang |
| Activity | Bản ghi trong đợt tham gia do thời gian server xác định. Tại đầu đợt, activity cần trùng Assignment ID. Tại điểm kết thúc do hoàn tất, đội hoàn tất giữ bản ghi nộp và trạng thái đóng |
| Kết quả | Kết quả phát sinh trong đợt. Ở thời điểm bắt đầu trùng nhau, ưu tiên người thực sự nộp. Đội cùng hoàn tất giữ kết quả nộp lúc hoàn tất và nghiệm thu kết quả đó |
| Ảnh | Khớp task, result và attachment trước khi mở kho. Quyền theo kết quả cha và vai trò hiện hành. Phản hồi `no-store` |
| Thao tác | Quyền bắt đầu, cập nhật tiến độ và nộp kết quả phụ thuộc phân công hiện hành và trạng thái task |
| SignalR | Gửi mã task cho quản lý và nhân viên còn quyền. Người vừa bị gỡ nhận tín hiệu làm mới danh sách, với payload không chứa mã task |

Thời gian do server ghi. Dữ liệu cũ có phân công nhưng thiếu ranh giới chỉ cấp thông tin có thể đối chiếu. MANAGER có thể hỗ trợ tra cứu từ hồ sơ gốc. Chưa cần thêm cột hoặc backfill cho FE-07. Thao tác phân công, hủy, bắt đầu, tiến độ, nộp và review dùng cùng khóa giao dịch theo task trong PostgreSQL. Kiểm quyền nộp trước khi gọi kho ảnh. Trường hợp commit có kết quả bất định giữ tệp để đối soát theo bản ghi kết quả sau đó.

Thông báo phân công chờ trong outbox lưu Assignment ID. Worker lấy khóa theo task và đối chiếu đợt còn hiệu lực, cùng trạng thái và quyền vận hành của tài khoản nhận. Event legacy thiếu Assignment ID đối chiếu bằng thời điểm phân công ghi trên server. Event của đợt đã kết thúc được xử lý mà không tạo thông báo.

[MaintainX · User roles and permissions](https://help.getmaintainx.com/user-roles-and-permissions) · [UpKeep · User type capabilities](https://help.onupkeep.com/en/articles/5866996-upkeep-user-type-capabilities-matrix)
