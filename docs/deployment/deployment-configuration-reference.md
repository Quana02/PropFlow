# Tham chiếu cấu hình triển khai

| Key | Nơi cấp | Kiểm chứng |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | API và Web, Production cho artifact rehearsal | Swagger trả 404, shell và API hoạt động |
| `ConnectionStrings__PropFlowDatabase` | Secret phía API | DB runtime đúng môi trường, kết nối mã hóa theo chuẩn nhà cung cấp |
| `PROPFLOW_MIGRATION_DATABASE` | Tiến trình migration riêng | Dùng `status` trước và sau `migrate` |
| `Authentication__Issuer`, `Authentication__Audience` | Cấu hình API | JWT từ issuer hoặc audience khác bị từ chối |
| `Authentication__PrivateKeyPem`, `Authentication__OtpHashKey` | Secret phía API | RSA từ 2048 bit, OTP key base64 ít nhất 32 byte ngẫu nhiên |
| `DataProtection__KeyPath` | API, volume bền vững | Cookie CSRF và dữ liệu được bảo vệ dùng được sau restart |
| `Authentication__CrossSiteCookie` | API | Giữ false với cùng site. Kiểm tương thích trình duyệt nếu thiết kế cross-site |
| `ReverseProxy__KnownProxies__0` | API và Web | IP proxy được phép, proxy ghi đè forwarded headers từ client |
| `Cors__AllowedOrigins__0` | API nếu hai origin | Origin HTTPS của Web. Kiểm allowlist và credentials |
| `Api:BaseUrl` | `wwwroot/appsettings.json` của Web | Dùng `/` trong topology cùng origin, chỉ chứa dữ liệu công khai |
| `Cloudinary__CloudName`, `Cloudinary__ApiKey`, `Cloudinary__ApiSecret` | API qua secret store | Upload và đọc ảnh thử nghiệm trong đúng account |
| `Authentication__Mail__Host`, `Port`, `From`, `Username`, `Password` trong section `Authentication:Mail` | API qua secret store | STARTTLS và hộp thư giả được duyệt |
| `Payments__SePay__Enabled`, `Payments__SePay__OAuth__Enabled` | API | Mặc định tắt trong rehearsal. Giá trị chi tiết theo runbook SePay |
| `AiChatbot__Gemini__Enabled`, `AiChatbot__Gemini__ApiKey`, `AiChatbot__Gemini__AnswerModel`, `AiChatbot__Gemini__EmbeddingModel` | API qua secret store | Xác minh model, quota và timeout trong project staging |
| `Communication__EmergencyCalls__StunUrls__0`, `Communication__EmergencyCalls__TurnUrls__0`, `Communication__EmergencyCalls__TurnSharedSecret`, `Communication__EmergencyCalls__TurnCredentialLifetimeSeconds` | API, TURN secret qua secret store | Kiểm relay trong hai mạng khác nhau và thời hạn credential |

Công cụ staging cần gán secret cho tiến trình API từ secret store. WebAssembly chỉ nhận base URL và public configuration. [Runbook](authorization-hardening-runbook.md) · [Triển khai SePay](../sepay-live-deployment.md)
