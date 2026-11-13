# Rà soát nội dung pháp lý trước khi ban hành

Trạng thái hiện tại là **dự thảo chưa ban hành**. Người có thẩm quyền duyệt nội dung theo pháp luật và cấu hình triển khai thực tế. [Luật 91/2025/QH15](https://vanban.chinhphu.vn/?docid=214590&pageid=27160) · [Nghị định 356/2025/NĐ-CP](https://vanban.chinhphu.vn/?docid=216387&pageid=27160).

- [ ] Xác minh pháp nhân, vai trò kiểm soát/xử lý, đầu mối dữ liệu và kênh liên hệ.
- [ ] Chốt mục đích, căn cứ theo từng luồng, các trường dữ liệu cần thiết và đối tượng nhận.
- [ ] Xác nhận nhà cung cấp đang bật, nơi xử lý, chuyển dữ liệu, quyền truy cập, dữ liệu nhạy cảm, dữ liệu trẻ em nếu có và nghĩa vụ đánh giá tác động.
- [ ] Xác minh retention cho dữ liệu gốc, ảnh/derived, audit, log, inbox, thư, chat, bản sao và cache. Chốt ngoại lệ theo nghĩa vụ lưu.
- [ ] Chốt cách tiếp nhận quyền dữ liệu, xác minh danh tính, thời hạn và phản hồi, lưu bằng chứng và xử lý sự cố.
- [ ] Đối chiếu cách phát hành chứng từ, điều kiện thanh toán, đơn vị chịu trách nhiệm và cơ chế xử lý sai lệch.
- [ ] Chốt phiên bản, ngày hiệu lực, thông báo thay đổi, bản lưu đã duyệt và người chịu trách nhiệm.
- [ ] Phân biệt thông báo, xác nhận đã đọc, chấp thuận điều khoản và đồng ý cho từng mục đích. Mọi yêu cầu ghi nhận về sau cần quyết định riêng.
- [ ] Đối chiếu ảnh Cloudinary cũ, SMTP thật, SePay live, Gemini, WebRTC/TURN, CDN/font/weather, hosting và nơi nhận log trước khi dùng dữ liệu thật.

Nguồn nội bộ: [Audit pháp lý và riêng tư](../audits/legal-privacy-review.md), [Bảo mật và hồi quy](../audits/security-privacy-regression.md).
