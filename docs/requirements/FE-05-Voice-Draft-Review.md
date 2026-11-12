# FE-05.1 — Nhập yêu cầu bằng giọng nói và kiểm tra bản nháp

Phạm vi được cập nhật theo yêu cầu của người dùng: cư dân nói hoặc nhập mô tả; AI nhắc thông tin còn thiếu trước khi cư dân gửi. FE-10/FE-11 của Manager tiếp tục sở hữu đề xuất phân loại, ưu tiên và phương án xử lý sau khi nhận yêu cầu.

## Luồng cư dân

1. Bấm **Nhập bằng giọng nói**, cho phép micro và nói tiếng Việt. Khi đã có mô tả, nút đổi thành **Nói bổ sung**; nội dung mới nối vào phần đã nhập.
2. Bấm **Dừng nói**, hoặc chờ nhận diện kết thúc sau một khoảng im lặng. Chữ nhận diện cuối cùng được cập nhật trước khi kiểm tra AI.
3. AI kiểm tra toàn bộ mô tả. Nếu thiếu thông tin, popup hỏi những mục còn thiếu; cư dân có thể nói bổ sung hoặc sửa bằng bàn phím.
4. Nội dung nhập bằng bàn phím có nút **Kiểm tra nội dung**. Bấm **Gửi yêu cầu** cũng kiểm tra nếu nội dung hiện tại chưa được kiểm tra.
5. AI chỉ tư vấn. Không tự tạo đơn khi nhận diện xong, không tự từ chối yêu cầu. Nếu AI chưa kiểm tra được hoặc đề nghị bổ sung, khi gửi có popup cho phép cư dân chủ động **Vẫn gửi nội dung này** tới BQL.

Hai ô ngày hẹn/khung giờ được bỏ khỏi form. Không bắt buộc lịch hẹn trong bản nháp. Khách thăm, lưu trú, đăng ký xe và chuyển đồ không bị hỏi lịch hẹn nhân viên. Với dịch vụ cần điều phối nhân viên, chỉ trích lịch nếu cư dân đã chủ động nêu; lịch có phần tóm tắt để cư dân kiểm tra trước khi gửi. Phần ảnh/video được giữ nguyên khả năng hiện có; thay đổi này không bổ sung API lưu tệp hoặc nhận diện ảnh.

## Nội dung cần kiểm tra

| Yêu cầu | Thông tin cần có trong mô tả |
|---|---|
| Khách đến thăm thông thường | Họ tên khách; không mặc định hỏi số ngày ở lại hay lịch hẹn |
| Khách đăng ký lưu trú/ở qua đêm | Họ tên khách, thời gian lưu trú nếu chưa nêu; chỉ áp dụng khi cư dân đã nói rõ ý định lưu trú |
| Đăng ký xe | Loại xe, biển số, màu xe; không yêu cầu lịch hẹn |
| Chuyển đồ/chuyển nhà | Đồ cần chuyển, chuyển vào/ra hoặc trong tòa nhà; thời gian có thể ghi thêm, không bắt buộc |
| Đăng ký dịch vụ chung cư chung chung | Hỏi rõ dịch vụ muốn đăng ký trước; không đồng thời hỏi vị trí hoặc lịch cho một dịch vụ chưa xác định |
| Sửa chữa/vệ sinh/thi công đã xác định | Nhu cầu/hiện tượng và vị trí nếu còn thiếu; lịch là tùy chọn |
| Khác (thủ tục, tra cứu, hỗ trợ hóa đơn) | Nội dung cần hỗ trợ; không mặc định yêu cầu lịch hẹn |

Không hỏi lại thông tin đã có, không hỏi lại số căn hộ đã được hệ thống xác định. Không hỏi thông tin khách khi đăng ký xe/chuyển đồ hoặc ngược lại. Không biến khách đến chơi thành khách lưu trú. Với tình huống nguy hiểm đang xảy ra, không hỏi lịch hẹn; nhắc thêm vị trí/nội dung nếu chưa rõ. Cư dân không nêu lịch hoặc nói **bất kỳ lúc nào/không có yêu cầu lịch riêng** đều không bị nhắc thiếu lịch.

## Tích hợp

- `POST /api/v1/resident/service-requests/draft-check`, body `{ "description": "..." }`.
- Dùng policy cư dân hiện có; xác định quan hệ cư trú đang hiệu lực từ tài khoản và ngày hiện tại theo múi giờ chung cư.
- Trả `available`, `missingInformation`, `preferredDate`, `preferredTimeCode`, `notice`. Không trả lại văn bản hoặc dữ liệu cá nhân do AI tạo ra.
- `available: false` khác với nội dung đầy đủ; lỗi quota/timeout không được báo là đã kiểm tra thành công.
- Cấu hình Gemini dùng chung `AiChatbot:Gemini`; không có khóa AI trong trình duyệt.
- Giới hạn kiểm tra theo tài khoản bằng `AiClassification:DraftChecksPerMinute` (mặc định 5). Client dùng lại kết quả cho đúng mô tả hiện tại; sửa chữ thì hủy hiệu lực kết quả cũ.
- Ngày/giờ chỉ được trích nếu có đoạn bằng chứng trong mô tả; ngày không hợp lệ/quá khứ bị loại. API gửi đơn tiếp tục kiểm tra ngày và mã khung giờ như trước.
- Gemini trả ngữ cảnh `requestKind` nội bộ. Backend lọc thông tin thiếu theo ngữ cảnh, không chỉ dựa vào câu hướng dẫn cho AI. `VISITOR` chỉ hỏi tên; `VISITOR_STAY` mới được hỏi thời gian lưu trú; `MOVING` hỏi đồ cần chuyển và hướng chuyển; `SERVICE` hỏi loại dịch vụ/nội dung/vị trí tùy thông tin đã có. Không có loại đăng ký nào được mặc định coi việc chưa nêu lịch là thiếu thông tin.
- `PreferredDate`/`PreferredTimeCode` chỉ được trích cho `SERVICE` khi cư dân chủ động nêu lịch và có bằng chứng trong mô tả. Ngày khách đến, lưu trú, ngày bắt đầu đăng ký xe và thời gian chuyển đồ giữ nguyên trong mô tả, không được sao chép thành lịch hẹn nhân viên. Thủ tục khác và tình huống nguy hiểm cũng không tự gán lịch hẹn.
- Kiểm tra bản nháp không ghi database. Khi cư dân chủ động gửi, mô tả được lưu nguyên nội dung (chỉ bỏ khoảng trắng đầu/cuối); lịch không áp dụng được lưu `null`. Không cập nhật lại các đơn đã gửi từ trước.

## Giọng nói và dữ liệu

Web Speech API được phát hiện theo khả năng trình duyệt và chỉ bật trong ngữ cảnh an toàn. Trình duyệt có thể sử dụng dịch vụ nhận diện trực tuyến; giao diện thông báo điều này trước khi dùng micro. Không có chức năng lưu bản ghi âm ở PropFlow. Khi không hỗ trợ/bị từ chối quyền/mất mạng, vẫn nhập được bằng bàn phím.

Tên khách có nhãn, biển số, điện thoại và email theo các mẫu được che trước khi gửi văn bản tới Gemini. AI chỉ trả mã thông tin thiếu và metadata lịch, không trả lại tên/biển số. Không lưu bản nháp, kết quả kiểm tra hoặc transcript vào database/browser storage trong luồng này.

Đóng popup tạo yêu cầu hoặc rời trang phải hủy nhận diện và yêu cầu kiểm tra đang chờ. Callback giọng nói cũ và kết quả AI cho mô tả đã sửa không được thay đổi bản nháp mới.
