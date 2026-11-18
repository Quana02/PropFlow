# Post-pull regression stabilization & authorization delta

Ngày: 18/11/2026. Hoàn tất trong project chính; không commit/push. Lượt cuối trên source cuối: **2.136 PASS, 0 FAIL, 0 SKIP**; authorization **142/142 VERIFIED**, baseline 133 mapping giữ nguyên, PF-AUTHZ-04 139/139 giữ nguyên.

## D1 — Bảy lỗi baseline

| Exact test | Root cause | Phân loại | Sửa theo contract | Kết quả |
|---|---|---|---|---|
| `CompactNavigationTests.Actual_shared_menu_preserves_role_routes_order_permissions_and_active_state(role: "STAFF", permission: "PERFORM_ASSIGNED_OPERATIONS", route: "/staff/service-requests/123", count: 2)` | Chưa tính menu Complaints đã được yêu cầu | Test lỗi thời | Ba route đúng thứ tự; cùng method, thêm ca active Complaints, thiếu permission vẫn không có menu | PASS |
| `CompactNavigationTests.Actual_shared_menu_preserves_role_routes_order_permissions_and_active_state(role: "STAFF", permission: "PERFORM_ASSIGNED_OPERATIONS", route: "/my-maintenance", count: 2)` | Cùng navigation contract | Test lỗi thời | Giữ service requests/maintenance/complaints; không mở menu Manager/Admin | PASS |
| `DropdownUiTests.WebClient_UsesSharedSelectInsteadOfNativeSelectFields` | FacilityGuides còn bốn native select | Implementation | Dùng `PropFlowSelect` hiện có; giữ GUID/string binding và payload | PASS |
| `DatabaseVerificationTests.Database_ShouldContain73BusinessTablesInCoreSchemas` | Baseline thiếu các bảng mới pull | Test lỗi thời | Giữ tên method; so exact `(schema,table)` với 13 relational owner models sau đối chiếu migrations | PASS |
| `DatabaseVerificationTests.AiRecommendation_ShouldContainExpectedTwoTables` | Baseline AI recommendation cũ | Test lỗi thời | Exact tám tên bảng đã đối chiếu migration/model/schema; không đổi đơn thuần 2→8 | PASS |
| `FinanceAccessTests.Finance_pages_require_accountant_role` | Áp finance-only contract cho shared UtilityInsights | Test lỗi thời | Riêng shared page cho MANAGER/ACCOUNTANT + `billing.read`; finance pages khác giữ ACCOUNTANT + `finance.manage` | PASS |
| `FinanceAccessTests.Finance_pages_use_accountant_layout` | Áp AccountantLayout cho shared page | Test lỗi thời | Shared UtilityInsights dùng MainLayout, finance pages khác giữ AccountantLayout | PASS |

Đối chiếu trực tiếp bảy FAIL từ PF04 với TRX cuối: [7/7 resolved](../../artifacts/authz/stabilize-seven-failure-validation.json), giữ đầy đủ FQN và parameters trước/sau.

FE-06 requirement ghi Manager sở hữu vòng đời/official response, Staff chỉ thực hiện follow-up được giao. User đã yêu cầu menu `/staff/complaints`; direct route và API scope vẫn được giữ. Requirements `Shared-Facility-Recommendations.md` và `Utility-Consumption-Insights.md` được pull ở commit `1dd76c5`; weekend notice làm rõ tại `f0ee21c`. Không lấy implementation làm nguồn duy nhất để cấp quyền.

Checkpoint [D1](../../artifacts/authz/stabilize-d1-status.json): Unit 16/16, Integration 4/4. Không bỏ/skip/rename bảy method để che FAIL; hai theory cases được cập nhật input count đúng approved navigation. Lượt sandbox đầu abort vì testhost không kết nối trong 90 giây; lượt test thực chạy elevated giữ nguyên source và guards. Artifact lỗi gốc PF04 vẫn được giữ.

### Schema ownership

