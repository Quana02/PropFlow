# Gọi thoại khẩn cấp cư dân – Ban quản lý

Phần mở rộng do người dùng yêu cầu cho khu vực Dịch vụ của cư dân: bấm **Khẩn cấp** để báo ngay và gọi thoại trong web tới một Manager đang trực. Manager nhận giao diện khẩn cấp trên mọi trang, không phải mở màn hình chat trước.

## Luồng sử dụng

1. Cư dân bấm **Khẩn cấp**. Máy chủ ghi nhận báo khẩn cấp ngay, không yêu cầu tạo đơn dịch vụ, điền lịch hẹn hoặc đợi AI phân tích.
2. Hệ thống tìm Manager có phiên trực tuyến và chưa có cuộc gọi khẩn cấp khác. Cuộc gọi khẩn cấp ưu tiên cả khi Manager đang xử lý chat thường; không xóa lịch sử chat đó.
3. Cả hai bên thấy giao diện cuộc gọi phủ toàn màn hình. Manager thấy tên cư dân và căn hộ đang cư trú; căn hộ không được mặc định là vị trí xảy ra sự cố.
4. Trình duyệt yêu cầu quyền micro nếu chưa được cấp. Khi được phép, WebRTC tự thiết lập âm thanh hai chiều; không cần Manager bấm nhận trong ứng dụng. Trình duyệt vẫn có thể yêu cầu bấm **Bật âm thanh** do quy định tự động phát.
5. Chỉ khi cả hai trình duyệt xác nhận kết nối media, máy chủ mới ghi nhận `CONNECTED`. Có nút tắt/bật micro, kết nối lại, kết thúc; kết thúc sẽ giải phóng micro và đường truyền.
6. Nếu không có Manager phù hợp, báo khẩn cấp ở hàng chờ. UI nói rõ chưa có người nhận; không hiển thị đã kết nối hoặc đã có người hỗ trợ.

Giao diện có nút gọi trực tiếp **114**. Đây là liên kết mở trình gọi của thiết bị, không phải PropFlow tự gọi cơ quan cứu nạn. Không chờ ứng dụng khi đang có nguy hiểm.

## Trạng thái và dữ liệu

| Trạng thái | Ý nghĩa |
| --- | --- |
| `WAITING` | Đã ghi nhận, chờ Manager trực |
| `CONNECTING` | Đã phân công Manager, đang thiết lập hoặc khôi phục thoại |
| `CONNECTED` | Hai phía xác nhận kết nối WebRTC |
| `ENDED` | Người tham gia đã kết thúc cuộc gọi |

Mỗi cư dân chỉ có một cuộc gọi chưa kết thúc; mỗi Manager chỉ nhận một cuộc gọi khẩn cấp cùng lúc. Bấm lặp hoặc gửi song song không tạo nhiều cuộc gọi. Một cuộc gọi chỉ gắn một tab của mỗi người; tab khác không được nhận tín hiệu âm thanh. Khi nối lại hoặc đổi người nhận, phiên đàm phán có revision mới để bỏ tín hiệu cũ.

Báo khẩn cấp, người nhận và mốc thời gian được lưu trong schema `communication`. Phiên kết nối có heartbeat và thời hạn; phiên hết hạn được dọn, Manager mất kết nối được phân công lại. Thông báo realtime giúp cập nhật ngay; kiểm tra định kỳ phục hồi khi bỏ lỡ thông báo. Không ghi âm, không lưu SDP/ICE vào database và không gửi nội dung cuộc gọi tới AI.

API và hub kiểm tra quyền cùng phạm vi cư trú/người được phân công. Tín hiệu thoại chỉ gửi tới connection được gắn với cuộc gọi. Manager không được tạo báo khẩn cấp thay cư dân qua API này. Giới hạn tần suất và kích thước tín hiệu được áp dụng phía máy chủ.

## Thử trên máy phát triển

1. Chạy API và Web sau khi áp dụng migration `20261112082535_AddEmergencyVoiceCalls` cho `CommunicationDbContext`.
2. Dùng hai hồ sơ trình duyệt khác nhau hoặc một cửa sổ thường và một cửa sổ ẩn danh. Đăng nhập Manager ở một cửa sổ, cư dân có liên kết cư trú hợp lệ ở cửa sổ còn lại.
3. Cho Manager mở bất kỳ trang nào; cư dân vào **Dịch vụ → Khẩn cấp**.
4. Cho phép micro ở cả hai cửa sổ. Kiểm tra Manager tự thấy giao diện khẩn cấp, hai bên nghe được nhau; dùng tai nghe để tránh vọng khi cùng máy/phòng.
5. Thử tắt micro, kết nối lại, kết thúc; thử không có Manager trực để kiểm tra thông báo đang chờ.

Kiểm tra tự động: unit/domain và phân quyền, PostgreSQL/SignalR thực, kiểm tra vòng đời JavaScript và Chromium WebRTC thực với micro giả lập. Kiểm tra Chromium xác nhận có gói âm thanh hai chiều, không chỉ xác nhận UI đổi trạng thái. Đây chưa phải thử nghiệm hai thiết bị trên các mạng Internet khác nhau.

## Cấu hình khi triển khai

Web cần HTTPS, API cho phép origin Web, và proxy chuyển tiếp được SignalR/WebSocket. `localhost` là ngoại lệ trình duyệt cho phép thử micro trên máy phát triển; HTTP qua địa chỉ IP LAN thường không được cấp micro.

Cấu hình không bí mật mẫu nằm trong `src/PropFlow.Api/appsettings.json`:

```json
{
  "Communication": {
    "EmergencyCalls": {
      "StunUrls": ["stun:stun.l.google.com:19302"],
      "TurnUrls": [],
      "TurnCredentialLifetimeSeconds": 600
    }
  }
}
```

STUN mặc định phục vụ thử nghiệm và các mạng cho phép kết nối trực tiếp. Để triển khai gọi ổn định qua các mạng/NAT khác nhau, cần bổ sung máy chủ TURN. Chưa có TURN được cấp sẵn trong dự án này.

Với coturn bật cơ chế TURN REST dùng shared secret, điền URL `turn:`/`turns:` vào `Communication:EmergencyCalls:TurnUrls` và lưu secret chỉ phía API bằng User Secrets hoặc biến môi trường `Communication__EmergencyCalls__TurnSharedSecret`. Secret phải khớp cấu hình coturn; không đưa secret vào cấu hình Web hoặc Git. API cấp username/credential có hạn sử dụng cho người đã xác thực, không trả shared secret.

Tham khảo cơ chế credential TURN: [tài liệu coturn](https://github.com/coturn/coturn/wiki/turnserver).

Chưa hỗ trợ gọi khi Manager đã đóng ứng dụng, thông báo cuộc gọi qua hệ điều hành, tổng đài điện thoại, hoặc tự gọi 114. Khi triển khai nhiều API instance cần hạ tầng chia sẻ realtime của SignalR; bản hiện tại dùng cơ chế realtime một API instance.
