# PF-AUTHZ-04 — SignalR session revocation và realtime authorization

Ngày kiểm tra: 18/11/2026. Trạng thái: **hoàn tất evidence local PF-AUTHZ-04**, 139/139 ô backend và 176/176 ca PF đạt. Regression toàn dự án còn 7 FAIL ngoài phạm vi; chưa thực hiện Final Local Acceptance và không tuyên bố production-ready.

## C1 — Inventory và phạm vi

Source hiện hành đăng ký bảy đường dẫn, sáu hub types trong `src/PropFlow.Api/Program.cs`; cả bảy bật `CloseOnAuthenticationExpiration`. Các role dưới đây phải đồng thời đáp ứng permission của policy, không chỉ có role claim.

| Đường dẫn | Hub type | Actor được kết nối | Public business/subscription methods |
|---|---|---|---|
| `/hubs/maintenance` | MaintenanceHub | MANAGER, STAFF | JoinTask, LeaveTask (MANAGER); JoinMaintenanceUpdates (self); JoinAssetStatus (asset-view policy); LeaveAssetStatus |
| `/hubs/service-requests` | ServiceRequestHub | MANAGER, STAFF, RESIDENT | Không có; subscription khởi tạo khi kết nối |
| `/hubs/announcements` | AnnouncementHub | MANAGER, RESIDENT | Không có |
| `/hubs/resident-notifications` | ResidentNotificationHub | RESIDENT | Không có |
| `/hubs/notifications` | ResidentNotificationHub | ADMIN, MANAGER, STAFF, ACCOUNTANT, RESIDENT | Không có |
| `/hubs/support-chat` | SupportChatHub | MANAGER, RESIDENT | Heartbeat; ResidentDraft (RESIDENT); ManagerTyping (MANAGER) |
| `/hubs/emergency-calls` | EmergencyCallHub | MANAGER, RESIDENT | Heartbeat; Signal; MediaConnected; MediaDisconnected, theo selected connection và revision |

Inventory gồm 17 cặp path/actor được phép. Ma trận phân biệt internal/resident email workflow, logout, password change/reset, disable, role, effective permission, missed invalidation và natural expiration: **350 dòng, 139 applicable, 211 N/A**. Mỗi N/A có lý do: không có quyền vào endpoint, workflow email thuộc actor khác hoặc endpoint quản trị không hỗ trợ loại tài khoản đó. Đây không phải tỷ lệ coverage mutation; manifest 133 mutation operations không thay đổi.

[Ma trận theo từng dòng](../../artifacts/authz/pf-authz-04-revocation-matrix.json).

[Đối chiếu artifact](../../artifacts/authz/pf04-matrix-validation.json) xác nhận 139 dòng VERIFIED đều khớp exact test name PASS trong TRX full cuối; 211 dòng N/A đều có lý do. Không lấy một PASS cũ để thay outcome mới thất bại.

| Path | Applicable / VERIFIED backend | Inbound | Outbound / reconnect |
|---|---:|---|---|
| maintenance | 18/18 | Hub method + filter | Assignment-aware dispatch; old credential bị từ chối |
| service-requests | 25/25 | N/A: subscription-only | Owner/assignee/manager dispatch; old credential bị từ chối |
| announcements | 16/16 | N/A: subscription-only | Real publication invalidation; old credential bị từ chối |
| resident-notifications | 7/7 | N/A: subscription-only | Persisted private notification; old credential bị từ chối |
| notifications | 41/41 | N/A: subscription-only | Persisted private notification cho năm role; old credential bị từ chối |
| support-chat | 16/16 | Draft/typing/heartbeat | Message/draft/typing theo participant; old credential bị từ chối |
| emergency-calls | 16/16 | Signal/media/heartbeat | Selected connection/revision; old credential bị từ chối |

Trước lượt này đã có implementation và evidence từng phần; chưa có baseline numeric cho toàn bộ 139 tổ hợp nên không gán giả định 0/139. Sau lượt này, từng ô có exact test name, outcome và artifact, bổ sung active chat/call khi cần. Live provider/TURN không phải ô authorization local bị bỏ qua.

### Safety