Bảy bảng thêm so với 73-core-table baseline: `property_assets.facility_guides`; `ai_recommendation.utility_insights`, `utility_insight_reviews`, `utility_ai_attempts`, `utility_notification_outbox`, `facility_recommendation_runs`, `facility_weekend_notice_runs`. Owner lần lượt PropertyAssets và AiRecommendation. Các migration 14–17/11 hiện có được dùng, không tạo migration mới. Migration ReplaceOccupancy giữ rule/schedule thành draft và bỏ occupancy/settings cũ; upgrade regression vẫn chạy.

Database fixture mới có tám bảng AI recommendation, model identity khớp migration result và không có bảng orphan trong 13 core schemas. [Ownership mapping](../database/PropFlow_Database_Ownership_Mapping.md) · [Danh tính thực tế](../../artifacts/authz/stabilize-schema-identities.txt). DBML lịch sử không được coi là inventory exhaustive của mọi feature mới; phần delta ownership đã cập nhật rõ.

## D2 — HTTP inventory

Parser hỗ trợ controller không có class Route; action-only routes; `/` và `~/` absolute templates; controller/action aliases; method attributes; tokens `[controller]`/`[action]`; constraints và array response metadata `typeof(View[])`. Cùng method/route key không bị tính trùng. Ba parser regression tests PASS và metadata `ControllerActionDescriptor` của ASP.NET Core test host khớp exact 258 khóa. Chỉ MVC business/technical HTTP controller operations được so sánh; SignalR negotiate/transport, framework/static resources không bị trộn vào mẫu số này.

| Hạng mục | PF04 | Hiện tại |
|---|---:|---:|
| HTTP operations | 253 (B7:247) | 258 |
| Applicable | 136 tạm tính | 142 |
| VERIFIED | 133 | 142 |
| NOT VERIFIED | 3 tạm tính | 0 |
| PARTIALLY VERIFIED | 0 | 0 |
| NOT APPLICABLE | 117 | 116 |

Không có removal/route-template change/guard-role change trong 247 baseline keys. Có 11 added keys so với B7. [Delta machine-readable](../../artifacts/authz/stabilize-http-delta.json) · [Metadata runtime](../../artifacts/authz/stabilize-runtime-http-keys.json) · [Test](../../tests/PropFlow.IntegrationTests/HttpInventoryTests.cs).

### Toàn bộ HTTP delta so với B7

| Method/route | Role/policy và scope | Applicable / lý do |
|---|---|---|
| `GET /api/v1/facility-guides` | MANAGER + MANAGE_OPERATIONS; property current deployment | N/A: read-only management projection, không provider/write |
| `PUT /api/v1/facility-guides/{facility:guid}` | MANAGER + MANAGE_OPERATIONS; facility thực, Staff active, optimistic token | Có: lưu/publication guide, không đổi operational asset state |
| `GET /api/v1/resident/facility-recommendations` | RESIDENT + active residency | N/A: published catalogue read-only |
| `GET /api/v1/resident/facility-recommendations/today` | RESIDENT + active residency; published facility | Có: cấp vé speech ngẫu nhiên, tạm, gắn actor; không provider |
| `POST /api/v1/resident/facility-recommendations` | RESIDENT + active residency; current published facts | Có: provider/cache/quota run |
| `POST /api/v1/resident/facility-recommendations/speech` | RESIDENT + active residency; server ticket belongs to actor | Có: Gemini TTS, transcript server-created, cache/quota |
| `GET /api/v1/utility-insights` | MANAGER/ACCOUNTANT + billing.read | Có: có thể materialize/recalculate persisted insights; không gọi Gemini |
| `GET /api/v1/utility-insights/apartments/{apartment:guid}` | Cùng policy; Billing public source resolves apartment/month | Có: cùng persisted effect |
| `POST /api/v1/utility-insights/apartments/{apartment:guid}/explain` | Cùng policy; fingerprint hiện hành | Có: provider attempt/quota/explanation |
| `POST /api/v1/utility-insights/apartments/{apartment:guid}/review` | MANAGER + billing.configure | Có: review history/outbox, không sửa invoice/payment |
| `GET /api/v1/resident/utility-insights` | RESIDENT, issued invoice recipient snapshot | Có: authorized periods có thể materialize/recalculate persisted insight |

