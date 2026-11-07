# Triển khai thanh toán SePay Live cho PropFlow

Ứng dụng chỉ cung cấp thanh toán thực tế. Màn hình, API cấu hình, webhook và giả lập Sandbox đã được gỡ. Bảng và migration thử nghiệm cũ được giữ để bảo toàn lịch sử, không tham gia số dư, QR, thông báo hay quyền đánh giá.

## Thông tin cần thống nhất với leader

- Nơi chạy .NET 8 API và Web, PostgreSQL; tên miền HTTPS ổn định.
- Nơi lưu thông tin bí mật và nơi lưu Data Protection keys bền vững qua các lần deploy/restart.
- Tài khoản SePay Live và ngân hàng nhận tiền do đơn vị quản lý sở hữu.
- Cách reverse proxy chuyển `/api/` và các hub thời gian thực về API; các đường dẫn giao diện về Web.

Tài liệu này không chọn nhà cung cấp và không tự bật nhận tiền. Không cần tạo ứng dụng OAuth nếu sử dụng API Token Live.

## Cấu hình máy chủ API

Thiết lập bằng secret manager/biến môi trường của nơi deploy. Trong local dùng User Secrets của PropFlow.Api. Không commit khóa thật.

| Biến | Giá trị |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__PropFlowDatabase` | Connection string database đã migrate |
| `DataProtection__KeyPath` | Đường dẫn tuyệt đối đến thư mục/volume bền vững, chỉ tài khoản chạy API được đọc/ghi |
| `Payments__SePay__OAuth__Enabled` | `false` cho cách dùng API Token |
| `Payments__SePay__ApiToken` | Token tạo trong SePay **Live**, không dùng token thử hoặc Merchant Secret |
| `Payments__SePay__WebhookPublicBaseUrl` | Origin HTTPS công khai của API, ví dụ `https://propflow.example.com`; không kèm `/api/`, query hoặc fragment |
| `Payments__SePay__AuthenticationMode` | `API_KEY` |
| `Payments__SePay__WebhookApiKey` | Khóa ngẫu nhiên riêng ít nhất 32 ký tự, nhập cùng giá trị trong webhook SePay |
| `Payments__SePay__Enabled` | Ban đầu `false`; chuyển `true` sau khi hoàn tất thiết lập bên dưới |
| `Cors__AllowedOrigins__0` | Origin HTTPS chính xác của Web |
| `ReverseProxy__KnownProxies__0` | Nếu qua reverse proxy, IP thật của proxy gần ứng dụng nhất; chỉ khai báo proxy tin cậy |

HMAC cũng được hỗ trợ: đặt `AuthenticationMode=HMAC` và `Payments__SePay__WebhookSecret` ít nhất 32 ký tự; cấu hình HMAC cùng khóa trên SePay. Khi chọn HMAC, API Key không thay thế chữ ký thiếu/sai.

Cấu hình đăng nhập/JWT, OTP email và Cloudinary vẫn cần được cung cấp qua thông tin bí mật như hiện tại. Web dùng `Api:BaseUrl=/` khi API được đưa ra cùng origin; nếu tách host, cập nhật cấu hình công khai `wwwroot/appsettings.Production.json` của **Web.Client** trước publish và CORS của API. Biến môi trường trên Web server không tự thay thế JSON của ứng dụng WASM tải về trình duyệt. Web không được chứa token SePay hay Client Secret.

Reverse proxy cần ghi đè các header `X-Forwarded-For` và `X-Forwarded-Proto` từ kết nối thật. Áp dụng `ReverseProxy:KnownProxies` cho cả API và Web nếu TLS kết thúc ở proxy; không mở tin cậy mọi IP. Thiết lập đang hỗ trợ một proxy sát ứng dụng (`ForwardLimit=1`).

### Giữ khóa mã hóa khi chuyển máy

