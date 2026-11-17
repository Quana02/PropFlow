# PropFlow Authorization Hardening 1.2

Ngày kiểm chứng 16/11/2026. Mọi phép thử deployment và dữ liệu trong báo cáo là fixture cục bộ. Worktree bắt đầu từ HEAD `0859534` với các thay đổi Hardening 1.1 đã có. API và Web publish từ cùng source ứng dụng với hồi quy. [Baseline 1.1](propflow-authorization-hardening-1.1-report.md)

SHA-256 của snapshot worktree `776275885020fe0339aede683a0d5fff7a7034d9266e062c5cef14cac1a3356e`, tính trên tên và nội dung các file chưa bị ignore trong `src`, `tests` và `scripts/authz`. SHA-256 của DLL Release API `73F6261ABC0E7EF3CDA461188E2B0BCB3C1A2E9DF8A216AD86CB414B52C133B6`, Web `8D2F0D2BB8ECC5625E743CEA49D42C80B89EDEB23480046D69ACCA0C3C1C28E3`.

## Kiểm kê và độ phủ

Khoảng thiếu ban đầu là 9/130 endpoint dùng nhóm verb POST, PUT, PATCH và DELETE đã có đối chứng. Inventory hiện vẫn có 65 Razor routes, 247 cặp HTTP method/route, 7 URL SignalR và 6 loại Hub. Mẫu số đã rà soát là 133 operation trong manifest hiện tại, gồm 130 verb ghi, trừ một QR preview local chỉ đọc, cộng bốn GET cấp thông tin hoặc có tác động cần kiểm. Đây là mẫu số làm việc. Những handler GET có thể phát sinh tác động gián tiếp cần được đối chiếu khi mở rộng inventory.

Manifest giữ role kỳ vọng riêng khỏi HTTP fixture. Alias được đếm theo route. Mỗi dòng có loại tác động, route, role, phạm vi và tên kiểm thử hoặc lý do cần đối chứng thêm. Chỉ một ca PASS khớp định danh trong TRX mới được tính. Trạng thái còn mở giữ được khi chạy lại. [Inventory](../../scripts/authz/inventory.mjs) · [Hành vi ngoại lệ](../../scripts/authz/operation-behavior.json) · [Fixture đã ánh xạ](../../scripts/authz/mutation-cases.json)

**32/133 operation có đủ đối chứng (24,06%).** Có 100 dòng PARTIALLY VERIFIED, 1 dòng NOT VERIFIED cho webhook SePay qua HTTP và 114 dòng NOT APPLICABLE. Manifest liệt kê riêng từng cặp HTTP method/route, action, guard quan sát, role kỳ vọng, số probe từ chối, fixture, test và lý do còn mở. Phần trăm dùng các dòng VERIFIED trên mẫu số 133. Các probe 401/403 với dữ liệu tối thiểu cung cấp bằng chứng một phần cho lớp chặn HTTP. [Manifest đầy đủ](../../artifacts/authz/hardening-12-mutation-coverage.json)

Tận dụng ca HTTP có sẵn của FE-01/02/03/05/08/09/10/13/15, kiểm row hoặc trạng thái trước/sau khi bị từ chối rồi dùng lại cùng payload với actor đúng quyền. FE-01 kiểm challenge email và tài khoản, FE-05 kiểm cư dân ngoài phạm vi và tác động qua stub, FE-10 kiểm phân loại được ghi đúng lúc. Ca đầu của FE-15 có số điện thoại trùng giữa các lượt theory nên positive control bị xung đột. Fixture chuyển sang cập nhật tên và giữ kiểm tra phạm vi profile. Giai đoạn này bổ sung test, script và tài liệu trên logic ứng dụng đã có của 1.1.

## Session, realtime và concurrency

- Giữ kiến trúc SID và lưu phiên PostgreSQL của 1.1. Ca cạnh tranh cho hai refresh và một logout dùng cùng cookie, với đối chứng thiết bị độc lập. Ca cuộc gọi khẩn cấp chạy hub và HTTP qua API, xác nhận signaling sau thu hồi và kiểm lịch sử còn giữ được. Kết quả từng test ghi ở phần hồi quy.
- Rehearsal qua proxy kiểm bearer cấp trước và sau refresh đều trả 401 sau logout, negotiate bị từ chối và phiên khác còn gọi được. Hai tab Chrome cùng phiên cùng về login. Trong TestServer, một cập nhật thu hồi ghi thẳng vào PostgreSQL mô phỏng mất tín hiệu invalidate, rồi thử batch outbound quanh mốc 256. Cuộc gọi khẩn cấp xác nhận trạng thái media qua SignalR để kiểm quyền signaling và lịch sử, phần truyền âm thanh qua mạng thực tế còn mở.
- FE-07 thêm ca xếp hai request thật vào hàng đợi advisory lock PostgreSQL. Manager phân công lại trước, STAFF cũ gửi kết quả kèm ảnh bị 403 và storage không được gọi. Đối chứng STAFF mới dùng cùng payload và lưu đúng một kết quả kèm ảnh.
- Hồi quy tài chính giữ ca hai giao dịch xác nhận đồng thời trên cùng hóa đơn. Test SePay local chạy inbox với dữ liệu giả để kiểm duplicate và settlement. Hồi quy resident/service request giữ ca cập nhật consent cạnh tranh và cư dân mất eligibility.