Năm dòng UtilityInsights cuối là năm actions bị parser cũ bỏ sót. Ba GET không được gọi là read-only chỉ vì HTTP verb. Mẫu số 142 = 133 baseline + 9 delta; hai catalogue reads N/A không đồng nghĩa đã VERIFIED toàn bộ read authorization.

## D3 — Bằng chứng operation mới

Ba fact real-login HTTP bao phủ chín operation với method/route mapping riêng trong `mutation-cases.json`; dùng production policies/services và PostgreSQL, fake provider/public Billing source có kiểm dữ liệu đầu vào. Không dùng fake authentication cho evidence mới. Các test feature cũ bổ sung real Billing-source, migration upgrade, provider transport và UI evidence.

| Actual test | Positive/negative controls và side effects |
|---|---|
| `Post_pull_facility_guide_uses_real_login_permissions_tokens_and_preserves_other_assets` | Valid Manager lưu đúng facility/actor/token; anonymous/sai role/revoked/mất permission không đổi snapshot; nonexistent facility 404, Staff phụ trách không hợp lệ 400, stale/replay 409; hai save cùng token chỉ một thành công; manipulated facility/creator/status không đổi actor hoặc asset state |
| `Post_pull_facility_recommendation_and_speech_enforce_resident_scope_privacy_and_provider_effects` | Resident có liên kết/cư trú thật; wrong role/anonymous/revoked/mất permission bị chặn; identity input không thay actor; provider nhận published candidates không nhận contact/Staff ID/actor ID; cache per actor; fallback khi lỗi hoặc output key giả; giới hạn input; actor khác không dùng ticket; WAV base64 JSON/no-store; cache replay; speech failure 503; hết eligibility không gọi provider |
| `Post_pull_utility_operations_check_real_sessions_role_scope_and_persisted_effects` | Manager/Accountant reads/explain, Manager-only review; negative controls trên cả năm routes; provider không nhận apartment/user/unit; reads không gọi provider, retry explain không gọi thêm; snapshot recipient chặn hộ khác dù fixture source cố tình trả candidate period rộng; stale fingerprint 409; review cạnh tranh tạo một history/outbox; response không có recipients/email |

`DeltaSnapshot` kiểm guide/facility/run/insight/history/attempt/outbox/notifications cùng provider counts trước–sau **từng denied request**, sau onboarding/login fixture. Không so snapshot trước cả bước tạo tài khoản vì registration hợp lệ có thể tạo welcome notice. Hai lượt focused đầu đã giữ tại `stabilize-d3-final.trx` và `stabilize-d3-retry.trx`.

Lượt full sau đó phát hiện snapshot global còn chịu ảnh hưởng từ outbox/worker của những ca PF trước trên Auth fixture dùng chung (notifications 217→221 và 469→473), trong khi guide/run/provider không đổi. Không xóa assertion notification: mỗi delta fact hiện tạo một `AuthDatabaseFixture` riêng qua guard Create/Verify/Drop và tắt hosted workers chỉ trong test host riêng. Production policies, JWT login, services và notification tables vẫn thật; worker workflow được kiểm riêng trong existing suites. `stabilize-isolated-delta.trx` xác nhận 3/3 PASS sau cô lập. Lượt full có hai FAIL được giữ dưới `stabilize-attempt2-integration.trx`.

Fixture còn có guide nháp đối chứng: valid update giữ nguyên token/metadata/nội dung guide khác, và nội dung `INTERNAL_GUIDE_CANARY` không tới provider hoặc resident suggestions. Đây là synthetic fixture, không thêm tiện ích vào database ứng dụng.

### Lỗi production bổ sung đã red/green