Lượt này dùng PostgreSQL 18, loopback `127.0.0.1:45488`, data directory tạm riêng có marker sở hữu. `current_database()`, port và data directory được xác minh trước khi chạy. Fixture tiếp tục dùng `TestDatabaseSafety` và database con có marker, không fallback sang database ứng dụng. Catalog chỉ đọc của cluster ứng dụng có 14 database, gồm database ứng dụng và 11 fixture được bảo vệ từ B7; cả `postgres` và `propflow_test` cũng được giữ lại.

[Đích thử thực tế](../../artifacts/authz/pf04-effective-db.json) · [Snapshot được bảo vệ](../../artifacts/authz/pf04-protected-before.json).

Khởi tạo đầu tiên thất bại vì đường dẫn có dấu; tiến trình initdb riêng bị kẹt trong sandbox đã được dừng theo PID. Cluster thay thế dùng đường dẫn ASCII trong TEMP. Không thao tác ghi hoặc dừng cluster ứng dụng.

## C2 — Session lifecycle

Ma trận 105 ca mới đã đạt 105/105 trong lượt focused sau khi sửa cấu hình fixture. Mỗi ca có hai tab cùng SID, một SID độc lập của cùng tài khoản, tài khoản đối chứng hợp lệ và tài khoản không thuộc recipient list. Kiểm positive control trước revoke, workflow thật, persisted refresh rows, bearer/refresh cũ, reconnect, khôi phục role/enable, login mới và outbound qua lifetime manager production. Logout giữ SID độc lập; các thay đổi mật khẩu/email/role/status thu hồi toàn bộ phiên bị ảnh hưởng. Bắt đầu OTP chưa hoàn tất không thu hồi phiên.

Lượt đầu 64 PASS, 41 FAIL đều do HTTP 429 của test host dùng chung IP. Chỉ điều chỉnh `auth-session` limit trong `AuthFlowFactory`, tương tự hai limit test đã có. Không sửa hạn mức production. Giữ [TRX đầu](../../artifacts/authz/pf04-matrix-first.trx) và [TRX chạy lại](../../artifacts/authz/pf04-matrix.trx), không cộng retry vào tổng ca phân biệt.

Các ca bổ sung kiểm fixed-role permission bằng fixture thay đổi permission trong database, không tạo dynamic RBAC endpoint; refresh cũ bị từ chối và restore permission không hồi sinh phiên đã bị revoke. Natural expiration được kiểm riêng với refresh hợp lệ. Các hub chỉ nhận subscription không có method nghiệp vụ để gọi: ghi N/A cho inbound method, không biến thử gửi một method không tồn tại thành bằng chứng business authorization.

`Hub_filter_rejects_a_real_invocation_before_missed_invalidation_can_close_transport` gọi trực tiếp filter production với connection đang được registry theo dõi sau persisted revoke, không gửi outbound hoặc gọi registry invalidation trước đó. Assert callback method không được chạy, caller bị abort và filter từ chối. Thử invocation qua client có thể nhận TaskCanceledException vì socket đã đóng trước khi gói lỗi trả về; đó là transport denial, không phải lỗi ứng dụng.

## C3 — Resource và nội dung riêng tư

Các ca backend đã kiểm notification persisted qua publisher production của maintenance/service request, announcement publish qua HTTP, chat draft/typing/message và signaling của cuộc gọi có thật. Đối chứng bao gồm actor ngoài hộ, ngoài participant list, ngoài selected connection và phiên hợp lệ khác. **139/139 ô applicable đã có bằng chứng backend đúng actor/cause; 0 PARTIALLY VERIFIED, 0 NOT VERIFIED**. Đây là ma trận session authorization; Chrome và media được nghiệm thu riêng bên dưới.

FE-07 bảo toàn historical access theo assignment interval: nhân viên cũ không còn nhận task event, không sửa task hiện tại, vẫn đọc lịch sử thuộc đợt được giao; tiến độ riêng của nhân viên ở đợt khác bị che. FE-05 từ chối nhân viên cũ sau reassignment, kể cả upload/progress đã xếp sau khóa phân công. Các ca `MaintenanceRealtime_StopsSendingTaskEventsToFormerAssignee`, `Reassignment_queued_before_result_upload_revokes_the_previous_assignee_before_storage`, `Former_assignment_result_cannot_be_uploaded_or_approved_after_reassignment` và `Progress_completion_and_evidence_wait_for_earlier_reassignment_then_deny_former_staff` đạt trong Integration. Mất quyền tài nguyên không thu hồi toàn bộ tài khoản.

