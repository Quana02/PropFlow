# Runbook triển khai Authorization Hardening 1.1

## Kiến trúc và cấu hình

Chạy một API ASP.NET Core 8 với registry SignalR và một Web host phục vụ Blazor WASM. Hai process dùng chung public origin qua reverse proxy là cấu hình ưu tiên. Lưu trạng thái phiên trong PostgreSQL. Scale-out API cần cơ chế phát sự kiện thu hồi và đóng connection theo instance, cấu hình SignalR phù hợp, bộ khóa chung và thử nghiệm lại giới hạn tốc độ.

| Cấu hình | Giá trị cấp qua môi trường hoặc secret provider |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` trên cả API và Web |
| `ConnectionStrings__PropFlowDatabase` | Kết nối PostgreSQL của deployment. Dùng tài khoản runtime theo quyền cần thiết |
| `Authentication__Issuer`, `Authentication__Audience` | Giá trị cố định của deployment |
| `Authentication__PrivateKeyPem` | Khóa RSA riêng tối thiểu 2048 bit, giữ ổn định giữa restart |
| `Authentication__OtpHashKey` | Khóa HMAC base64 của ít nhất 32 byte ngẫu nhiên, giữ ổn định giữa restart |
| `DataProtection__KeyPath` | Đường dẫn ổ bền vững trên API. Chỉ tài khoản API có quyền cần thiết. Bảo vệ và sao lưu cùng DB |
| `ReverseProxy__KnownProxies__0` | Địa chỉ proxy đáng tin cậy, khai báo trên cả hai host nếu dùng forwarded headers |
| `Cors__AllowedOrigins__0` | Origin HTTPS chính xác của Web khi Web/API khác origin |
| `Authentication__CrossSiteCookie` | Mặc định false. Thiết lập true cho topology cross-site đã xác minh chính sách cookie trình duyệt |
| `Api:BaseUrl` | Cấu hình WASM công khai tại `wwwroot/appsettings.json`. Dùng `/` với reverse proxy cùng origin |

Proxy chuyển `/api/v1/` và `/hubs/` tới API, các route Web và static assets về Web host. Chuyển đúng scheme HTTPS từ proxy chỉ định, hỗ trợ Upgrade WebSocket và Long Polling, đặt timeout vượt chu kỳ keepalive, và lược bỏ query string ở log hub để tránh ghi `access_token`. Cookie refresh và antiforgery dùng Secure, HttpOnly và path `/api/v1/auth`. Luồng refresh/logout tiếp tục yêu cầu CSRF. Kiểm CORS với credentials và origin chính xác khi tách origin. Xác minh callback, timeout và network policies ở môi trường thật trước mở traffic.

## Phát hành và cập nhật DB

[Bộ tài liệu chuyển giao 1.2](README.md) có lệnh migration theo thứ tự liên module, diễn tập sao lưu/khôi phục, preflight và hướng dẫn staging từng bước. Runtime .NET 8 hiện đã hết hỗ trợ. Xem [điều kiện phát hành](../audits/propflow-authorization-hardening-1.2-report.md#runtime-và-deployment).

```powershell
dotnet publish src/PropFlow.Api/PropFlow.Api.csproj -c Release -o artifacts/release/api
dotnet publish src/PropFlow.Web/PropFlow.Web.csproj -c Release -o artifacts/release/web
```

1. Sao lưu PostgreSQL, RSA/OTP key và Data Protection key ring. Ghi nhận migration version, xác minh có thể khôi phục bản sao trước khi nâng.
2. Dừng nhận request mới và drain API cũ. Dùng migration runner đã duyệt với quyền DDL. Với database đã hoạt động, áp dụng `20261115110000_BindAccessTokensToSessions` của `AuthenticationDbContext` sau `20261113075102_AddAccountAvatar`. Với DB mới, dùng thứ tự khởi tạo schema liên module của dự án trước các migration bổ sung ràng buộc liên schema.
3. Kiểm tra các cột `session_id`, `access_fingerprint`, index `IX_refresh_tokens_user_id_session_id` và migration history. Migration đánh dấu refresh token còn hoạt động trước upgrade là `session_binding_upgrade`. Người dùng đăng nhập lại một lần, JWT cũ thiếu SID nhận 401.
4. Khởi động API với cùng nguồn khóa, sau đó Web và reverse proxy. Kiểm luồng smoke trước khi mở ingress. Thông báo phiên phải đăng nhập lại khi rollout.

## Smoke và rollback

- Kiểm HTTPS, tải WASM, CSRF, login, refresh, cùng-device logout, bearer trước/sau rotation đều bị chặn sau logout và thiết bị thứ hai còn phiên. Kiểm 401 anonymous và 403 actor hợp lệ thiếu quyền.
- Kiểm thương lượng và kết nối WebSocket qua cả bảy URL. Mở nhiều tab, đăng xuất ở một tab và đối chiếu transport đóng. Kiểm đổi role, disable/re-enable và replay JWT/cookie. Kiểm phân công lại FE-07 trong khi mở task và thử URL ảnh từ hai đợt tham gia.
- Khởi động lại API và Web với DB và key ring giữ nguyên. So sánh phiên còn hợp lệ và phiên đã thu hồi. Theo dõi hai metric `propflow.signalr.connections` và `propflow.signalr.revocations` cùng latency HTTP/SignalR.
- Ưu tiên sửa tiến với schema đã nâng. Downgrade migration đánh dấu các phiên có SID thu hồi trước khi xóa cột. Binary cũ có thể chấp nhận bearer cũ đến khi JWT hết hạn. Nếu buộc rollback binary, lập kế hoạch đổi khóa ký hoặc chặn toàn bộ JWT cũ tại ingress trước khi mở traffic. Khôi phục từ backup có thể lấy lại trạng thái phiên trước lúc thu hồi, cần thu hồi lại và thay khóa trước khi phục vụ người dùng.

## Trách nhiệm và kiểm chứng môi trường thật

| Hạng mục | Chủ trì |
| --- | --- |
| Build, ma trận hồi quy, fixture PostgreSQL và smoke local | Nhóm phát triển |
| Sao lưu và migrate DB của deployment, quyền thư mục, bảo vệ khóa | Người vận hành và quản trị DB |
| Proxy TLS, DNS, cấu hình startup, quan sát và lịch triển khai | Người vận hành |
| Cloudinary signed delivery/revocation, SMTP, Gemini, SePay/OAuth và TURN/WebRTC bên ngoài | Người vận hành cùng quản trị tài khoản tích hợp |

Kiểm thử local dùng dữ liệu tổng hợp. Chỉ chạy kiểm chứng provider trên tài khoản và môi trường đã có quyền. [SePay deployment](../sepay-live-deployment.md) · [Microsoft SignalR hosting](https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration?view=aspnetcore-8.0)
