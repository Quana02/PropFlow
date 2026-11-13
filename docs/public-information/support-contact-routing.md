# Phân tuyến liên hệ và hỗ trợ

| Tình huống | Đầu mối theo nghiệp vụ | Hướng xử lý |
| --- | --- | --- |
| Đăng ký, OTP, khóa tài khoản, mất email | Đầu mối quản lý tài khoản do chung cư chỉ định | `/help`, `/forgot-password`, `/registration/resume`. Nếu không truy cập được email thì dùng kênh ngoài ứng dụng do chung cư công bố. |
| Hồ sơ và căn hộ | Ban quản lý | `/resident/apartment` sau đăng nhập. Chuẩn bị mã căn hoặc mã cư dân khi đối chiếu. |
| Yêu cầu dịch vụ | Ban quản lý và nhân sự được phân công | `/resident/service-requests` sau đăng nhập. |
| Phản ánh | Ban quản lý | `/resident/complaints` sau đăng nhập. |
| Hóa đơn và đối soát | Ban quản lý và kế toán theo quyền | `/resident/billing` sau đăng nhập. Tham chiếu mã hóa đơn. |
| Quyền dữ liệu | Đầu mối bảo vệ dữ liệu chờ xác nhận | `/privacy#quyen`, đề nghị Ban quản lý hướng dẫn kênh và xác minh phù hợp. |
| Sự cố kỹ thuật | Ban quản lý hoặc đầu mối được chỉ định | `/resident/contact-management` sau đăng nhập. Ghi thời điểm, trang và lỗi. |
| Khẩn cấp | Kênh ứng cứu do chung cư công bố | Ưu tiên ứng cứu trực tiếp. Cuộc gọi trong ứng dụng tùy kết nối và khả năng tiếp nhận. |

Bản demo chưa có email, hotline, địa chỉ hoặc lịch hỗ trợ công khai đã duyệt. Phải xác nhận kênh cho người không đăng nhập được, đầu mối dữ liệu, trực ngoài giờ và quy trình chuyển tuyến trước khi ban hành nội dung. Trang công khai chỉ cung cấp hướng dẫn, các link có yêu cầu đăng nhập được ghi rõ.
