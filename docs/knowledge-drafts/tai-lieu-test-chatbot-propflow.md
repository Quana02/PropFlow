# Hướng dẫn yêu cầu dịch vụ PropFlow — tài liệu kiểm thử chatbot

Mã tài liệu: PF-RAG-TEST-01.
Phiên bản nội dung: 1.0.
Ngày soạn: 05/11/2026.
Đối tượng đọc: cư dân và nhân sự quản lý chung cư.

Đây là tài liệu dùng để kiểm thử chatbot RAG trong môi trường phát triển hoặc trình diễn PropFlow. Không sử dụng tài liệu này làm nội quy chính thức của chung cư. Tài liệu không quy định đơn giá, giờ làm việc, mức phạt hoặc thông tin ngân hàng thực tế.

## 1. Gửi và theo dõi yêu cầu dịch vụ

Cư dân mở mục Yêu cầu dịch vụ trên PropFlow để gửi yêu cầu và theo dõi tiến độ xử lý. Khi gửi yêu cầu, cư dân nên nêu rõ sự cố, vị trí và tình trạng đang gặp phải. Mô tả rõ ràng giúp Ban quản lý tiếp nhận và phân công nhân viên phù hợp.

Sau khi gửi, cư dân mở danh sách Yêu cầu dịch vụ rồi chọn yêu cầu cần xem để đọc chi tiết. Cư dân cần thực hiện thao tác gửi tại chức năng Yêu cầu dịch vụ; việc hỏi chatbot không tự tạo yêu cầu mới.

## 2. Phí dịch vụ ban đầu

Khi tạo đơn dịch vụ và phân công nhân viên, Manager có thể ghi nhận chi phí dịch vụ ban đầu. Phí ban đầu có thể có hoặc không, tùy yêu cầu cụ thể. Tài liệu này không cung cấp bảng giá cố định cho các loại dịch vụ.

Phí ban đầu và phí phát sinh là hai khoản cần được phân biệt. Việc chấp nhận xử lý một yêu cầu không có nghĩa cư dân đã đồng ý với mọi chi phí phát sinh sau đó.

## 3. Đề nghị chi phí phát sinh

Nếu phát hiện cần thêm chi phí trong lúc xử lý, Staff phải gửi đề nghị cho cư dân trước khi thực hiện phần việc phát sinh. Đề nghị phải nêu số tiền và lý do phát sinh.

Cư dân mở thông báo hoặc chi tiết yêu cầu để xem phí dịch vụ ban đầu, số tiền phát sinh, tổng chi phí dự kiến và lý do. Cư dân có thể đồng ý hoặc từ chối đề nghị chi phí phát sinh.

Trong thời gian chờ cư dân phản hồi chi phí phát sinh, Staff chưa được tiếp tục cập nhật tiến độ, hoàn thành công việc hoặc tải bằng chứng. Manager chưa được nghiệm thu yêu cầu đang chờ phản hồi này.

## 4. Khi cư dân đồng ý chi phí phát sinh

Khi cư dân đồng ý với đề nghị chi phí phát sinh hiện tại, Staff được tiếp tục xử lý công việc. Sau đó Manager thực hiện nghiệm thu theo quy trình của yêu cầu.

Số tiền phát sinh được Manager duyệt không được vượt số tiền cư dân đã chấp thuận. Staff không khai thêm một khoản phát sinh mới tại bước hoàn thành công việc để bỏ qua bước xin ý kiến cư dân.

Nếu số tiền hoặc lý do phát sinh thay đổi, Staff phải tạo đề nghị mới và cư dân cần xác nhận lại. Sự đồng ý đối với đề nghị cũ không tự áp dụng cho đề nghị mới.

## 5. Khi cư dân từ chối hoặc chưa phản hồi

Nếu cư dân từ chối chi phí phát sinh, yêu cầu tự động kết thúc theo trạng thái hủy của hệ thống. Các phân công còn hoạt động được hủy và hệ thống lưu lịch sử cư dân từ chối chi phí. Yêu cầu này không phát hành hóa đơn mới và không mở phần đánh giá.

Nếu cư dân chưa phản hồi chi phí phát sinh, yêu cầu tiếp tục chờ. Hệ thống không tự coi việc im lặng là đồng ý và cũng không tự từ chối chỉ vì cư dân chưa trả lời sau một khoảng thời gian.

## 6. Xem hóa đơn và trạng thái thanh toán

Cư dân mở mục Hóa đơn và thanh toán để xem thông tin được hệ thống cung cấp cho tài khoản của mình. Tài liệu này không chứa hóa đơn, công nợ hoặc lịch sử chuyển khoản của bất kỳ cư dân nào.

Chatbot tài liệu chưa tra cứu hóa đơn, công nợ hoặc giao dịch cá nhân. Khi cần biết hóa đơn của mình đã được ghi nhận thanh toán hay chưa, cư dân cần xem tại chức năng Hóa đơn và thanh toán.

Không thực hiện chuyển tiền thật chỉ để thử tài liệu chatbot này. Tài liệu không cung cấp tài khoản nhận tiền hoặc mã QR để thanh toán.

## 7. Kiểm tra câu trả lời và nguồn tài liệu

Khi chatbot có câu trả lời kèm dẫn nguồn, cư dân có thể chọn Đọc tài liệu nguồn để kiểm tra nội dung và phiên bản. Nếu tài liệu đã bị thu hồi hoặc thay thế, nguồn cũ có thể không còn mở được; cư dân nên gửi lại câu hỏi để lấy thông tin hiện hành.

Nếu chưa có nội dung phù hợp trong tài liệu được xuất bản, chatbot cần thông báo chưa đủ thông tin thay vì tự đưa ra nội quy hoặc đơn giá. Đối với nội dung cần quyết định hoặc xác nhận chính thức, cư dân liên hệ Ban quản lý qua kênh liên hệ được chung cư công bố.

Tài liệu kiểm thử này không xác định số điện thoại Ban quản lý, lịch mở cửa phòng sinh hoạt chung, mức phí giữ xe hoặc mức phạt tiếng ồn. Các thông tin đó cần được Ban quản lý cung cấp trong tài liệu chính thức riêng.
