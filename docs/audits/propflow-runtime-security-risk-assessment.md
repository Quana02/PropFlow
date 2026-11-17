# PropFlow — Đánh giá rủi ro runtime trước phát hành

Ngày kiểm chứng 16/11/2026. Công cụ và gói theo [SDK audit](propflow-sdk-efcore-npgsql-compatibility-audit.md). SDK 8.0.314, `net8.0`, EF Core 8.0.11, Npgsql provider 8.0.11, driver 8.0.6, PostgreSQL localhost 18.4. API/Web Release dùng framework-dependent publish. Các runtime cài trên máy được giữ nguyên.

## Runtime nạp thực tế

Runner ghi DLL đã nạp bằng danh sách module của PID API và Web sau restart. Hai tiến trình nạp CoreCLR từ `Microsoft.NETCore.App/8.0.33` và `Microsoft.AspNetCore.Hosting.dll` từ `Microsoft.AspNetCore.App/8.0.33`. API cũng nạp `Microsoft.AspNetCore.Http.Connections.dll` từ cùng shared framework. Bằng chứng áp dụng cho Release rehearsal trên loopback. WASM bundle được phát hành riêng từ runtime pack 8.0.17 theo SDK audit. [Danh sách module đã nạp](../../artifacts/authz/final-loaded-runtimes.json)

Lượt quét `dotnet list PropFlow.sln package --vulnerable --include-transitive` trực tiếp trên nguồn NuGet.org trả mã 0. Báo cáo liệt kê 32 project và không ghi nhận advisory cho graph gói, với phạm vi dữ liệu do feed cung cấp. Shared framework đã nạp được đánh giá riêng bằng advisory bên dưới. [Kết quả NuGet](../../artifacts/authz/final-nuget-vulnerability.json)

## Advisory và khả năng áp dụng

| Advisory | Dải liên quan và applicability |
| --- | --- |
| CVE-2026-114146, Critical 9.0 | SignalR server có thể xử lý invocation dưới danh tính khác nếu liên kết identity lúc negotiate không nhất quán. ASP.NET Core 8.0.33 đã nạp nằm trong dải ảnh hưởng của Microsoft, bản vá nhánh 8 là 8.0.34. PropFlow cung cấp 7 đường dẫn hub và dùng WebSocket/Long Polling. Kiểm thử phân quyền ở application layer độc lập với lỗi runtime. [Microsoft](https://github.com/dotnet/aspnetcore/security/advisories/GHSA-67wp-ghc2-fw2g) |
| CVE-2026-114149, High | Giới hạn số header trong multipart có thể bị vượt và gây tiêu thụ tài nguyên. API có upload minh chứng FE-05/07 và ảnh đại diện. 8.0.33 bị ảnh hưởng, 8.0.34 là bản vá nhánh 8. [Microsoft](https://github.com/dotnet/aspnetcore/security/advisories/GHSA-x33c-w48q-qrgc) |
| CVE-2026-114157, Named Pipes trên Windows | Runtime local thuộc dải ảnh hưởng. Source cấu hình HTTP(S) và không thấy `UseNamedPipes`. Applicability ở hosting cuối phụ thuộc transport đã cấu hình. [Microsoft](https://github.com/dotnet/aspnetcore/security/advisories/GHSA-7phj-vgxq-r67h) |
| Product lifecycle | .NET 8, ASP.NET Core 8 và EF Core 8 đã hết hỗ trợ 10/11/2026. 8.0.34 là bản vá cuối Microsoft liệt kê cho .NET 8. [Chính sách .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) · [Vòng đời EF](https://learn.microsoft.com/en-us/ef/core/what-is-new/) |

## Rủi ro còn lại và quyết định

- **Local và demo cô lập** dùng dữ liệu tổng hợp, listener loopback và proxy thử nghiệm. Quyền trên hub, lọc recipient, CSRF và kiểm soát ingress giảm phạm vi phơi nhiễm trong môi trường thử.
- **Staging truy cập công khai, pilot và production** còn chặn trên ASP.NET Core 8.0.33. WAF, giới hạn upload và proxy ACL có thể giảm một số lớp rủi ro. Chúng không chứng minh SignalR đã được vá và không kéo dài vòng đời .NET 8.
- **DECISION REQUIRED** trước khi mở ingress hoặc dùng dữ liệu thật. Chủ dự án cần phê duyệt phương án xử lý runtime/lifecycle và phạm vi hồi quy trên hosting đích. Phiên bản 8.0.34 sửa các advisory đã dẫn trong dòng 8, nhưng dòng đó đã hết hỗ trợ. Mọi cài đặt hay đổi framework/runtime/dependency đứng ngoài lượt kiểm chứng này. [Release notes 8.0.34](https://github.com/dotnet/core/blob/main/release-notes/8.0/8.0.34/8.0.34.md) · [Triển khai cùng chủ dự án](../deployment/guided-deployment-plan.md)
