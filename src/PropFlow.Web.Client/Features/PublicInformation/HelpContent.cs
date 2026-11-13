namespace PropFlow.Web.Client.Features.PublicInformation;

public static class HelpContent
{
    public static IReadOnlyList<FaqCategory> Categories { get; } =
    [
        new("account", "Tài khoản",
        [
            new("Ai có thể đăng ký?", "Chủ sở hữu hiện hành đủ điều kiện hoặc người thuê là chủ hộ đang cư trú tại căn chưa có chủ sở hữu hiện hành. Hồ sơ phải còn hiệu lực, chưa liên kết tài khoản và thông tin phải khớp.", "đăng ký điều kiện", "/register", "Mở đăng ký"),
            new("Vì sao không tìm được hồ sơ?", "Kiểm tra email liên hệ, số điện thoại, loại và số giấy tờ theo hồ sơ Ban quản lý. Nếu vẫn không khớp, nhờ Ban quản lý đối chiếu qua kênh của chung cư."),
            new("Không nhận được OTP hoặc mã đã hết hạn?", "Kiểm tra hộp thư rác và địa chỉ email. Dùng chức năng gửi lại khi hệ thống cho phép, rồi nhập mã mới. Có thể mở tiếp tục đăng ký nếu đã bắt đầu trước đó.", "otp mã xác minh", "/registration/resume", "Tiếp tục đăng ký"),
            new("Đăng nhập thất bại hoặc tài khoản bị khóa?", "Kiểm tra tên đăng nhập. Nếu hệ thống báo khóa tạm thời, làm theo thông báo trên màn hình. Với tài khoản bị vô hiệu hóa hoặc không thể khôi phục, xem hướng dẫn liên hệ phù hợp.", "đăng nhập khóa lỗi", "/contact", "Xem hướng dẫn liên hệ"),
            new("Quên mật khẩu hoặc mất quyền truy cập email?", "Dùng trang Quên mật khẩu nếu còn truy cập email đăng nhập. Nếu mất quyền truy cập email, đề nghị đầu mối quản lý tài khoản đối chiếu qua kênh đã được chung cư cung cấp.", "khôi phục mật khẩu", "/forgot-password", "Mở Quên mật khẩu"),
            new("Đổi mật khẩu bằng cách nào?", "Sau khi đăng nhập, mở Tài khoản rồi chọn Đổi mật khẩu.", "mật khẩu", "/account/change-password", "Mở Đổi mật khẩu · cần đăng nhập"),
            new("Cư dân đổi email đăng nhập thế nào?", "Ban quản lý đối chiếu hồ sơ và khởi tạo yêu cầu đổi email. Cư dân hoàn tất bước xác minh trong luồng được cung cấp.", "email đăng nhập manager"),
            new("Cập nhật email liên hệ, số điện thoại hoặc họ tên thế nào?", "Thông tin tài khoản có thể được cập nhật trong Tài khoản theo trường hệ thống cho phép. Để sửa thông tin hồ sơ cư dân, liên hệ Ban quản lý để đối chiếu. Email liên hệ hồ sơ khác email dùng đăng nhập.", "hồ sơ tên điện thoại")
        ]),
        new("residence", "Hồ sơ và căn hộ",
        [
            new("Xem hồ sơ cá nhân và căn hộ ở đâu?", "Đăng nhập bằng tài khoản cư dân, mở Cổng cư dân và mục Căn hộ để xem thông tin trong phạm vi được cấp.", "căn hộ quan hệ", "/resident/apartment", "Mở Căn hộ · cần đăng nhập"),
            new("Ai sửa hồ sơ hoặc quan hệ sở hữu, cư trú?", "Báo Ban quản lý khi thông tin sai. Người có quyền sẽ đối chiếu và cập nhật hồ sơ hoặc quan hệ căn hộ theo quy trình. Cư dân không tự sửa quan hệ sở hữu.")
        ]),
        new("requests", "Yêu cầu dịch vụ",
        [
            new("Tạo và theo dõi yêu cầu sửa chữa thế nào?", "Trong Cổng cư dân, mở Yêu cầu dịch vụ. Mô tả việc cần xử lý, vị trí và thông tin cần thiết. Theo dõi trạng thái và chi tiết tại danh sách.", "sửa chữa theo dõi", "/resident/service-requests", "Mở Yêu cầu dịch vụ · cần đăng nhập"),
            new("Đính kèm ảnh và trao đổi kết quả thế nào?", "Hãy mô tả sự cố bằng văn bản khi gửi yêu cầu. Nếu Ban quản lý cần ảnh bổ sung, hỏi họ về kênh tiếp nhận phù hợp và chỉ gửi ảnh liên quan. Theo dõi tiến độ và kết quả trong chi tiết yêu cầu.", "ảnh tệp bằng chứng"),
            new("Chi phí phát sinh và đánh giá sau xử lý nằm ở đâu?", "Khi có đề xuất chi phí, xem chi tiết yêu cầu và thực hiện bước phản hồi nếu hệ thống cho phép. Sau xử lý, dùng chức năng đánh giá khi yêu cầu đủ điều kiện và nút xuất hiện."),
            new("Khi nào cần liên hệ khẩn?", "Khi có nguy cơ tức thời đối với người hoặc tài sản, ưu tiên kênh ứng cứu đã được chung cư công bố. Cuộc gọi hỗ trợ trong ứng dụng cần đăng nhập và phụ thuộc kết nối, quyền micro và khả năng tiếp nhận.")
        ]),
        new("complaints", "Phản ánh",
        [
            new("Gửi phản ánh hay tạo yêu cầu dịch vụ?", "Dùng Phản ánh để gửi góp ý hoặc vấn đề cần Ban quản lý phản hồi. Dùng Yêu cầu dịch vụ cho sự cố cần xử lý kỹ thuật. Có thể theo dõi trạng thái và phản hồi trên trang Phản ánh.", "khiếu nại góp ý", "/resident/complaints", "Mở Phản ánh · cần đăng nhập")
        ]),
        new("billing", "Hóa đơn và thanh toán",
        [
            new("Xem hóa đơn và công nợ ở đâu?", "Mở Hóa đơn và thanh toán trong Cổng cư dân để xem khoản phải trả, đã thu và chứng từ trong phạm vi của bạn.", "phí công nợ", "/resident/billing", "Mở Hóa đơn · cần đăng nhập"),
            new("Đã chuyển khoản nhưng vẫn còn nợ?", "Nếu có chức năng báo chuyển khoản, dùng hướng dẫn trên đúng hóa đơn. Khoản chờ xác minh chưa giảm dư nợ. Nếu quá thời gian theo hướng dẫn của chung cư hoặc có sai lệch, liên hệ Ban quản lý và chuẩn bị mã hóa đơn."),
            new("Kế toán gửi nhắc phí cho ai?", "Kế toán thao tác trên hóa đơn hoặc công nợ. Máy chủ xác định người nhận theo nghĩa vụ của hóa đơn. Thông báo tiếp nhận trên màn hình cho biết yêu cầu đã được ghi nhận để xử lý.")
        ]),
        new("communication", "Thông báo và liên lạc",
        [
            new("Xem và đánh dấu đã đọc thông báo thế nào?", "Mở chuông thông báo, chọn thông báo cần xem hoặc dùng Đánh dấu tất cả đã đọc khi có mục chưa đọc. Nếu thiếu thông báo nghiệp vụ, kiểm tra trạng thái hồ sơ liên quan và hỏi Ban quản lý."),
            new("Chat với Ban quản lý hoặc gọi khẩn ở đâu?", "Trong Cổng cư dân, mở Liên hệ Ban quản lý. Phiên chat được ghép khi có Manager sẵn sàng. Dùng cuộc gọi hỗ trợ nếu tính năng hiển thị và tình huống phù hợp. Kênh ngoài giờ do chung cư công bố.", "chat khẩn cấp", "/resident/contact-management", "Mở liên hệ BQL · cần đăng nhập")
        ]),
        new("ai", "Trợ lý AI",
        [
            new("Chatbot trả lời từ nguồn nào?", "Trợ lý hỗ trợ tra cứu nội dung hướng dẫn khi được cấu hình. Mở nguồn trích dẫn nếu câu trả lời có nguồn và kiểm tra lại nội dung. Ban quản lý xác nhận quyết định nghiệp vụ."),
            new("Khi nào nên hỏi Ban quản lý thay vì AI?", "Hỏi Ban quản lý khi cần xác nhận hồ sơ, nghĩa vụ thanh toán, ngoại lệ hoặc xử lý khẩn. Tránh nhập OTP, mật khẩu, giấy tờ và chi tiết riêng tư không cần thiết.")
        ])
    ];
}
