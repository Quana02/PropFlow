# Phân tích tiêu thụ điện nước — mở rộng Task 1

Phạm vi do người dùng yêu cầu ngày 14/11/2026; không thay thế FE-10/FE-11 phân loại yêu cầu dịch vụ.

- Manager giữ quyền nhập/chốt chỉ số hiện có. Manager và Accountant đọc phân tích; chỉ Manager xác nhận hoặc bỏ qua cảnh báo.
- Billing sở hữu công tơ, lượng tiêu thụ, hóa đơn và snapshot người nhận. AiRecommendation chỉ đọc qua Billing.Contracts, lưu kết quả và lịch sử xét duyệt riêng.
- Phân tích kWh/m³ theo ngày trong tháng; chỉ dùng kỳ đủ tháng, không lấp kỳ thiếu bằng 0. Cần ít nhất 6 kỳ tham chiếu hợp lệ. Không kết luận rò rỉ từ chỉ số tháng.
- ML.NET IID spike là phép phát hiện phân phối lịch sử; trung vị/MAD và mức thay đổi tối thiểu kiểm soát nhiễu. Điểm lệch không phải xác suất sự cố. Chưa áp dụng mô hình mùa vụ hoặc nhân khẩu học khi thiếu dữ liệu phù hợp.
- Dữ liệu có công tơ nhưng không nối liên tục từ kỳ liền trước được đánh dấu không đủ căn cứ. Các lượng tháng nhập trực tiếp trước đây được coi là dữ liệu tháng theo hợp đồng Billing.
- Gemini giải thích kết quả đã tính, không quyết định số liệu/mức độ. Không gửi tên, số căn, user ID hoặc chỉ số công tơ tích lũy ra provider. Có timeout, quota lưu theo ngày và fallback; lỗi AI không cản lưu công tơ.
- Kết quả lưu kèm fingerprint dữ liệu, phiên bản phương pháp, thời điểm và trạng thái xét duyệt. Sửa chỉ số làm kết quả cũ hết hiệu lực. Chỉ gửi cảnh báo sau khi kỳ đã có hóa đơn chốt và Manager xác nhận; outbox chống gửi trùng.
- Resident chỉ xem kỳ đã phát hành có snapshot người nhận là tài khoản của mình. Lịch sử tham chiếu cũng giới hạn theo quyền của toàn bộ người nhận kỳ hiện tại; không lộ lịch sử hộ trước.
- Kiểm thử: thiếu dữ liệu, 0 hợp lệ, MAD=0, kỳ không liền mạch, số ngày khác nhau, tăng/giảm, model lỗi, AI lỗi/quota, xét duyệt trùng, dữ liệu đổi và truy cập chéo tài khoản.

API: `/api/v1/utility-insights` cho Manager/Accountant; `/api/v1/resident/utility-insights` cho cư dân. GET không gọi Gemini; giải thích và xét duyệt là thao tác riêng có giới hạn.

## Sử dụng

- Manager: mục **Phân tích điện nước**, đường dẫn `/operations/utility-insights`. Phần nhập chỉ số hiện có cũng tự hiển thị kết quả sau khi lưu.
- Accountant: `/finance/utility-insights`, xem phân tích và yêu cầu giải thích; không được xác nhận cảnh báo.
- Resident: liên kết phân tích trong trang Hóa đơn, đường dẫn `/resident/utilities`. Chỉ thấy các kỳ đã phát hành của mình; có thể mở yêu cầu kiểm tra với mô tả điền sẵn.
- **AI giải thích thêm** dùng cấu hình Gemini hiện có. Không cần tài liệu RAG cho phép tính này. Khi Gemini lỗi hoặc chưa cấu hình, số liệu và kết quả ML.NET vẫn hoạt động.
- Manager xác nhận kèm ghi chú sau khi chốt kỳ; worker gửi thông báo trong ứng dụng theo hàng đợi mỗi ba mươi giây. Không gửi email, không tự thay đổi hóa đơn hoặc tạo công việc sửa chữa.
- Cấu hình tùy chọn `UtilityInsights`: tối thiểu sáu kỳ tham chiếu, tối đa hai mươi yêu cầu giải thích mỗi người trong hai mươi bốn giờ, hai trăm yêu cầu toàn hệ thống; cách nhau ít nhất một phút cho cùng kết quả. Khóa Gemini dùng cấu hình máy chủ hiện có.
- Migration `20261114111444_AddUtilityConsumptionInsights` chỉ bổ sung bảng phân tích, lịch sử xét duyệt, lượt gọi AI và hàng đợi; không sửa dữ liệu điện nước/hóa đơn. Không thêm dữ liệu demo vào cơ sở dữ liệu thật.