Announcement draft/audience được kiểm bởi `Resident_cannot_read_drafts_or_other_resident_and_apartment_audiences`. Chat giữ lịch sử nhưng không gửi draft, typing và message mới cho phiên cũ; REST history/reply/receipt/recall vẫn kiểm participant. Emergency giữ hồ sơ khi mất kết nối, chặn signaling ngoài selected connection/revision, chỉ session mới hợp lệ được nhận signaling phục hồi. Các trạng thái WAITING/CONNECTING/CONNECTED được kiểm riêng, không suy trạng thái signaling thành bằng chứng âm thanh đã truyền.

## C4 — Failure, recovery và timing

`Temporary_database_interruption_fails_closed_on_all_paths_and_recovers_without_reviving_revoked_sessions` chỉ chạy khi được cấp rõ đường dẫn cluster tạm và ownership marker. Kiểm port, tên fixture, TEMP path và marker trước khi dừng. Bảy đường dẫn có positive control; khi database ngừng, outbound không được gửi và inbound không chạy business method. Sau khôi phục, phiên hợp lệ nhận sự kiện mới, SID đã revoke vẫn 401 và không reconnect được; login mới hoạt động. Database được khởi động lại trong finally nếu assertion thất bại.

[TRX interruption sau sửa runner](../../artifacts/authz/pf04-runner-fixed-outage.trx). Lượt đầu có một đối chứng bị lặp vì hai aliases dùng chung hub type; sửa recipient của positive control, giữ [TRX gốc](../../artifacts/authz/pf04-boundaries-outage-first.trx). Wrapper EF có thể bọc NpgsqlException: assertion kiểm đúng inner exception. HTTP lỗi không chứa connection string, credential hoặc chi tiết driver. Đây là fixture failure, không phải payload lọt khi database ngừng.

Lượt full ghi đủ 797 kết quả và testhost đã thoát, nhưng `cmd/postgres` khởi động từ pg_ctl vẫn giữ handle của runner. Redirect stdout/stderr riêng chưa đủ trên Windows. Fixture hiện dùng shell launch với cửa sổ ẩn trên Windows để không kế thừa handle của vstest; nền tảng khác redirect cả stdin/stdout/stderr. Chỉ chờ pg_ctl, không chờ toàn bộ cây PostgreSQL. Đã dừng/khởi động lại đúng cluster tạm để giải phóng lượt cũ, rồi chạy lại ca interruption: PASS, runner trả exit 0 trong khi postmaster vẫn chạy. [Xác nhận runner đã trả](../../artifacts/authz/pf04-runner-fixed-status.json). Không dừng cluster ứng dụng và không làm mất kết quả TRX full.

Idle reconciliation đóng cả bảy đường dẫn khi không có invocation, outbound dispatch hoặc immediate invalidation. Service production chạy chu kỳ 30 giây; test chờ tối đa 35 giây và lượt full cuối quan sát 510,7 ms vì đang gần nhịp reconcile. Đây không phải bảo đảm mọi idle socket đóng trong 2 giây. Khi có invocation hoặc private dispatch, authorization boundary kiểm lại ngay dù cố tình bỏ invalidation. Natural JWT expiration đóng access transport nhưng không tự revoke refresh SID còn hợp lệ; refresh đúng session nối lại được, bearer hết hạn không được hồi sinh.

### Timing

Một ca dùng EF DiagnosticListener và ambient transaction completion để lấy T0 commit, MeterListener trên counter registry có sẵn để lấy T1, và callback đóng hai transport cùng SID để lấy T2. Không thêm instrumentation production. Ba lượt PASS ghi commit → đóng cả hai socket **9,67 ms**, **1,38 ms** và **0,40 ms**; SID độc lập vẫn hoạt động. Đây là ba mẫu local, không phải percentile hoặc SLA. Lượt full cuối ghi T0=5,82 ms, T1=5,90 ms, T2=6,22 ms từ bắt đầu thao tác. [Mẫu đầu](../../artifacts/authz/pf04-timeline.trx) · [Mẫu chạy lại](../../artifacts/authz/pf04-permission-final.trx) · [Full cuối](../../artifacts/authz/pf04-source-final-integration.trx).