## Release, proxy và tải local

Published API/Web chạy Production trên Windows với PostgreSQL local. Proxy Node dành cho rehearsal bind loopback, cấp TLS bằng chứng chỉ chỉ dùng trong lượt thử, ghi đè forwarded headers nhận từ client và chuyển status upstream. Phép thử kiểm deep link, tệp Blazor, mã 401/403/404, cookie Secure/HttpOnly, CSRF sai và hợp lệ, negotiate, WebSocket upgrade, thu hồi và restart. Long Polling qua proxy gọi hub được với phiên hợp lệ, trả 401 cho poll sau logout. Log console đã dò mẫu bearer, query token và RSA key. Log sink và proxy của hosting đích còn phải kiểm riêng. [Smoke transport](../../artifacts/authz/hardening-12-proxy-load-1.json) · [WebSocket reverse proxy](https://nginx.org/en/docs/http/websocket.html)

| Users / sessions / sockets | Mẫu HTTP và hub | HTTP p95 | Hub p95 | Thao tác/giây | Logout đến đóng socket p95 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 10 / 10 / 10 | 100 + 100 | 76,61 ms | 25,36 ms | 451,38 | 35,18 ms |
| 25 / 25 / 25 | 250 + 250 | 154,77 ms | 44,26 ms | 552,91 | 16,04 ms |
| 50 / 50 / 50 | 500 + 500 | 275,04 ms | 108,84 ms | 538,83 | 18,75 ms |
| 100 / 100 / 100 | 1.000 + 1.000 | 611,62 ms | 178,69 ms | 628,08 | 34,15 ms |
| 200 / 200 / 200 | 2.000 + 2.000 | 1.825,94 ms | 603,92 ms | 445,41 | 20,81 ms |

Mỗi người dùng là một tài khoản STAFF tổng hợp, một phiên và một WebSocket. Workload ổn định luân phiên GET /auth/me và invocation JoinMaintenanceUpdates, 10 cặp thao tác mỗi người. Kết nối và warm-up đứng ngoài thời gian đo throughput. Sau lượt tải, kịch bản thu hồi từng phiên, kiểm socket tự đóng và phiên kế tiếp vẫn gọi được hub, sau đó xác nhận bearer cũ bị từ chối. Các tệp JSON lưu p50/p95/p99/max, số mẫu, lỗi, thời lượng và số liệu working set của API trước khi chạy và hai giây sau khi socket cuối đóng. [10 users](../../artifacts/authz/hardening-12-proxy-load-10.json) · [25 users](../../artifacts/authz/hardening-12-proxy-load-25.json) · [50 users](../../artifacts/authz/hardening-12-proxy-load-50.json) · [100 users](../../artifacts/authz/hardening-12-proxy-load-100.json) · [200 users](../../artifacts/authz/hardening-12-proxy-load-200.json)

Môi trường Windows 10.0.26200, 12 logical processors, ASP.NET Core 8.0.33 và PostgreSQL 18.4. Dataset có 7 tài khoản fixture nền và 10–200 STAFF dành riêng cho workload. Rate limit login và session được nới riêng trong rehearsal. 401/403/404 chủ đích nằm ngoài số lỗi workload. Cùng máy chạy hồi quy cục bộ nên độ trễ còn chịu ảnh hưởng của tải nền. Chưa đo ổn định dài hạn, query count, saturation của pool, outbound private event ở Release, workload nhiều role hoặc môi trường có độ trễ mạng. Working set còn cao hơn sau hai giây, cần phép đo dài hơn để phân biệt heap/pool đã khởi tạo với tăng trưởng liên tục. Kết quả tuần tự 1.1 thuộc workload khác.

Chạy lại với `pwsh -NoProfile -File scripts/authz/browser.ps1 -Scope Proxy -UseRelease -LoadUsers 50`. Thêm `-ChromeSession` để kiểm Blazor trên bản publish qua cùng origin. Restart giữ PostgreSQL, RSA/OTP key, key ring và proxy. Chrome xác nhận thay đổi localStorage/sessionStorage không mở được route ADMIN, đăng xuất trả 204, hai tab đều về login, bearer cũ nhận 401. [Restart qua proxy](../../artifacts/authz/hardening-12-proxy-restart.json) · [Chrome hai tab](../../artifacts/authz/hardening-12-browser-proxy-session.json)

## Migration, backup và restore

Runner kiểm và migrate 14 DbContext theo thứ tự phụ thuộc. Fixture trống được migrate rồi seed, sao lưu ra archive PostgreSQL custom format 280.649 byte, khôi phục nguyên tử qua `pg_restore --single-transaction` vào DB fixture khác. Đối chiếu số tài khoản, quyền, cư dân, hóa đơn, phân công, kết quả và từng migration ID trong schema auth. Clone có 14/14 context `pending=0`. Hai DB thử và archive được dọn. Regression nâng cấp phiên legacy cũng áp dụng migration auth trên PostgreSQL và đối chiếu lịch sử thu hồi. Chưa chạy phục hồi traffic sau rollback binary hoặc phục hồi tệp provider; runbook ghi điều kiện thu hồi lại phiên và đổi khóa trước khi mở ingress. [Bằng chứng phục hồi](../../artifacts/authz/hardening-12-database-recovery.json) · [Runbook DB](../deployment/database-migration-and-recovery.md)

## Runtime và deployment

Microsoft kết thúc hỗ trợ .NET 8 ngày 10/11/2026. Máy rehearsal có runtime ASP.NET Core 8.0.33. Source vẫn nhắm net8.0 theo phạm vi được duyệt. Triển khai với dữ liệu thật cần chốt nâng cấp runtime LTS còn hỗ trợ, xác minh tương thích Blazor/SignalR/EF/Npgsql và chạy lại test/Release smoke. [Chính sách .NET của Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

[Bộ bàn giao triển khai](../deployment/README.md) gồm hướng dẫn tiếng Việt, preflight, tham chiếu cấu hình, migration/backup/restore và kiểm chứng provider. Chủ dự án thực hiện các thao tác cần quyền hosting/provider ở giai đoạn đã chốt.

| Mức | Đánh giá |
| --- | --- |
| Local Technical Readiness | Đạt các ca hồi quy đã ghi. Còn operation chưa đủ đối chứng, ma trận realtime và concurrency chưa khép kín. |
| Demo Readiness | Có thể demo local bằng fixture tổng hợp và các luồng đã kiểm. |
| Release Artifact Readiness | API/Web publish và khởi động Production local thành công trên runtime đang cài. Runtime có hỗ trợ là điều kiện phát hành. |
| Deployment Rehearsal Readiness | Đạt phạm vi HTTPS cùng origin, WebSocket, restart, migration và backup/restore cục bộ. |
| Staging Readiness | Có runbook; cần chọn hosting, runtime LTS, TLS, DB riêng, log và chạy smoke tại đích. |
| Pilot Readiness | Cần staging đạt, xác minh provider với dữ liệu thử, kiểm vận hành, quyền dữ liệu và người phụ trách. |
| Production Readiness | Chưa đạt. Còn runtime hỗ trợ, coverage bảo mật, provider, restore/monitoring tại đích, tải đại diện và duyệt phát hành/pháp lý. |

Backlog môi trường thật gồm Cloudinary tenant, tài sản public cũ, CDN và orphan cleanup, SMTP, SePay, Gemini privacy/quota, relay TURN và media WebRTC, quyết định ảnh cư dân FE-05, phê duyệt điều khoản/quyền riêng tư và đầu mối hỗ trợ chính thức. [Kiểm chứng provider](../deployment/external-provider-verification.md)

## Kiểm thử hồi quy

| Suite | Kết quả | Chứng cứ |
| --- | --- | --- |
| Unit | 940/940, có nhánh SePay trên PostgreSQL fixture | [TRX](../../artifacts/authz/hardening-12-Unit-Postgres.trx) |
| Architecture | 64/64 | [TRX](../../artifacts/authz/hardening-12-Architecture.trx) |
| Integration | 561/561 trên binary cuối, gồm 9 ca tập trung đã chạy riêng trước đó | [TRX](../../artifacts/authz/hardening-12-integration-verified.trx) · [Ca tập trung](../../artifacts/authz/hardening-12-target-final.trx) |
| Billing | 150/150 | [TRX](../../artifacts/authz/hardening-12-billing.trx) |
| JavaScript | 36/36 | `node --test tests/frontend/*.test.cjs` |
| Chrome route, API, JWT và chéo tài khoản | 378/378 lượt route, 1.073/1.073 probe từ chối, 35/35 JWT và 28/28 tài nguyên | [Route](../../artifacts/authz/browser-matrix.json) · [API](../../artifacts/authz/api-matrix.json) · [JWT](../../artifacts/authz/jwt-matrix.json) · [Tài nguyên](../../artifacts/authz/horizontal-matrix.json) |
| Chrome Release lịch sử | Cả sáu tổ hợp 390/768/1440 px, sáng và tối đạt. Kết quả đúng hai đợt phân công trong fixture | [Kết quả](../../artifacts/authz/hardening-history-browser.json) |
| Release publish | API/Web thành công. Smoke Production xác minh Swagger 404 và giữ trạng thái phiên qua restart | [Restart](../../artifacts/authz/hardening-12-proxy-restart.json) |

Lượt Integration ban đầu có một lỗi do fixture AdditionalCostConsent chưa cấp grant STAFF thật cho hai tài khoản tổng hợp. Bổ sung grant để khớp điều kiện phân quyền worker. Lượt kế tiếp 560/560 đạt. Lượt bổ sung ca race phát hiện fixture dùng PostgreSQL cho PropertyAssets trong khi chỉ tạo schema Maintenance. Đã giới hạn PostgreSQL vào context được chọn, sau đó nhóm 9 ca tập trung chạy đạt. Ma trận Chrome Debug dùng Web artifact cũ hơn hai file UI của trang bảo trì. Bản Debug hiện cùng nền dialog ở hai theme, còn lượt Release vừa publish dùng source cuối và vượt qua các kiểm tra riêng của trang bảo trì.

Quét gói dễ tổn thương trực tiếp và gián tiếp hoàn tất cho solution và project Billing, chưa có advisory trong nguồn quét. Bước publish Web đọc cache restore có NU1900 do sandbox chặn nguồn lúc lấy metadata. Kết quả quét riêng không thay đổi trạng thái hỗ trợ của .NET 8.

## Checkpoint và điều kiện nghiệm thu

| Mục | Bằng chứng hiện có | Phần còn mở |
| --- | --- | --- |
| PF-AUTHZ-03 | Các đợt A → B → A, quyền ảnh, thứ tự tab, STAFF cũ bị chặn gửi ảnh khi reassignment thắng race | Thứ tự upload thắng race, hủy ghi giữa chừng và dọn orphan khi storage/provider lỗi |
| PF-AUTHZ-04 | Refresh/logout cùng cookie, thiết bị khác, hai tab Chrome, revoke giữa cuộc gọi, batch 255/256/257, khởi động lại Release | Ma trận từng nguyên nhân thu hồi × bảy đường dẫn SignalR, retry khi DB gián đoạn, reconnect nhiều hub và relay âm thanh thật |
| PF-AUTHZ-06 | Inventory 247 cặp HTTP, manifest 133 operation có tác động, test đối chứng theo từng operation đã xác minh | Các dòng chưa đủ payload/actor/snapshot trong manifest |
| Tải | 10, 25, 50, 100 và 200 phiên đồng thời qua Release proxy, có p95 thu hồi | Query/pool, dài hạn, nhiều role với private event và latency trên hosting cuối |
| DB và Release | Publish, HTTPS, restart, fresh migration, upgrade auth legacy và restore bản sao local | Rehearsal rollback binary và kiểm smoke trên DB khôi phục có luồng traffic đại diện |
| Bàn giao | Preflight, tham chiếu cấu hình, runbook DB, provider và kế hoạch tiếng Việt | Chủ dự án chốt provider, topology và runtime được hỗ trợ để hướng dẫn thao tác cụ thể |

Bước tiếp theo cho PF-AUTHZ-06 là khởi đầu từ hành động trong cột `action` của manifest, đối chiếu quy tắc actor/tài nguyên rồi thêm ca valid payload và snapshot vào fixture HTTP sẵn có. Ưu tiên hoàn thiện nhóm phiên và đặc quyền FE-01/15, rồi giao dịch và webhook FE-08/09. Chạy Integration/Billing với PostgreSQL cô lập và sinh lại manifest trước khi cập nhật tỷ lệ. [Manifest theo operation](../../artifacts/authz/hardening-12-mutation-coverage.json) · [Lệnh chạy lại](../../scripts/authz/README.md)

Đã dọn các tài nguyên thử nghiệm của lượt 1.2 và đối chiếu cổng 47101, 47201, 47301 cùng cổng động của Unit tests. Hai database mang tiền tố Billing fixture được tạo ngày 13/11/2026 có từ trước lượt này và được giữ lại.
