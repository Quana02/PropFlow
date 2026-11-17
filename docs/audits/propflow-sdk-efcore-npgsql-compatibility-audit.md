# PropFlow — Kiểm toán SDK, EF Core và Npgsql

Ngày đối chiếu nguồn và môi trường **16/11/2026**. Phạm vi là kiểm kê chỉ đọc và so sánh các bản vá trong nền tảng `net8.0` hiện hữu.

## A. Tóm tắt

- **SDK được chọn là 8.0.314** với MSBuild 17.10.29. Cả 34 project kiểm kê nhắm `net8.0`. Runtime .NET và ASP.NET Core 8 cài trên máy gồm 8.0.17 và 8.0.33. Bản API/Web Release là framework-dependent, mặc định được kỳ vọng chọn 8.0.33 trên máy này.
- **Đồ thị hiện tại nhất quán** với EF Core, Relational, Design và InMemory 8.0.11 ở nơi sử dụng. Npgsql EF provider là 8.0.11 và Npgsql ADO.NET driver là 8.0.6. Không thấy xung đột về phiên bản trong các đồ thị đã lưu.
- **KEEP CURRENT cho baseline phát triển cục bộ. BLOCKED cho production trên runtime hiện tại.** ASP.NET Core 8.0.33 thuộc phạm vi advisory SignalR Critical CVE-2026-114146. Cả hai phương án package dưới đây giữ nguyên runtime nên rủi ro đó vẫn còn. .NET 8 và EF Core 8 đã hết hỗ trợ từ 10/11/2026. [Advisory SignalR của Microsoft](https://github.com/dotnet/aspnetcore/security/advisories/GHSA-67wp-ghc2-fw2g) · [Vòng đời .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) · [Vòng đời EF Core](https://learn.microsoft.com/en-us/ef/core/what-is-new/)

## B. SDK và công cụ

| Hạng mục | Quan sát trực tiếp |
| --- | --- |
| Hệ điều hành, kiến trúc và đường dẫn | Windows build 10.0.26200, RID `win-x64`, `C:\Program Files\dotnet\dotnet.exe`. Host báo 9.0.22 x64, có đăng ký cài đặt x86 riêng. |
| SDK cài đặt x64 | 8.0.314, 9.0.203, 9.0.320. |
| SDK tại root PropFlow và thư mục API | 8.0.314, commit SDK `30906d2596`, MSBuild 17.10.29.22308. |
| SDK tại ba thư mục cha | Cả ba chọn 9.0.320. Kiểm tra đến root ổ E không thấy `global.json` ở thư mục cha. |
| Cấu hình repository | `global.json` yêu cầu 8.0.314, `rollForward: latestFeature`, `allowPrerelease: false`. `Directory.Build.props` đặt `net8.0`, C# 12, nullable, implicit usings và deterministic. |
| Visual Studio | VS 17.14.37827.9 qua `vswhere`. MSBuild nêu trên là của CLI. Chưa kiểm SDK thực sự được IDE load trong một build riêng. |
| Workload | Manifest `8.0.300-manifests.ee7a832d`. Đã cài `maui-windows`, `maccatalyst`, `ios`, `android` qua VS. Không thấy `wasm-tools` trong danh sách workload của SDK này. |
| Công cụ local | Manifest `.config/dotnet-tools.json` đặt `dotnet-ef` 8.0.11 và `rollForward: false`. `dotnet tool run dotnet-ef --version` xác nhận 8.0.11. |
| Công cụ global | `dotnet-ef` 8.0.2, `dotnet-aspnet-codegenerator` 8.0.23, `microsoft.dotnet-scaffold` 20.0.0. Công cụ EF global lệch patch với runtime EF, nên lệnh tooling đã ghi nhận rõ local tool. |

`latestFeature` chọn SDK ổn định cao nhất trong dòng 8.0 đủ mới. Máy clone có SDK 8.0 feature band hoặc patch cao hơn có thể dùng SDK khác. Máy chỉ cài 9.x sẽ không thỏa điều kiện khi chạy từ repo. CLI dò `global.json` từ thư mục làm việc, như chênh lệch đã quan sát khi chạy trên thư mục cha. Khi cần tái lập Release, ghi `dotnet --version` tại repo root và đối chiếu package graph. SDK 8.0.314 hỗ trợ `net8.0`; Release đã có xác nhận khả năng build và publish của baseline. [SDK resolver của Microsoft](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json) · [Phiên bản SDK và runtime](https://learn.microsoft.com/en-us/dotnet/core/versions/selection) · [Cấu hình SDK](../../global.json) · [Manifest công cụ](../../.config/dotnet-tools.json)

## C. Framework, runtime và publish

| Phạm vi | Phiên bản hoặc yêu cầu |
| --- | --- |
| `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`, `Microsoft.WindowsDesktop.App` cài x64 | Mỗi loại có 8.0.17, 8.0.33, 9.0.4, 9.0.22. WindowsDesktop nằm trong inventory máy. Các host PropFlow yêu cầu hai shared framework đầu. |
| API và Web host | `Microsoft.NET.Sdk.Web`, `net8.0`. Hai Release `.runtimeconfig.json` ghi `Microsoft.NETCore.App` và `Microsoft.AspNetCore.App` cùng mốc 8.0.0, không đặt `rollForward`. `.deps.json` báo `.NETCoreApp,Version=v8.0` không kèm RID. |
| Chế độ triển khai | Framework-dependent theo runtimeconfig và `web.config` gọi `dotnet` với DLL, hosting model IIS in-process nếu dùng IIS. Apphost `.exe` Windows hiện hữu. Không có `coreclr.dll` đóng gói tại root của hai artifact. Host đích cần shared framework và apphost đúng kiến trúc nếu chọn cách chạy executable. |
| Blazor client | `Microsoft.NET.Sdk.BlazorWebAssembly`, `net8.0/browser-wasm` trong assets. `Components.WebAssembly` và `Components.Authorization` 8.0.27, `SignalR.Client` 8.0.32, Web host dùng `Components.WebAssembly.Server` 8.0.32. Assets Release yêu cầu pack Mono browser-wasm **8.0.17** và bản publish có `blazor.boot.json` cùng `dotnet.native.wasm`. |
| Project chuẩn | 31 project dùng `Microsoft.NET.Sdk`: 14 module persistence, Reporting, 10 project Contracts, Shared và năm project test/fixture. Tất cả dùng `net8.0`. Tổng 34 khi tính API, Web và Client. |
| Solution và hạ tầng build | `PropFlow.sln` chứa 32 project. BillingCycleTests và BrowserFixture chạy riêng. Có solution Web lồng trong `src/PropFlow.Web`. Kiểm kê file dự án không thấy CPM, `Directory.Build.targets`, `NuGet.Config` tại repo, lockfile, publish profile, Dockerfile hoặc workflow CI/CD được cung cấp. |

8.0.0 là mốc yêu cầu trong artifact. Với policy mặc định và các framework hiện cài, host được kỳ vọng sử dụng patch **8.0.33** cho runtime .NET và ASP.NET Core. Audit chưa khởi chạy ứng dụng để đo assembly đã load hoặc kiểm override tại host đích. Runtime browser-wasm được đóng gói cho trình duyệt và có phiên bản riêng với shared runtime trên máy chủ. Quyết định phát hành phải đối chiếu cả host và WASM artifacts. [Chọn runtime và roll-forward](https://learn.microsoft.com/en-us/dotnet/core/versions/selection) · [Quy tắc frontend](../agent/rules/PropFlow_Frontend_Rules.md) · [Runbook Release](../deployment/authorization-hardening-runbook.md)

## D. Inventory EF Core và toàn vẹn package graph

Đọc 132 bản `project.assets.json` hiện hữu của 34 project trong cây project và các bộ artifact Hardening. Nhóm có EF gồm 20 project. Đối chiếu các manifest với `.deps.json` của API/Web Release và metadata NuGet. Phiên bản được giải quyết nhất quán trong phạm vi này.

| Package hoặc tooling | Khai báo | Giải quyết | Project dùng và quan hệ |
| --- | --- | --- | --- |
| `Microsoft.EntityFrameworkCore` | Không có PackageReference trực tiếp | 8.0.11 | Gián tiếp qua provider và các gói EF, trong API, 14 module persistence, bốn test và fixture. |
| `Microsoft.EntityFrameworkCore.Abstractions` | Gián tiếp | 8.0.11 | Cùng 20 project qua Core hoặc provider. |
| `Microsoft.EntityFrameworkCore.Relational` | Gián tiếp | 8.0.11 | Cùng 20 project qua provider hoặc Design. |
| `Microsoft.EntityFrameworkCore.Analyzers` | Gián tiếp | 8.0.11 | Cùng 20 đồ thị, analyzer đi theo Core. |
| `Microsoft.EntityFrameworkCore.Design` | Trực tiếp 8.0.11 ở chín project | 8.0.11 | API và Administration, Apartments, Billing, Communication, Complaints, Maintenance, PropertyAssets, ServiceRequests. Có trong assets Unit, Architecture, Integration qua project references. API đánh dấu `PrivateAssets=all` và bản API Release vẫn liệt kê package trong `.deps.json`. |
| `Microsoft.EntityFrameworkCore.InMemory` | Trực tiếp 8.0.11 ở Unit và Integration | 8.0.11 | Test-only. Các ca PostgreSQL cần đối chiếu provider thật. |
| `Microsoft.EntityFrameworkCore.Tools` | Không tìm thấy | Không tìm thấy | Lệnh CLI dùng `dotnet-ef` từ manifest. |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Trực tiếp 8.0.11 ở 16 project | 8.0.11 | API, toàn bộ 14 module persistence ở mục J và ArchitectureTests. Gián tiếp ở Unit, Integration, BillingCycle và BrowserFixture. |
| `Npgsql` | Gián tiếp | 8.0.6 | Cùng 20 project qua provider. Mã dùng trực tiếp API ADO.NET vẫn nhận assembly qua dependency gián tiếp. |

Web, Web.Client, Reporting, Shared và 10 project Contracts có đồ thị không chứa EF/Npgsql. Không thấy `VersionOverride`, `NU1605`, `NU1608` hoặc một nhánh EF lệch patch trong assets đã đọc. Một số assets lưu **NU1900** do lần restore cũ không đọc được chỉ mục nguồn advisory NuGet. Warning này ảnh hưởng độ đầy đủ dữ liệu advisory của lần restore đó. Hardening 1.2 có quét gói riêng ở thời điểm của lượt đó. Audit này đối chiếu advisory chính thức theo phạm vi, không tạo đồ thị restore hoặc quét lại toàn bộ transitive dependency. [Metadata EF Core 8.0.11](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/8.0.11) · [NuGet audit và NU1900](https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1900) · [Baseline Hardening 1.2](propflow-authorization-hardening-1.2-report.md)

## E. Npgsql, provider và PostgreSQL

- `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11 phát hành 18/11/2024, target `net8.0`. Metadata có giới hạn dưới **EF Core, Abstractions và Relational >= 8.0.11**, cùng **Npgsql >= 8.0.6**, không ghi trần tại các dependency này. Giới hạn dưới là quy tắc resolve NuGet, không phải cam kết tương thích giữa các major. Đây là patch 8.x cao nhất tìm được trong lịch sử package tại ngày kiểm. [Provider 8.0.11](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/8.0.11#dependencies-body-tab)
- Npgsql 8.0.6 có asset `net8.0` và phụ thuộc `Microsoft.Extensions.Logging.Abstractions >= 8.0.0` trong group này. Dòng `net8.0` của driver 8.0.9 ghi cùng mức. Lệch số patch giữa các lớp là cách đóng gói chính thức. [Driver 8.0.6](https://www.nuget.org/packages/Npgsql/8.0.6#dependencies-body-tab) · [Driver 8.0.9](https://www.nuget.org/packages/Npgsql/8.0.9#dependencies-body-tab)
- API, Administration, AiChatbot, Apartments, Authentication, Maintenance, Payments, PropertyAssets, Residents, Unit, Integration, BillingCycle và fixture có tham chiếu namespace/type Npgsql trong mã ngoài migrations. Kiểm kê không thấy package NodaTime, NetTopologySuite, Json.NET plugin cho Npgsql hoặc pgvector. Enum trạng thái dùng conversion sang chuỗi. JSONB dùng mapping hiện có. Embedding của AiChatbot là `real[]` và truy vấn PostgreSQL có tham số kiểu mảng. Không thấy đăng ký `EnableDynamicJson` hay `MapEnum` trong production source. [Nguồn query embedding](../../src/Modules/AiChatbot/Infrastructure/ResidentKnowledgeSearch.cs) · [Compatibility notes](https://www.npgsql.org/doc/compatibility.html) · [Release notes provider 8.0](https://www.npgsql.org/efcore/release-notes/8.0.html)
- Windows báo service `postgresql-x64-18` đang chạy từ `E:\PostgreSQL\18\bin\pg_ctl.exe`. Metadata `postgres.exe`, `postgres --version` và `psql --version` đều cho **18.4**. Đây là bằng chứng binary cục bộ. Phiên bản backend của connection string thực tế ở môi trường đích **NOT VERIFIED** vì audit không kết nối PostgreSQL. Hardening 1.2 đã chạy PostgreSQL local 18.4 với fixture.
- Provider 8 mặc định sinh SQL theo compatibility mode PostgreSQL 14 và source không đặt `SetPostgresVersion`. Deployment phải bảo đảm DB hỗ trợ cú pháp thực dùng và extension `btree_gist` của Residents. Không suy ra toàn bộ PropFlow đã chạy đạt trên mọi phiên bản PostgreSQL từ default 14. Nhánh PostgreSQL 18 còn hỗ trợ, patch hiện tại của nhánh là 18.7 ngày 12/11/2026. Release notes nêu các sửa lỗi bảo mật, điều kiện `REINDEX` và lưu ý backup gia tăng sau cập nhật. Đánh giá bản vá máy chủ là công việc riêng, có xét cấu hình thực tế. [Default compatibility](https://www.npgsql.org/efcore/release-notes/8.0.html) · [Vòng đời PostgreSQL](https://www.postgresql.org/support/versioning/) · [Release PostgreSQL 18.7](https://www.postgresql.org/docs/release/18.7/)

## F. Đánh giá bảo mật và hỗ trợ

| Phát hiện | Đánh giá áp dụng |
| --- | --- |
| CVE-2026-114146, Critical 9.0 | Microsoft mô tả SignalR có thể xử lý invocation dưới danh tính khác do liên kết identity lúc negotiate không nhất quán. ASP.NET Core 8 đến 8.0.33 thuộc dải ảnh hưởng, vá ở 8.0.34. API có bảy đường dẫn hub, hai transport đã được diễn tập. Kết luận phạm vi dựa trên thành phần sử dụng và version, không thực hiện khai thác. [Advisory](https://github.com/dotnet/aspnetcore/security/advisories/GHSA-67wp-ghc2-fw2g) |
| CVE-2026-114149, High | Advisory giới hạn header của multipart có thể bị vượt, gây tiêu thụ tài nguyên. Bản sửa nhánh 8 là 8.0.34. Có surface liên quan vì PropFlow nhận multipart ảnh minh chứng. [Advisory](https://github.com/dotnet/aspnetcore/security/advisories/GHSA-x33c-w48q-qrgc) |
| CVE-2026-114157, Kestrel Named Pipes | Windows và 8.0.33 thuộc dải công bố. Không thấy `UseNamedPipes` trong source đã đọc. Khả năng áp dụng phụ thuộc hosting configuration bên ngoài mã. [Advisory](https://github.com/dotnet/aspnetcore/security/advisories/GHSA-7phj-vgxq-r67h) |
| GHSA-x9vc-6hfv-hg8c, Npgsql SQL injection | Nhánh 8 đã vá ở 8.0.3. Driver 8.0.6 hiện tại và 8.0.9 candidate nằm ngoài dải ảnh hưởng này. [Advisory Npgsql](https://github.com/npgsql/npgsql/security/advisories/GHSA-x9vc-6hfv-hg8c) |
| EF Core và provider | Không tìm thấy advisory do maintainer công bố trực tiếp trên hai trang advisory đã tra. Phạm vi hẹp này không chứng minh toàn bộ dependency graph an toàn. [Advisories EF Core](https://github.com/dotnet/efcore/security/advisories) · [Advisories provider](https://github.com/npgsql/efcore.pg/security/advisories) |
| Lifecycle | .NET 8, ASP.NET Core 8 và EF Core 8 hết hỗ trợ ngày 10/11/2026. .NET 8.0.34 là bản vá cuối được Microsoft liệt kê. Patch EF không vá shared runtime hoặc Mono WASM. Proxy và kiểm soát ingress có thể giảm phơi nhiễm trong thử nghiệm, bản vá của nhà phát hành vẫn cần được đánh giá riêng. [Chính sách .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) · [Thông báo 8.0.34](https://github.com/dotnet/core/blob/main/release-notes/8.0/8.0.34/8.0.34.md) |

## G–H. So sánh các tổ hợp

| Tiêu chí | A · Baseline | B · Thay driver cùng major | C · Đồng bộ patch EF Core và driver |
| --- | --- | --- | --- |
| EF Core, Abstractions, Analyzers, Relational | 8.0.11 | 8.0.11 | 8.0.34 |
| EF Design, InMemory, dotnet-ef local | 8.0.11 | 8.0.11 | 8.0.34 |
| Npgsql EF Provider | 8.0.11 | 8.0.11 | 8.0.11 |
| Npgsql Driver | 8.0.6 | 8.0.9 | 8.0.9 |
| Metadata trên `net8.0` và dependency constraints | Thỏa và quan sát đã resolve | Thỏa. Driver 8.0.9 >= 8.0.6 | Thỏa. Core/Abstractions/Relational 8.0.34 >= 8.0.11, driver 8.0.9 >= 8.0.6 và Design/InMemory thuộc cùng patch EF. |
| SDK, shared runtime và PostgreSQL | SDK 8.0.314 và runtime 8 hiện hữu. PG 18.4 có rehearsal | Metadata không đòi đổi SDK, shared runtime hoặc PostgreSQL. Chưa kiểm chạy trên tổ hợp hiện hữu. | Metadata không đòi đổi SDK, shared runtime hoặc PostgreSQL. Chưa kiểm chạy trên tổ hợp hiện hữu. |
| Lợi ích | Có bản publish, test, di trú và restore baseline | Phạm vi thay đổi nhỏ nhất. Nhận các bản sửa TLS/SCRAM, đọc bất đồng bộ, xử lý ngắt kết nối và ánh xạ nullable. Chưa tái hiện lỗi tương ứng trên baseline. | Nhận servicing của EF 8 đến patch cuối. EF và tooling cùng patch. Chưa xác định lỗi EF 8.0.11 đang xảy ra trong PropFlow mà phương án này giải quyết. |
| Breakage và regression cần kiểm | Đồ thị ổn định theo artifacts | Không thấy thay đổi API bắt buộc trong các mục đã tra. Trọng tâm là pool, TLS, typed parameters, cancellation và lỗi trong transaction. | Có thể khác SQL translation, materialization, metadata mô hình, migration SQL và tooling. Không thấy yêu cầu đổi chữ ký ứng dụng trong metadata/release đã tra. |
| Migration và deploy | Runner 14 context và chuyển trạng thái auth legacy đã có bằng chứng baseline | Chỉ đổi driver không tạo migration theo thiết kế. Vẫn cần so model và SQL. Phải publish lại API/runner và chạy DB smoke. | Không dự kiến đổi schema chỉ vì patch. Kiểm pending model và SQL cho 14 context trên cả fresh/upgrade, rồi publish lại API/runner. |
| Rủi ro và mức xác minh | Package thấp trong baseline local, shared runtime rủi ro cao nếu mở production. Build/runtime có bằng chứng lịch sử. | Rủi ro thay đổi thấp hơn C. Metadata đã kiểm, ảnh hưởng source đã rà soát. Build và runtime **NOT VERIFIED**. | Rủi ro regression lớn hơn B. Metadata đã kiểm, ảnh hưởng source đã rà soát. Build và runtime **NOT VERIFIED**. |

Đối chiếu tag Npgsql cho thấy 8.0.7 thêm TLS close notification và siết xử lý SCRAM-SHA-256. 8.0.8 sửa lỗi đọc async và ngắt kết nối. 8.0.9 sửa SSL connection reset trên Windows, một số converter nullable và tránh đưa mật khẩu vào tên data source khi bật `Persist Security Info`. Đây là lý do kiểm TLS, cancel/dispose và tham số nullable trước khi chọn B. Baseline chưa được kiểm về điều kiện cấu hình `Persist Security Info`. [8.0.6 → 8.0.7](https://github.com/npgsql/npgsql/compare/v8.0.6...v8.0.7) · [8.0.7 → 8.0.8](https://github.com/npgsql/npgsql/compare/v8.0.7...v8.0.8) · [8.0.8 → 8.0.9](https://github.com/npgsql/npgsql/compare/v8.0.8...v8.0.9)

Npgsql 8.0.9 phát hành 12/03/2026 và EF Core 8.0.34 phát hành 10/11/2026. Release EF 8.0.34 liệt kê các thay đổi servicing của repository, trong đó có cập nhật pipeline và dependency gRPC. Danh sách này chưa chứng minh SQL PostgreSQL giữ nguyên trên toàn dải từ 8.0.11. Metadata của EF 8.0.34 vẫn dùng các giới hạn dưới 8.0.1 cho Caching.Memory/Logging và 8.0.0 cho Configuration.Abstractions của Relational. Design yêu cầu Relational từ 8.0.34, InMemory yêu cầu Core từ 8.0.34. Tương thích được suy ra theo metadata, release notes đọc được và phạm vi review source. Dòng major khác nằm ngoài lựa chọn. [Release EF Core](https://github.com/dotnet/efcore/releases/tag/v8.0.34) · [Core](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/8.0.34) · [Relational](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Relational/8.0.34) · [Design](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Design/8.0.34) · [InMemory](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.InMemory/8.0.34) · [Lịch sử dotnet-ef](https://www.nuget.org/packages/dotnet-ef#versions-body-tab)

## I. Ảnh hưởng đến mã nguồn và hành vi

Rà soát `UseNpgsql`, options, entity configuration, `Include` và projection, `AsNoTracking`, phân trang, raw/interpolated SQL, bulk update/delete, `SaveChanges`, concurrency tokens và interceptors. Context được cấp theo scope. Một số context chia sẻ `NpgsqlConnection` trong scope, một số nhận connection string từ cấu hình. Không thấy đăng ký `AddDbContextPool`, `EnableRetryOnFailure`, `SetPostgresVersion` hoặc global query filters trong production source đã quét. Pool driver và timeout thực tế phụ thuộc thiết lập runtime. Audit không đọc connection string hoặc secret. [Composition API](../../src/PropFlow.Api/Program.cs) · [Backend rules](../agent/rules/PropFlow_Backend_Rules.md) · [Ownership](../database/PropFlow_Database_Ownership_Mapping.md)

| Vùng và điểm vào | Dấu hiệu rủi ro nếu đổi B/C | Đối chứng cần chạy khi được duyệt |
| --- | --- | --- |
| FE-01/15, `AuthStore`, `AccessTokenStateValidator`, `AdministrationTransaction` | `hashtextextended`, `pg_advisory_xact_lock`, `FOR UPDATE`, nullable UUID và fingerprint qua `Contains` theo batch, transaction chung qua `UseTransactionAsync`. B nhạy ở lock/pool/dispose. C thêm rủi ro dịch SQL và mapping null. | Refresh cạnh tranh, logout, disable/role change, lịch sử và phiên cũ sau restart. So SQL batch theo index và chứng minh audit cùng transaction. [AuthStore](../../src/Modules/Authentication/Infrastructure/Persistence/AuthStore.cs) · [Session validator](../../src/Modules/Authentication/Infrastructure/AccessTokenStateValidator.cs) · [Administration transaction](../../src/PropFlow.Api/Composition/AdministrationTransaction.cs) |
| FE-07, `MaintenanceService`, `MaintenanceTaskLock` | Query lịch sử lọc assignment và thời gian trước projection, phân trang, result/attachment, advisory lock khi phân công và gửi ảnh. B nhạy ở lock/dispose. C nhạy ở `Any`, sort và projection. | Task đang làm/đã xong, quyền ảnh đúng đợt A → B → A, trang lọc, race reassignment/upload và rollback storage. [Maintenance service](../../src/Modules/Maintenance/Application/MaintenanceService.cs) · [Task lock](../../src/Modules/Maintenance/Infrastructure/Persistence/MaintenanceTaskLock.cs) |
| FE-08/09, `BillingCycle`, `PaymentFinanceStore`, `SePayInbox` | Tính tiền `decimal`, VND `numeric(18,0)`, quantity `numeric(18,4)`, meter `numeric(18,3)`, ngày kỳ phí, raw SQL và khóa theo hóa đơn. Trùng webhook xử lý qua SQLSTATE unique violation, có outbox và thông báo. | So doanh thu/nợ và rounding theo từng dòng, hai xác nhận cùng hóa đơn, duplicate/replay, correction và settlement. Dùng stub và DB tổng hợp. [BillingCycle](../../src/Modules/Billing/Infrastructure/Finance/BillingCycle.cs) · [Payment store](../../src/Modules/Payments/Infrastructure/Finance/PaymentFinanceStore.cs) · [SePay inbox](../../src/Modules/Payments/Infrastructure/ResidentPayments/SePayInbox.cs) |
| SignalR, background workers và audit | Batch outbound dựa trên nhiều query xác thực. Worker dùng scope riêng, advisory lock, `FOR UPDATE SKIP LOCKED`, retry và cancellation. Interceptor viết audit cùng transaction, một số DbContext còn capture outbox tại `SaveChanges`. | Kiểm fan-out quanh 256, session thay đổi khi dispatch, DB timeout/cancel, duplicate outbox, số kết nối chờ, cleanup và throughput. [Hub lifetime](../../src/PropFlow.Api/Composition/SessionHubLifetimeManager.cs) · [Finance worker](../../src/Modules/Communication/Infrastructure/FinanceEmailWorker.cs) · [Audit interceptor](../../src/PropFlow.Api/Composition/Auditing/SystemAuditInterceptor.cs) |
| AiChatbot, Residents và composition liên module | `NpgsqlCommand` đọc `real[]`, raw SQL `unnest`, full-text search, session advisory lock khi index, sequence mã cư dân, transaction Residents/Apartments dùng chung connection. Các điểm bắt `PostgresException` phụ thuộc SQLSTATE và tên constraint. | Typed arrays/null, timestamp UTC, DateOnly, unique/FK errors, release lock sau cancel và rollback cả hai context. [Knowledge search](../../src/Modules/AiChatbot/Infrastructure/ResidentKnowledgeSearch.cs) · [Knowledge worker](../../src/PropFlow.Api/Composition/KnowledgeIndexWorker.cs) · [Transaction coordinator](../../src/PropFlow.Api/Composition/AtomicTransactionCoordinator.cs) |

Mapping đã thấy gồm `uuid`/Guid, `timestamptz` cho UTC instant, `date` cho ngày nghiệp vụ, số decimal có precision, enum lưu text/varchar, JSONB cho audit/AI và outbox có mapping theo entity, cùng `real[]` cho embeddings. Có default/identity annotations, sequence mã cư dân, `gen_random_uuid()`, partial index và ràng buộc FK liên schema. Khi so SQL, giữ nguyên timezone nghiệp vụ, kiểu tham số, precision, phạm vi và thứ tự phân trang. Tác động đến query plan cần đo trên bản sao PostgreSQL có dữ liệu đại diện. [DBML](../database/PropFlow.dbml) · [Backend rules](../agent/rules/PropFlow_Backend_Rules.md)

## J. Database và migration

14 context có **92 migration** theo source. Mỗi dòng có `<Context>ModelSnapshot.cs` trong thư mục `src/Modules/<Module>/Infrastructure/Persistence/Migrations`, assembly mặc định của module. Cả 14 snapshot ghi `ProductVersion=8.0.11` và annotation identity-by-default của Npgsql. Mỗi context đăng ký `UseNpgsql` với `__EFMigrationsHistory` riêng trong schema bên dưới và tham chiếu provider 8.0.11 từ project sở hữu. Configuration nằm cùng khu vực Persistence, gồm `OnModelCreating`, cấu hình entity riêng hoặc inline. Cột kết nối chỉ cách API đăng ký DI. [Composition API](../../src/PropFlow.Api/Program.cs) · [Module ownership](../database/PropFlow_Database_Ownership_Mapping.md)

| Module và DbContext | Schema, kiểu kết nối | Migration cuối trên source | Mối quan tâm khi kiểm B/C |
| --- | --- | --- | --- |
| Authentication · `AuthenticationDbContext` | `auth`, scoped connection | 9 · `20261115110000_BindAccessTokensToSessions` | SID, fingerprint, partial index, thu hồi legacy và liên kết Residents/Apartments. |
| Residents · `ResidentsDbContext` | `residents`, scoped connection | 7 · `20261003103000_EnforceResidentCanonicalUniqueness` | Sequence, canonical uniqueness, exclusion bằng GiST và lịch sử cư trú. |
| Apartments · `ApartmentsDbContext` | `apartments`, scoped connection | 7 · `20261102160128_AddApartmentUnitTypeDefaults` | Chủ sở hữu hiện hành, loại căn hộ, precision diện tích và row lock. |
| PropertyAssets · `PropertyAssetsDbContext` | `property_assets`, scoped connection | 8 · `20261016033156_AddResidentAssetStatusOutbox` | Building singleton, mã tài sản, lịch và outbox. |
| Administration · `AdministrationDbContext` | `administration`, scoped connection | 6 · `20261109154621_GrantManagerResidentAccountPermissions` | Role grant, JSONB audit và giao dịch chung với auth. |
| ServiceRequests · `ServiceRequestsDbContext` | `service_requests`, connection string | 8 · `20261101095403_RequireResidentConsentForAdditionalCosts` | Consent đồng thời, amount, file metadata, partial index và outbox. |
| Maintenance · `MaintenanceDbContext` | `maintenance`, scoped connection | 7 · `20261111160000_AddEmergencyMaintenanceFlag` | Khoảng tham gia, lịch sử trạng thái, result/attachment và lock. |
| Complaints · `ComplaintsDbContext` | `complaints`, scoped connection | 2 · `20260921174433_RemoveBuildingScopeFromComplaints` | Concurrency token và đóng khiếu nại cùng thông báo. |
| Billing · `BillingDbContext` | `billing`, connection string | 12 · `20261110105334_AddUtilityMeterReadings` | Invoice snapshot, numeric precision, uniqueness, số điện/nước và thông báo. |
| Payments · `PaymentsDbContext` | `payments`, connection string | 11 · `20261030113943_AddSePaySandboxWebhook` | Idempotency, inbox, phân bổ tiền, khóa và uniqueness. |
| AiClassification · `AiClassificationDbContext` | `ai_classification`, connection string | 2 · `20260915030811_CorrectAiClassificationDomainConsistency` | Enum chuỗi, kết quả và review gắn giao dịch tác vụ. |
| AiRecommendation · `AiRecommendationDbContext` | `ai_recommendation`, connection string | 1 · `20260915040506_InitialAiRecommendation` | Payload JSONB, review, transaction với ServiceRequests. |
| AiChatbot · `AiChatbotDbContext` | `ai_chatbot`, connection string | 2 · `20261105074813_AddResidentChatKnowledgeChunks` | `real[]`, full-text, concurrency token và lock khi index. |
| Communication · `CommunicationDbContext` | `communication`, scoped connection | 10 · `20261112082535_AddEmergencyVoiceCalls` | Queue mail, chat uniqueness, paging và sự kiện cuộc gọi. |

Production DI thêm `SystemAuditInterceptor` cho 13 context, AiChatbot cấu hình riêng. Có 11 design-time factory theo inventory, gồm AiChatbot đặt trong composition API. Residents, AiClassification và AiRecommendation không có dedicated factory trong scope đã quét, runner mở trực tiếp các context của chúng. Reporting dùng read model và nguồn liên module. Runner áp prerequisite Auth đến `20260914101250_CorrectAuthenticationEmailOtpVerification`, rồi PropertyAssets, Apartments, prerequisite Residents đến `20260914063807_InitialResidents`, Administration, hoàn tất Auth/Residents rồi các module còn lại. Thứ tự xử lý FK giữa auth, residents và apartments. [Runner](../../scripts/authz/fixture/DatabaseMigration.cs) · [Tài liệu migration](../deployment/database-migration-and-recovery.md)

`20261115110000_BindAccessTokensToSessions` thêm `session_id`, `access_fingerprint`, partial index và thu hồi refresh token cũ. Việc giảm phiên bản schema có quy tắc thu hồi riêng. Không đưa thay đổi này vào kế hoạch rollback package đơn thuần. Nếu diff mô hình dưới candidate cho thấy SQL/DDL mới, đánh giá riêng trước khi tạo migration. Restore snapshot cũ có thể khôi phục trạng thái phiên trước thời điểm thu hồi, cần quy trình trong runbook trước lúc cấp traffic. [Migration auth](../../src/Modules/Authentication/Infrastructure/Persistence/Migrations/20261115110000_BindAccessTokensToSessions.cs) · [Runbook](../deployment/authorization-hardening-runbook.md)

## K. Tác động deployment

Bản API/Web hiện tại publish được với SDK đã chọn. Môi trường chạy cần shared .NET/ASP.NET Core 8 đúng kiến trúc, PostgreSQL và khả năng kết nối tới database theo runbook. Nếu dùng IIS, môi trường cần thành phần hosting phù hợp với `web.config`. Proxy cùng origin, TLS, WebSocket, Long Polling, key ring và runtime secrets thuộc baseline vận hành. Review host/version riêng trước phát hành. [Tài liệu cấu hình](../deployment/deployment-configuration-reference.md) · [Release readiness](release-readiness.md) · [Runbook triển khai](../deployment/authorization-hardening-runbook.md)

B/C theo metadata không buộc đổi mode, SDK, framework, PostgreSQL server hoặc connection settings. Nếu sau này áp dụng, phát hành lại toàn bộ API và migration runner từ đồ thị đã kiểm, kèm artifact Web tương ứng với cùng release và smoke cả hai host. Kiểm driver TLS/SSL, timeouts, pooling, DNS PostgreSQL, quyền tạo extension và shared transaction tại hạ tầng đích. Đưa kiểm tra version resolver vào build nếu đã có pipeline được duyệt. Cả hai candidate giữ nguyên blocker bảo mật ASP.NET Core runtime.

## L. Đề xuất và kế hoạch sau khi được duyệt

**KEEP CURRENT** cho môi trường local theo quyết định giữ .NET 8. **BLOCKED** cho phát hành public/production dùng ASP.NET Core 8.0.33. Một bản vá database package không xử lý CVE SignalR hay kéo dài support. Nếu phát sinh nhu cầu cụ thể về driver, ưu tiên **RECOMMEND TESTING** Option B trong bản sao cô lập. Option C chỉ đáng thử nếu cần bản sửa EF cụ thể hoặc muốn đánh giá servicing patch cuối với ngân sách hồi quy đầy đủ.

| Phương án kiểm chứng sau này | Phạm vi thay đổi phiên bản cần phê duyệt | Kiểm tra riêng |
| --- | --- | --- |
| B | `Npgsql` 8.0.6 gián tiếp → 8.0.9 trong mọi đồ thị DB. Nếu chọn reference tường minh để ép resolve, rà 14 module, API, test có graph độc lập và runner. Provider/EF/CLI giữ 8.0.11. | So assets/deps toàn bộ để tránh graph dev khác test/runner. Thử TLS, connection state/pool, cancellation, số tiền, rollback và 14 context. |
| C | B cộng Core/Abstractions/Analyzers/Relational 8.0.11 → 8.0.34, Design ở chín project và InMemory ở hai test project → 8.0.34, local `dotnet-ef` 8.0.11 → 8.0.34. Provider vẫn 8.0.11. Cần chỉ định EF runtime dependency phù hợp trên đồ thị độc lập để tránh module restore về minimum 8.0.11. | So dependency graph, generated query, design-time model, pending model khác với pending migrations, schema snapshot và generated SQL. Bổ sung cross-module audit/transaction và tests PostgreSQL thật. |

Sau phê duyệt riêng cho candidate, dùng bản sao source và database tổng hợp. Lưu manifest phiên bản và artifact A để quay lui. Restore và kiểm warnings trong môi trường cô lập, build solution và publish Release. Chạy 940 Unit/component, 64 Architecture, 36 JavaScript, 561 Integration, 150 BillingCycle và ma trận Chrome 378 kiểm tra route theo baseline. Đối chiếu fresh migration, upgrade từ baseline, pending/model diff cho 14 context và backup/restore. Chạy hồi quy session, lịch sử FE-07, decimal/transaction tài chính, SignalR và worker. Chạy proxy HTTPS, hai transport, restart, pooling và benchmark cùng workload với A. Nếu candidate lỗi và schema giữ nguyên, phục hồi package graph/API/runner từ artifact A và kiểm smoke lại. Nếu một đợt thay đổi schema được duyệt riêng, dùng kế hoạch DB phục hồi đã diễn tập cùng quy tắc bảo vệ phiên. [Báo cáo baseline](propflow-authorization-hardening-1.2-report.md) · [Phục hồi DB](../deployment/database-migration-and-recovery.md)

## M. Phạm vi bằng chứng và giới hạn

- Đọc trực tiếp SDK resolution, công cụ local/global đã cài, source dự án, 132 assets, manifests Release, cấu hình host từ artifact, code dữ liệu và migrations, binary/dịch vụ PostgreSQL cùng tài liệu ownership, backend/frontend và deployment.
- Kiểm chứng chính sách hỗ trợ, metadata version/dependency và các advisory nêu trên từ Microsoft, NuGet, PostgreSQL và maintainer Npgsql tại ngày 16/11/2026. Kiểm kê advisory theo phạm vi liên quan, không thay thế một lần quét cả đồ thị mới.
- Baseline Hardening 1.1/1.2 là bằng chứng lịch sử, gồm publish, reverse proxy, 14 context migrate/restore và kết quả hồi quy đã lưu. Candidate B/C được đánh giá ở mức metadata và source. Chưa restore candidate, build/publish candidate, đo runtime đã load hoặc kiểm SQL/migration trên candidate. PostgreSQL backend đích và hosting đích chưa được truy vấn. [Hardening 1.1](propflow-authorization-hardening-1.1-report.md) · [Hardening 1.2](propflow-authorization-hardening-1.2-report.md) · [Thiết lập local](../../README.md)

## N. Xác nhận chỉ đọc

HEAD trước audit `0859534e182c0cc288c218a6bdab9cc58de6cb79`. Baseline có 121 mục status kể cả file untracked được liệt kê riêng, gồm thay đổi Hardening có sẵn. SHA-256 của diff tracked so với HEAD là `46cf32a96a57891d874f675034211bc7512802c7a423610dbd3c3deb9a097800`. SHA-256 của danh sách và nội dung 1.464 đường dẫn tracked/untracked không bị ignore, loại báo cáo này, là `2f572da636fe33189c8da0719c926d936af152d569e8c7061e975645f0c7563f`.

| Loại thay đổi của audit | Số lượng |
| --- | ---: |
| File source | 0 |
| Framework, runtime, SDK | 0 |
| Package hoặc lockfile | 0 |
| Schema và thao tác trên database | 0 |
| Migration hoặc model snapshot | 0 |
| Cấu hình deployment | 0 |
| Tài liệu mới | 1 |

Tài liệu duy nhất được tạo là `docs/audits/propflow-sdk-efcore-npgsql-compatibility-audit.md`. Chênh lệch Git trước/sau được kiểm bằng HEAD, hash diff, hash nội dung và danh sách status. Không tạo process ứng dụng/database hoặc tiến trình nền phục vụ rehearsal.