105 mẫu workflow HTTP → quan sát đóng trong lượt full cuối: p50 **3,1 ms**, p95 **5,6 ms**, max **20,1 ms** tính từ khi nhận response; max **377,0 ms** tính từ bắt đầu workflow. Các số này là cận trên phía test client, không thay cho trace T0/T1. [Dữ liệu](../../artifacts/authz/pf04-timing.json).

Chrome ba nguyên nhân disable/role/password: T3 đầu tiên thấy `/me` hoặc refresh 401 trong **122–287 ms**; toàn bộ tab bảo vệ về login T4 trong **1.766–2.216 ms**. Socket notifications đóng trong **111–265 ms**. Ca Emergency sau sửa: local peer/micro được dọn **782 ms**, phía hợp tác được dọn **927 ms** từ thao tác logout. Các mốc Chrome tính từ bắt đầu thao tác, không cùng gốc thời gian với T0 backend. [Chrome session](../../artifacts/authz/pf04-SessionRevocation-notification-session-revocation.json) · [Chrome media](../../artifacts/authz/pf04-Emergency-notification-emergency.json).

Target local 2 giây server, 3 giây client là engineering target. Gói đã được xác minh và đưa vào hàng đợi trước commit revoke không thể bị thu hồi. Phép thử kiểm dispatch mới và authorization boundary, không tuyên bố hủy mọi frame đang trên đường truyền.

## Regression, thay đổi và phần còn lại

### Application bugs đã tái hiện và sửa

| Lỗi | Nguyên nhân | Sửa tối thiểu | Bằng chứng |
|---|---|---|---|
| Manager kết thúc cuộc gọi rồi nhận cuộc mới nhưng không tạo answer/media | `mediaPaused` giữ từ call trước | Reset khi ID cuộc gọi thay đổi | Ca Chrome cũ thất bại trước revoke; ca mới nối media thật trước logout |
| Bên hợp tác còn giữ audio/peer/micro khi phía kia logout | Refresh giữ call để phục hồi nhưng không dọn media khi participant offline | Stop media khi WAITING/ENDED hoặc một participant offline; giữ nguyên call record | [RED](../../artifacts/authz/pf04-browser-Emergency-media-red.log) → [GREEN](../../artifacts/authz/pf04-browser-Emergency.log) |

Hai sửa đổi nằm trong [EmergencyCallClient.cs](../../src/PropFlow.Web.Client/Features/Communication/EmergencyCalls/EmergencyCallClient.cs). Không thiết kế lại signaling/session, không kết thúc hồ sơ cuộc gọi chỉ vì logout. Bộ Chrome yêu cầu peer thực sự connected và micro live trước revoke, sau đó cả hai phía phải giải phóng tài nguyên trong target local. Media dùng fake microphone và direct ICE local; không có TURN live. SignalR server chặn signaling và đánh dấu participant offline; cleanup P2P được xác minh trên client hợp tác. Kết quả không chứng minh server có thể cưỡng chế dừng mọi media của client bị sửa để bỏ qua cleanup.

Fixture failure được phân biệt: auth-session 429, duplicate recipient của hai hub aliases, EF exception wrapper, pg_ctl giữ stdout pipe, call fixture chưa kết thúc làm ảnh hưởng ca sau, thiếu ML assembly do restore assets cũ, selector `.call-controls` rỗng bị hiểu thành điều khiển còn hoạt động và cold startup Chrome thiếu login trong timeout. Chat từng nhầm `SupportChatChanged` của read receipts với gửi tin thất bại; assertion mới so sánh tập persisted message IDs trước/sau, vẫn kiểm giữ draft và không có nội dung gửi lỗi. Chỉ sửa fixture/assertion đúng contract; không hạ tiêu chí private payload, connected media hoặc cleanup. Các log đỏ được giữ lại.

UtilityConsumption fixture trước đây bỏ connection được truyền và không theo marker safety. Nó đã dùng `PropFlowApiFactory(connection)` cùng Create/Verify/Drop của `TestDatabaseSafety`; không thay service nghiệp vụ utility. Dependency ML đã có trong source pull về, chỉ restore lại assets từ cache, không nâng version.

### Regression và catalog