Nếu dùng lại database local có tài khoản ngân hàng/token đã mã hóa, sao chép bộ **Data Protection keys hiện có** một cách bảo mật vào thư mục `DataProtection:KeyPath` trên máy chủ. Trên Windows local mặc định thường nằm ở `%LOCALAPPDATA%/ASP.NET/DataProtection-Keys`; xác minh nơi lưu thật của môi trường đang chạy. Giữ application name `PropFlow` và quyền truy cập thư mục. Tạo key ring hoàn toàn mới sẽ không giải mã được dữ liệu cũ. Sao lưu database và keys cùng chính sách; không đưa keys vào Git hoặc ảnh Docker công khai.

API Production sẽ báo lỗi khi chưa có `DataProtection:KeyPath`. Nếu bật SePay, API kiểm tra URL HTTPS công khai và khóa webhook ngay khi khởi động. Đây là kiểm tra cấu hình; vẫn phải kiểm tra kết nối SePay và gửi webhook thực tế.

## Thiết lập lần đầu

1. Sao lưu và migrate database theo thứ tự module; khởi động API/Web với `Payments:SePay:Enabled=false`.
2. Admin → Thanh toán → Kiểm tra kết nối → Đồng bộ tài khoản.
3. Chọn đúng ngân hàng từ SePay, kiểm tra QR mẫu, xác nhận OTP email để lưu. Mỗi thay đổi hoặc vô hiệu hóa tài khoản vẫn yêu cầu OTP.
4. Trong **SePay Live → Tích hợp Webhook**, thêm URL `https://TEN-MIEN-API/api/v1/payments/webhooks/sepay`, chọn **Tiền vào**, đúng tài khoản nhận tiền, JSON và API Key/HMAC tương ứng. Bật retry; tắt bộ lọc chỉ gửi khi có mã hoặc bảo đảm mã `PF` + 22 ký tự hex được nhận diện đầy đủ.
5. Đặt `Payments:SePay:Enabled=true`, khởi động lại API. Kiểm tra trạng thái cấu hình trong Admin. Khi chưa chọn ngân hàng, một số bước có thể vẫn báo chưa sẵn sàng.
6. Tạo hóa đơn mới, quét QR bằng ứng dụng ngân hàng, kiểm tra người nhận, số tiền và giữ nguyên nội dung `PF...`.
7. Kiểm tra giao dịch trên SePay, lịch sử gửi webhook và kết quả đối soát PropFlow. HTTP 200 chỉ chứng minh đã nhận vào inbox; kết quả hóa đơn phải được xác minh sau khi worker xử lý.
8. Xác nhận hóa đơn giảm đúng dư nợ, không cộng hai lần khi SePay gửi lại; khoản không khớp nằm ở màn hình kế toán xử lý giao dịch.

QR cũ giữ tài khoản nhận đã gắn với hóa đơn để bảo toàn đối soát. Thay tài khoản trong Admin không tự đổi QR đã phát hành. Số tiền thử cũ không biến thành tiền thật và hóa đơn chưa có khoản thu xác nhận vẫn phải được thanh toán.

## Kiểm tra trước khi mở cho cư dân

- Địa chỉ HTTPS hoạt động liên tục, API nhận được POST webhook từ Internet; không sử dụng tunnel Sandbox cũ.
- Thông tin ngân hàng đúng, email OTP gửi được, key ring sống qua restart/deploy.
- Chuyển khoản đủ/thiếu, gửi webhook lặp, sai nội dung/tài khoản và hóa đơn đã thanh toán được xử lý đúng.
- Khi không nhận được webhook, cư dân có thể gửi mã giao dịch cho kế toán đối soát; không tự xác nhận chỉ vì cư dân bấm nút hoặc trình duyệt quay về trang thành công.
- Chạy kiểm thử, publish API/Web, cấu hình process manager và lịch sao lưu database/keys theo nơi deploy đã chọn.

OAuth vẫn là tùy chọn sau này: dùng ứng dụng được SePay phê duyệt và redirect HTTPS `/admin/sepay/callback`. Merchant ID/Secret Key của Payment Gateway không thay thế API Token hay Client ID/Secret OAuth.
