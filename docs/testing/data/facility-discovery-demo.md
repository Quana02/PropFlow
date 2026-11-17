# Dữ liệu mẫu test AI gợi ý tiện ích

Script nhập local: `tools/database/seeds/20261116_facility_discovery_demo.sql`.

Chỉ dùng database `propflow` trên localhost, có sẵn Manager và Staff đang hoạt động. Không tạo tài khoản, không đổi trạng thái tiện ích cũ, không chạm hóa đơn/điện nước. Các bản ghi mang tiền tố `[DEMO]` và đầu mối liên hệ được ghi rõ là mẫu. Script chạy lại không tạo trùng hoặc ghi đè thay đổi Manager đã lưu. Đây là import thủ công phục vụ test, không phải migration hoặc seed khi khởi động.

## Hồ sơ được cung cấp

| Tiện ích mẫu | Đối tượng | Lịch | Điều kiện |
|---|---|---|---|
| Khu vui chơi trẻ em | 3–12 tuổi | Hằng ngày 08:00–20:00 | Có phụ huynh/người giám hộ đi cùng |
| CLB thiếu nhi thứ Bảy | 4–10 tuổi | Thứ Bảy 08:00–17:00 | Chương trình có giám sát mô phỏng; cần BQL xác nhận tiếp nhận |
| Thư viện và phòng đọc | Cư dân nói chung | Hằng ngày 06:00–22:00 | Giữ yên lặng |
| Hồ bơi gia đình | Từ 6 tuổi | Thứ Ba đến Chủ nhật 06:00–20:00 | Có người giám hộ cho trẻ; nghỉ thứ Hai và Chủ nhật của tuần nhập mẫu |
| Phòng gym người lớn | Từ 18 tuổi | Hằng ngày 05:30–22:00 | Không dành cho trẻ em |
| Vườn và đường đi bộ | Gia đình | Hằng ngày 05:00–22:00 | Trẻ có người lớn đi cùng |
| Sân cầu lông | Từ 10 tuổi | Hằng ngày 06:00–21:00 | Liên hệ xác nhận sử dụng, không tự giữ chỗ |

Hai hồ sơ test âm: **Lớp kỹ năng chưa xuất bản** (bản nháp) và **Phòng vui chơi ngừng hoạt động**. Cả hai không được xuất hiện trong danh mục hoặc gợi ý cho cư dân.

Người phụ trách lấy từ Staff đang hoạt động có sẵn; Manager đang hoạt động được ghi nhận là người quản lý hồ sơ. Tên thật hiển thị theo tài khoản hiện có, không bịa nhân viên hoặc số điện thoại. Phân công này là dữ liệu demo, không gửi thông báo/chat tới Staff.

## Cách test

1. Manager mở **Hồ sơ tiện ích** để xem/chỉnh các mục `[DEMO]`.
2. Resident chọn **Tiện ích AI** trong **Dịch vụ cư dân**, chạm micro để nói hoặc nhập nhu cầu sau. Không cần thêm tài liệu vào chatbot RAG: chức năng này đọc trực tiếp hồ sơ tiện ích. Có thể nói bổ sung khi AI hỏi tuổi; dùng **Tìm cho nhu cầu khác** để bắt đầu tình huống độc lập.

| Nội dung thử | Kết quả cần kiểm tra |
|---|---|
| Khu vực cuối tuần cho trẻ em | Tiện ích phù hợp trẻ/gia đình, giờ cuối tuần; hỏi thêm tuổi nếu chưa nêu |
| Bé 6 tuổi, thứ Bảy hai vợ chồng phải lên công ty, cần người trông | Chỉ CLB có giám sát phù hợp; phải liên hệ xác nhận, không gợi ý gửi trẻ một mình ở hồ bơi/khu vui chơi |
| Bé 12 tuổi, thứ Bảy bố mẹ đi làm, cần người trông | Không có chương trình giám sát phù hợp tuổi; thông báo chưa tìm thấy |
| Tôi cần một nơi đọc sách yên tĩnh | Thư viện/phòng đọc |
| Tôi muốn tập thể lực ở phòng gym | Gym, kèm lịch và người phụ trách |
| Tôi muốn đi bộ và thư giãn ngoài trời cùng gia đình | Vườn và đường đi bộ |
| Tôi muốn chơi cầu lông với bạn bè | Sân cầu lông, không tự xác nhận đặt sân |
| Bé 3 tuổi muốn đi bơi | Không gợi ý hồ bơi do chưa đủ tuổi |
| Tôi muốn bơi vào thứ Hai | Không gợi ý hồ bơi vì đóng cửa thứ Hai |
| Tôi muốn đặt vé xem phim | Không tự tạo rạp chiếu phim khi không có trong danh mục |

Nêu “thứ Hai”, “cuối tuần”, “hôm nay” hoặc “ngày mai” trong câu nói/nhập để hệ thống đối chiếu lịch. Có thể dùng Chủ nhật của tuần nhập mẫu để kiểm tra hồ bơi đóng cửa ngoại lệ.

Kết quả `AI gợi ý` dùng Gemini. Nếu provider chưa sẵn sàng hoặc hết hạn mức, giao diện ghi rõ tra cứu theo nội dung; đánh giá độ phù hợp ngữ nghĩa chỉ khi AI đang hoạt động. Bản ghi DEMO không xác nhận rằng chung cư thực tế có các dịch vụ trên.