Kết quả theo tên ca, lấy outcome mới nhất, không cộng retry, được ghi tại [kết quả phân biệt](../../artifacts/authz/pf04-final-results.json). BillingCycle build lại trong output riêng, dependency Web.Client đã được rebuild sau media fix và hash của DLL chép vào test host khớp output. Test assembly không đổi source nên incremental build giữ nguyên byte/timestamp; test source vẫn cũ hơn binary. Lượt cuối đạt **154/154**; lượt 150 ca từ DLL cũ bị loại khỏi kết luận. [Build](../../artifacts/authz/pf04-source-final-billing-build.log) · [Chứng minh source/dependency thực chạy](../../artifacts/authz/pf04-source-final-billing-proof.json) · [TRX](../../artifacts/authz/pf04-source-final-billing.trx).

| Kiểm tra | PASS | FAIL | SKIP / khác | Bằng chứng |
|---|---:|---:|---:|---|
| Solution build | Đạt, 0 warning/error | 0 | — | `pf04-source-final-build.log` |
| Unit/component | 1.060 | 3 | 0 | `pf04-source-final-unit.trx` |
| Architecture | 64 | 0 | 0 | `pf04-source-final-architecture.trx` |
| JavaScript | 47 | 0 | 0 | `pf04-javascript-final.log` |
| Integration full | 793 | 4 | 0 | `pf04-source-final-integration.trx` |
| PF-AUTHZ-04, nằm trong Integration | 176 | 0 | 0 | Không cộng lần thứ hai vào tổng |
| BillingCycle trên dependency cuối | 154 | 0 | 0 | `pf04-source-final-billing.trx` |

Năm suite có **2.125 ca phân biệt: 2.118 PASS, 7 FAIL, 0 SKIP/khác**. 176 ca PF-AUTHZ-04 đã nằm trong Integration, không cộng thêm vào mẫu số. Solution build và publish API/Web Release đạt; JavaScript syntax check trên toàn bộ `.mjs` và `git diff --check` đạt. Bốn lỗi Integration và ba lỗi Unit được giữ nguyên outcome FAIL, không ghi đè bằng ca focused khác tên.

### Chrome Release, HTTPS và transport

| Nhóm | Kết quả mới nhất | Nội dung chính |
|---|---|---|
| SessionRevocation | PASS | Disable, role, password; hai tab cùng SID, SID độc lập bị thu hồi đúng phạm vi, account khác hợp lệ; restore không hồi sinh phiên cũ |
| Emergency | PASS | Selected connection, offer/answer/retry/end; local connected media và cleanup hai phía khi logout |
| Reconnect | PASS | Cùng document phục hồi thông báo/chat bị lỡ, nhận sự kiện mới, không trùng; recipient đăng xuất lấy lại thông báo khi login |
| Operations | PASS | Assignment, asset notices, former/current STAFF; lịch sử đúng interval, link/replay permissions |
| ServiceRequests | PASS | Owner/assignee scope, reassignment, additional cost/consent, kết quả theo assignment hiện tại |
| Communication | PASS sau retry | Announcement audience; draft/typing/message/reply/receipt/recall/hide/forward; API failure giữ draft và không tạo message |
| ReadState | PASS | Hai tab cùng SID, SID độc lập, loại actor khác/phiên cũ, concurrent read-all giữ thông báo mới và không duplicate |

[Tổng hợp bảy nhóm với timestamp/artifact thực tế](../../artifacts/authz/pf04-chrome-final.json). Đây là bảy nhóm kịch bản, không phải bảy bản sao của toàn bộ 139 tổ hợp backend. Các lượt dùng published Release qua HTTPS same-origin loopback proxy. Smoke WebSocket và Long Polling đạt; poll dùng bearer đã revoke trả 401. Restart giữ session hợp lệ 200 và refresh hợp lệ, session đã revoke tiếp tục 401. LoadUsers=1 là smoke, không phải benchmark tải lớn. [HTTPS/WS/LP](../../artifacts/authz/pf04-Communication-hardening-12-proxy-load-1.json) · [Restart](../../artifacts/authz/pf04-Communication-hardening-12-proxy-restart.json).

