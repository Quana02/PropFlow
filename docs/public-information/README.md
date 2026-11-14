# Các trang thông tin công khai của PropFlow

Bốn tuyến công khai dùng `PublicInformationLayout`, header công khai hiện có và token giao diện PropFlow.

| Tuyến | Nội dung |
| --- | --- |
| `/terms` | Điều khoản dự thảo, phạm vi dịch vụ, tài khoản, giao dịch và hỗ trợ |
| `/privacy` | Chính sách dữ liệu cá nhân dự thảo, nhóm dữ liệu, quyền truy cập và phụ thuộc vận hành |
| `/help` | FAQ tiếng Việt, lọc chủ đề và tìm có dấu hoặc không dấu ngay trên trình duyệt |
| `/contact` | Hướng dẫn liên hệ theo vấn đề, gồm các bước cho người chưa đăng nhập |

Nội dung nằm trong `src/PropFlow.Web.Client/Features/PublicInformation`. Điều khoản và chính sách luôn hiện nhãn dự thảo. Công bố bản chính thức cần nội dung và thông tin đơn vị vận hành đã phê duyệt.

Liên kết trên form và trang nghiệp vụ mở tab mới để giữ trạng thái tại chỗ. Landing và Login dùng điều hướng cùng tab. Không có lưu thêm mật khẩu, OTP hoặc hồ sơ để giữ trạng thái form.

- [Rà soát pháp lý](legal-content-review-checklist.md)
- [Bản đồ nội dung FAQ](resident-help-content-map.md)
- [Phân tuyến hỗ trợ](support-contact-routing.md)
- [Báo cáo kiểm thử và triển khai](../audits/public-information-verification.md)