`FacilityProviderPrivacyTests.Actual_Gemini_facility_transport_redacts_credentials_in_need_and_public_context` dùng HTTP handler local capture request thực của `GeminiFacilityNeedAdvisor`; red vì credential canaries tới payload dù email/phone đã bị che. `FacilityNeedMatcher.Redact` hiện áp mẫu credential từ B7 cho JWT và API key/password/OTP có nhãn, cả need lẫn guide context và speech facility name. Không thêm cross-module dependency. Không lưu prompt/canary payload vào artifacts. [RED](../../artifacts/authz/stabilize-privacy-red.trx); GREEN nằm trong full Unit cuối.

Speech provider thực là `GeminiFacilitySpeech`, không SpeechSynthesis/browser TTS. Vé gắn actor, transcript cố định/server-created; request text tùy ý không được chuyển tới provider. Các existing `FacilitySpeechTests` kiểm ticket expiry/cache/quota, provider failure và malformed PCM/media. `FacilityDiscoveryTests` kiểm source/residency thay đổi trong khi provider đang chạy, unknown/duplicate keys, per-actor cache, quota và public factual projection. Test transport bổ sung `Actual_facility_providers_fail_safely_on_timeout_http_failure_and_malformed_response` chạy cả advisor và TTS với HTTP handler local để kiểm đủ ba failure modes. Không gọi external provider live.

## D4 — Regression / cleanup

### Kết quả cuối trên source cuối

| Suite | PASS | FAIL | SKIP/khác | Artifact |
|---|---:|---:|---:|---|
| Unit/component | 1.067 | 0 | 0 | `stabilize-final-unit.trx` |
| Architecture | 64 | 0 | 0 | `stabilize-final-architecture.trx` |
| JavaScript | 50 | 0 | 0 | `stabilize-final-javascript.log` |
| Integration | 801 | 0 | 0 | `stabilize-final-integration.trx` |
| BillingCycle | 154 | 0 | 0 | `stabilize-final-billing.trx` |
| **Tổng phân biệt** | **2.136** | **0** | **0** | `stabilize-final-results.json` |

Đây là một lượt sạch của cả năm suite trên source cuối qua `stabilize-test-tail.ps1`; mỗi suite lấy đúng artifact mới của lượt đó. Các focused/retry không cộng vào tổng. 176 ca PF-AUTHZ-04 đã nằm trong Integration. Tất cả tên test phân biệt được kiểm bằng HashSet. Solution build và BillingCycle dependency build đều 0 warning/0 error; không suppress NU1900 hay cảnh báo khác. Các cảnh báo TestServer về request-body-size feature vẫn giữ trong logs.

API/Web publish Release đạt; Web được publish lại sau bản sửa shared select và dùng cho Chrome PASS. Timestamp/hash của test assembly và Web.Client dependency mới của BillingCycle được lưu ở `stabilize-final-billing-proof.json`. Source hash được xác minh không đổi sau build; `stabilize-tail-status.json`, `stabilize-chrome-status.json` và `stabilize-final-source-manifest.json` là các artifacts kết quả cuối. `stabilize-final-status.json` là lịch sử pipeline đầu, còn giữ Chrome FAIL trước khi sửa; không dùng file lịch sử đó thay kết quả cuối.

Inventory cuối có 70 UI routes, 258 HTTP operations, không có guard chưa phân loại; metadata MVC và parser khớp exact keys. Manifest cuối có 142 VERIFIED, 0 PARTIALLY VERIFIED, 0 NOT VERIFIED, 116 NOT APPLICABLE. Cả bốn TRX cuối được dùng để tái tạo [coverage](../../artifacts/authz/final-authorization-mutation-coverage.json). 14 mapping cần evidence từ BillingCycle nên bản đối chiếu tạm chỉ có ba suite được giữ riêng ở `stabilize-three-suite-coverage.json`; không dùng bản tạm để báo hoàn tất.

[Baseline validation](../../artifacts/authz/stabilize-pf-baseline-validation.json) xác nhận 133 mapping cũ không đổi và tiếp tục VERIFIED; 139 dòng PF04 cùng 167 tên evidence liên quan đều tìm thấy PASS trong Integration mới. Không audit lại matrix hoặc cộng 176 ca PF lần thứ hai.