Lượt chat đầu gặp assertion nhầm receipt invalidation; retry tiếp gặp Windows `EBUSY` khi Chrome ghi `DevToolsActivePort`, chưa chạy đến scenario. Runner hiện retry ENOENT/EBUSY trong vòng chờ có giới hạn sẵn có. Lượt cuối PASS toàn bộ scenario. Giữ [log assertion đầu](../../artifacts/authz/pf04-browser-Communication-first.log), [log khóa file](../../artifacts/authz/pf04-browser-Communication-port-file-busy.log) và [log đạt](../../artifacts/authz/pf04-browser-Communication.log), không dùng artifact cũ để thay kết quả mới.

Full Integration đã ghi đủ 797 kết quả trước sự cố handle của fixture; ca interruption sau sửa launcher chạy lại PASS và runner đã thoát bình thường. Các suite/focused/retry là nhiều lượt được đối chiếu theo tên, **không phải một full clean run**. Không tính ba tên test cũ đã bị đổi/xóa khỏi source vào tổng final; full suite cuối là tập tên chuẩn, focused chỉ cập nhật outcome của tên đã tồn tại.

Baseline mutation đã nghiệm thu vẫn **133/133 VERIFIED**; manifest `mutation-cases.json` giữ nguyên. Source pull có thêm ba mutation ngoài baseline chưa có fixture mapping: `PUT /api/v1/facility-guides/{facility:guid}`, `POST /api/v1/resident/facility-recommendations`, `POST /api/v1/resident/facility-recommendations/speech`. Manifest quan sát hiện có 136 applicable, 133 VERIFIED, 3 NOT VERIFIED (97,79%). Parser tĩnh còn bỏ năm utility insight actions của controller không có class Route; vì vậy không coi 253 khóa quan sát là inventory HTTP đầy đủ của toàn ứng dụng. Các phần mới này nằm ngoài PF-AUTHZ-04; không gắn nhãn toàn web 100% coverage. [Coverage hiện hành](../../artifacts/authz/pf04-mutation-coverage.json).

Lượt source cuối xác nhận ba lỗi Unit ngoài PF-AUTHZ-04: hai ca menu STAFF kỳ vọng hai mục trong khi source đã thêm complaints thành ba; một ca phát hiện native select trong FacilityGuides thay cho shared select. Integration còn hai schema-count assertion cũ (73→80 business tables, 2→8 AI recommendation tables) và hai Finance assertion áp kỳ vọng ACCOUNTANT/AccountantLayout cho trang UtilityInsights đang dùng chung MANAGER/ACCOUNTANT và MainLayout. Đây là các điểm cần đối chiếu/sửa riêng với feature mới pull, không tự kết luận tất cả chỉ là false positive. Chúng không bị sửa hoặc bỏ qua để làm xanh PF-AUTHZ-04.

### Cleanup và giới hạn

Cleanup đã hoàn tất: dừng đúng postmaster PID 23720 sau kiểm marker/PID/port, xóa cluster TEMP và thư mục khởi tạo lỗi có marker riêng. Không còn runtime thuộc output thử hoặc Chrome profile; các cổng 45488/47101/47201/47301 đã giải phóng. Wrapper đã dừng API/Web/proxy/Chrome, drop fixture và dọn certificate/key riêng ở từng lượt. Đối chiếu PID kèm creation time để không nhầm PID được hệ điều hành tái sử dụng.

Snapshot chỉ đọc xác nhận đủ 14 database được bảo vệ, gồm database ứng dụng và 11 database lịch sử; không dừng cluster hoặc ứng dụng do người dùng chạy. [Snapshot trước](../../artifacts/authz/pf04-protected-before.json) · [Snapshot sau](../../artifacts/authz/pf04-protected-after.json) · [Cleanup](../../artifacts/authz/pf04-cleanup.json). Không suy việc catalog còn đủ database thành phép so sánh từng dòng dữ liệu; các lệnh ghi/test đều dùng đích tạm đã xác minh.

Framework, SDK/runtime, dependency versions, schema/migrations và business contracts giữ nguyên. Không commit/push. Live TURN/media giữa hai mạng Internet, provider ngoài, Final Local Acceptance và runtime release blocker được theo dõi riêng. Không tuyên bố production-ready.

Các artifact test nằm trong `artifacts/authz` bị Git bỏ qua; source tests, runner và báo cáo trong project chính là phần review/commit được. Không dùng chứng cứ cũ để thay một kết quả mới thất bại.
