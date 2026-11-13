# Điều kiện demo, pilot và production

Demo dùng dữ liệu giả đã có bằng chứng trên Chrome. Pilot với dữ liệu cư dân và production còn phụ thuộc nhà cung cấp, phê duyệt pháp lý và quy trình vận hành. Mức ưu tiên dưới đây áp dụng theo luồng sẽ bật.

## Trước khi dùng dữ liệu hoặc tiền thật

| Ưu tiên | Điều kiện | Bằng chứng cần có |
| --- | --- | --- |
| P0 · Ảnh FE-05 | Mã nguồn dùng Cloudinary authenticated cho ảnh mới. Ảnh public cũ và biến thể CDN cần kiểm kê và chuyển đổi theo kế hoạch được duyệt. | Thử bằng ảnh giả trên tenant kiểm thử. URL original/derived yêu cầu xác thực. STAFF ngoài phân công bị từ chối. Thu hồi và CDN invalidation được xác nhận. Có backup và đối chiếu khi chuyển ảnh cũ. |
| P0 · Pháp lý và dữ liệu | Duyệt Terms/Privacy, xác nhận pháp nhân, mục đích, đơn vị xử lý, vendor, vùng lưu trữ, retention và đầu mối thực thi quyền dữ liệu. | Phê duyệt nội dung và ngày hiệu lực. Diễn tập xác minh danh tính, xử lý yêu cầu của chủ thể và sự cố. [Checklist](../public-information/legal-content-review-checklist.md). |
| P0 · AI và bản nháp chat | Chốt payload Gemini, nguồn tài liệu được dùng và phạm vi Manager nhận nội dung đang gõ. Giữ quyền quyết định ở người có thẩm quyền. | Kiểm payload/log bằng dữ liệu giả, thử Manager ngoài phiên, nguồn đã thu hồi, timeout và fallback. |
| P0 · Đối soát tiền thật | Xác minh SePay và quy trình giao dịch không khớp, chỉnh/hoàn, chứng từ và trách nhiệm xử lý. | Kiểm chữ ký, replay, dư nợ khi pending, sai đích và phục hồi sau outage. [Triển khai SePay](../sepay-live-deployment.md). |
| P0 · Hạ tầng | Xác minh SMTP tới hộp thư, TURN, HTTPS/CORS, secrets, quyền log, lưu giữ và backup/restore. | Diễn tập thu hồi phiên đa tab, SMTP lỗi, gọi thoại qua mạng triển khai, khôi phục dữ liệu và truy vết sự cố. |

## Trước nghiệm thu

- **FE-02 và các trang công khai:** Đã chặn suy luận thông tin liên hệ qua tìm kiếm ACCOUNTANT. Bốn tuyến công khai đã chạy kiểm thử. Phê duyệt nội dung và kênh hỗ trợ vẫn là điều kiện vận hành. [Kiểm chứng bảo mật](security-privacy-regression.md) · [Kiểm chứng trang công khai](public-information-verification.md).
- **FE-05 và nghiệp vụ xuyên vai trò:** Chạy yêu cầu → phân công → cập nhật → duyệt → cư dân xem kết quả, gồm các ca hủy, chi phí và dấu vết chứng cứ. Nếu pilot cần cư dân gửi ảnh, chốt hợp đồng gửi tệp và quyền đọc. Bộ chọn hiện chỉ xem tên tệp cục bộ.
- **Hồi quy browser:** Xác minh OTP có challenge hợp lệ, tài khoản khóa, logout các role còn thiếu, gửi/đọc hết thông báo, các thao tác chat, đối soát, bộ lọc và phân trang nhiều trang. Ưu tiên phím Tab/Escape, focus, phóng to và màn nhỏ.
- **AI và tài liệu:** Đo chất lượng tiếng Việt, tình huống thiếu dữ kiện, nguồn nội bộ hoặc đã thu hồi, prompt injection và nhà cung cấp gián đoạn.
- **FE-15.8 và báo cáo:** Chốt phạm vi cấu hình hành chính trước khi mở hợp đồng. Với tính năng xuất báo cáo, duyệt mục đích, quyền, cột dữ liệu tối thiểu và audit trước khi mở giao diện. [Đặc tả FE-15](../requirements/FE-15-Administration.md).

## Tham chiếu sản phẩm

Benchmark ưu tiên ba mẫu có thể áp dụng trực tiếp. Trung tâm trợ giúp định tuyến theo loại vấn đề, timeline yêu cầu thể hiện người xử lý và bước tiếp theo, tài liệu quyền riêng tư giải thích vai trò của đơn vị vận hành và bên xử lý. Với AI, nêu nguồn, giới hạn và điểm cần người kiểm tra. Các mô hình thanh toán hoặc trách nhiệm pháp lý của sản phẩm nước ngoài cần đối chiếu với phạm vi PropFlow trước khi áp dụng. [BuildingLink Help](https://help-resident.buildinglink.com/en/support/solutions/articles/42000100528-when-to-contact-building-staff-or-buildinglink-support) · [AppFolio Online Portal](https://www.appfolio.com/help/online-portal) · [RentCafe Terms](https://resources.yardi.com/legal/rentcafe-terms-of-service/).