Cleanup đã hoàn tất lúc 09:19:44 UTC: kiểm marker/path/PID rồi dừng đúng postmaster PID 6276, xóa cluster tạm, giải phóng 45488/47101/47201/47301. Không còn artifact runtime process hoặc Chrome notification profile. Wrapper dọn certificate/key files và synthetic assets của browser. Snapshot chỉ đọc xác nhận đủ 14 database bảo vệ; không dừng cluster ứng dụng. Không suy việc catalog còn đủ thành phép so từng dòng dữ liệu; mọi đích ghi đều là fixture/cluster riêng đã xác minh. [Cleanup](../../artifacts/authz/stabilize-cleanup.json) · [Snapshot sau](../../artifacts/authz/stabilize-protected-after.json).

Kiểm độc lập sau cleanup lúc 09:21:58 UTC xác nhận **0** owned cluster processes, test listeners, Chrome profiles, key directories, certificate files và synthetic-asset directories; cluster directory không còn. [Resource verification](../../artifacts/authz/stabilize-final-resource-verification.json).

### Shared select — lỗi tìm thấy trong targeted Chrome

Chrome đã tái hiện hai lỗi của component dùng chung: `aria-expanded`/`aria-selected` dùng Razor boolean khiến giá trị false bị bỏ hoặc rút gọn, và callback Enter từ JavaScript thay đổi state nhưng không yêu cầu render lại nên DOM còn listbox cũ. Sửa tối thiểu trong `PropFlowSelect.razor`: xuất chuỗi `true`/`false` cho hai ARIA attributes; render sau callback chọn bằng bàn phím. Không đổi CSS hoặc business binding.

`Manager_edits_published_profile_with_real_contact_and_schedule_not_people_counts` có assertion ARIA đóng; `Keyboard_JS_callback_closes_and_renders_the_listbox_even_when_the_value_does_not_change` render component thật và gọi đúng public JS callback. Cả hai có RED riêng (`stabilize-select-aria-red.trx`, `stabilize-select-enter-red.trx`), focused GREEN cuối 4/4 gồm hai provider transport tests.

Targeted Chrome HTTPS `PostPull` đã PASS trên Web Release cuối: 390/768/1440 px × light/dark, popover trong viewport, không tràn ngang, Escape/ArrowDown/Enter, giá trị ARIA hợp lệ, busy/loading/empty, PUT lỗi 503 giữ bản nháp, hai Utility role hợp lệ, ba role còn lại bị chặn, menu Staff Complaints active. Đã xem trực tiếp ảnh 390-dark và 1440-light. Artifact: `stabilize-notification-post-pull.json`, `stabilize-chrome-status.json` (exit 0, 07:27:46 UTC). Các app/proxy/Chrome/cert/profile của lượt này đã được runner dọn.

Các lượt Chrome trước còn phát hiện lỗi riêng của harness: fixture không có facility, scroll animation chưa ổn định khi đo viewport và chuỗi selector Staff sai escape. Đã sửa setup/wait/selector và giữ logs lỗi; không dùng chúng làm lý do đổi UI CSS hay nới assertion. Browser facts hiện dùng actor thật và fixture facility tạo bằng Manager API trên DB test riêng.

Lịch sử retry được giữ riêng. Lượt full đầu phát hiện fixture `FacilityDiscoveryUiTests.Manager_edits_published_profile_with_real_contact_and_schedule_not_people_counts` thiếu `IJSRuntime` khi trang chuyển sang shared select. Đã thêm đúng `NoJavaScript` mock sẵn có, không bỏ assertion. Artifacts lượt đầu giữ dưới prefix `stabilize-attempt1-*`; không dùng hợp nhất các retries làm lượt sạch cuối.

Cluster test loopback port 45488, ASCII TEMP path, marker và current_database/data_directory verification được ghi trước khi thử. Database ứng dụng và 14 database bảo vệ không được dùng làm đích ghi. Các suite PostgreSQL/Chrome chạy tuần tự. Giữ bản sửa Windows pg_ctl shell launch để server không giữ handle của vstest.

Lượt full Integration sau bản sửa shared select đạt 800/801, một FAIL ở `Idle_reconciliation_closes_all_seven_paths_without_an_invocation_dispatch_or_immediate_invalidation`: timeout tại positive marker đầu (dòng 42 cũ), **trước** revoke và trước cửa sổ đo idle; không phải timeout 35 giây của reconciliation. Client handshake có thể hoàn tất trước server lifetime registration. Setup hiện đợi completion của method cố ý không đăng ký trước mỗi positive marker; đây chỉ là readiness barrier trong test, không đổi session implementation. Server ẩn detailed errors nên assertion kiểm tên method được phản hồi thay vì chuỗi lỗi nội bộ. Vẫn giữ marker 3 giây, cửa sổ idle 35 giây không hoạt động, bearer 401, phiên đối chứng 200 và delivery isolation. Focused `stabilize-idle-setup-green.trx` PASS; lượt full lỗi giữ tại `stabilize-idle-setup-first.trx`, không thay outcome bằng focused trong tổng.

Lượt full tiếp theo giữ idle PASS (25,06 giây) nhưng cùng setup race xuất hiện ở `Temporary_database_interruption_fails_closed_on_all_paths_and_recovers_without_reviving_revoked_sessions`, dòng 69: timeout nhận `available` **trước** lệnh stop PostgreSQL. Artifact `stabilize-outage-setup-first.trx` giữ 800 PASS/1 FAIL/0 SKIP. Readiness barrier được gom vào `AuthenticationTestSessions.AwaitServerReadyAsync` và dùng cho đúng hai setup này. Không đổi lifetime manager, hub policy/filter, worker interval hoặc timeout. Focused chung `stabilize-realtime-setup-green.trx` đạt 2/2: outage thực stop/start cluster riêng (32 giây), idle 23 giây; các assertions fail-closed, recovery và không hồi sinh phiên cũ đều giữ nguyên.

Framework/C#12/net8.0/SDK/runtime/package versions, migrations/schema và approved contracts không đổi. Không revert feature mới. Không audit lại B1–B7 hoặc chạy lại riêng 139 tổ hợp; full Integration vẫn chứa PF tests hiện hành. Không tự chuyển sang Final Local Acceptance, không commit/push và không tuyên bố production-ready.

## E — Phân loại điều kiện còn lại

Không còn regression FAIL/SKIP, missing authorization evidence hoặc DECISION REQUIRED trong scope navigation, FacilityGuides và UtilityInsights. Các kết quả D4 và coverage mới xác nhận điều này; không lấy outcome cũ thay một FAIL mới.

External-provider verification vẫn nằm ngoài lượt này: Gemini/SMTP/SePay/Cloudinary live và TURN/media giữa mạng Internet không được gọi. Provider tests dùng fake/local HTTP handlers. Runtime release blocker đã ghi ở [runtime security risk assessment](propflow-runtime-security-risk-assessment.md) không được giải quyết bằng lượt stabilization này; không đánh đồng local regression PASS với được phép release.

## F — Xác nhận phạm vi

Giữ C#12, net8.0, SDK8.0.314, runtime8.0.33, EF8.0.11, Npgsql EF8.0.11/driver8.0.6 và Gemini model cấu hình hiện có. Targeted Chrome ghi nhận API/Web thực load CLR/ASP.NET Core8.0.33 tại `stabilize-final-loaded-runtimes.json`. Không sửa project/package manifests, schema/migration files, RBAC roles, kiến trúc, business contracts hoặc thêm tính năng. Các bản sửa UI/accessibility/provider privacy và test/inventory nằm trực tiếp trong project chính; `artifacts/authz` là evidence local bị Git ignore. Không commit/push/deploy; dừng để chủ dự án nghiệm thu trước Final Local Acceptance.
