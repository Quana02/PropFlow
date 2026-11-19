# PropFlow — Final Local Acceptance & Deployment Readiness Gate

Ngày 18/11/2026. Nghiệm thu trực tiếp project chính, HEAD `1906ca7bebba76ac8902942d050240f978ab83cb` cùng working diff của Service Request Review. Báo cáo này tách nghiệm thu local bằng dữ liệu tổng hợp khỏi quyết định mở môi trường thật. Không commit, push hoặc deploy.

**Gate kỹ thuật local: PASS.** Full regression cuối 2.154 PASS, 0 FAIL, 0 SKIP; Chrome Release HTTPS và cleanup đạt. **Nghiệm thu toàn bộ đặc tả: PARTIALLY VERIFIED**, do FE-15.8 chưa có contract/implementation được duyệt. Local/demo cô lập không đồng nghĩa production-ready; public staging/pilot/production vẫn BLOCKED ở phần F.

## A. Functional acceptance

Phạm vi là các business journeys đại diện theo requirements FE-01–15 và các quyết định bổ sung hiện hành, không quy đổi số test thành phần trăm hoàn thành đặc tả. Ma trận chi tiết có expected actor/behavior, bằng chứng, phần thiếu, phương pháp, result và limitation: [acceptance matrix](../../artifacts/acceptance/final-local-acceptance-matrix.json).

| Feature / Journey | Actor | Bằng chứng chính | Kết quả và giới hạn |
|---|---|---|---|
| FE-01: eligibility, OTP, login, email/password, session lifecycle | Cả năm role; Manager hỗ trợ email Resident | AuthenticationFlowTests; HTTP thật với JWT và fake mail | PASS; fake SMTP không chứng minh delivered |
| FE-02/03: onboarding, household, ownership/residency, căn hộ | Manager, Resident; Staff/Accountant đúng projection | ResidentOnboardingHttp/Atomic, ApartmentOwnershipConcurrency, PropertyOperationsAuthorization; BillingCycle apartment catalogue | PASS; Các đoạn nghiệp vụ được kiểm riêng, không tuyên bố một lượt browser xuyên suốt onboarding |
| FE-04: current building, facility/equipment và weather | Manager; Staff đọc theo assignment | Fe04StaffHttp, PropertyOperationsAuthorization, CurrentBuildingWeather | PASS; Một deployment/một chung cư; weather provider giả |
| FE-05: Manager review → Resident consent → xử lý → invoice → payment → feedback | Manager, Staff, Resident, Accountant | Hai ca Acceptance mới; CostReview/InvoiceRecipient/Workflow; Chrome ServiceRequests | PASS; Hai nhánh có phí/miễn phí; mô tả kỹ tại C |
| FE-06: complaint, follow-up, resolve/close | Resident, Manager, Staff | ComplaintAnnouncementHttp, Communication/PropertyOperationsAuthorization | PASS; Không đóng/resolve bỏ qua work; bảo vệ hộ và assignment |
| FE-07: schedule/task, kết quả, nghiệm thu và asset state | Manager, Staff | MaintenanceAuthorizationHttp; PF resource boundary tests | PASS; Giữ interval lịch sử, A→B→A; không dùng quyền FE-04 để bypass |
| FE-08/09: fee/rent/service invoice, payment/debt, receipt, reminder | Manager, Accountant, Resident; Admin cấu hình được phép | BillingCyclePostgres, PaymentAuthorizationHttp, FinanceWorkflowHttp, SePayWebhookHttp | PASS; Pending/reject không tự giảm nợ; fake SePay/SMTP; không giao dịch thật |
| FE-10/11: classification/priority/recommendation và Manager review | Manager, Staff, Resident gửi yêu cầu | AiAuthorizationHttp, provider privacy/fallback tests | PASS; AI không tự quyết nghiệp vụ; Gemini giả |
| FE-12: knowledge/version/publication và chatbot đúng audience | Admin, Manager, Staff, Resident | KnowledgeDocuments, AiAuthorizationHttp knowledge/chat, ResidentChatDatabase | PASS; Không đưa draft/internal vào nguồn cư dân; chưa kiểm Gemini live |
| FE-13: notification, announcement, chat, emergency, reconnect/revocation | Cả năm role theo từng chức năng | CommunicationAuthorizationHttp, AuthenticationFlow/PF, Chrome kế thừa và mới | PASS; Local HTTPS/WebRTC không chứng minh TURN hai mạng Internet |
| FE-14: dashboard, counters/charts, filters/projection | Cả năm role | AdministrationOverview fixture counts/charts; scoped finance API so UI Chrome; business projection tests | PASS; Đại diện; không mọi báo cáo/date filter được thao tác UI-to-UI. PDF/Excel không bắt buộc theo BR-14.11 |
| FE-15: internal accounts, fixed role/status, effective access, activity | Admin; role khác bị chặn theo contract | Administration/Activity, AuthenticationFlow, route/menu tests | PARTIALLY VERIFIED; Phần đã hỗ trợ được nghiệm thu riêng; FE-15.8 chưa hoàn thành |
| FacilityRecommendations/Guides/weekend notices | Manager, Resident; Staff không quản lý | FacilityDiscoveryHttp, FacilityDiscovery/ProviderPrivacy/Weekend tests | PASS; Facts/publication, eligibility, cache/quota/fallback; fake Gemini |
| Speech | Resident | FacilitySpeech, FacilityDiscoveryUi, frontend audio tests | PASS; Ticket actor-bound, transcript server, expiry/cache/quota, WAV/error; chưa nghe TTS live |
| UtilityInsights | Manager, Accountant, Resident | UtilityConsumptionHttp, UtilityConsumption/InsightUi | PASS; Fingerprint, Manager review, recipient snapshots, outbox; không sửa invoice/payment |
| Public information và UI đại diện | Anonymous và cả năm role | Chrome mới + Phase 2 còn phù hợp | PASS; Public access/links/FAQ/register draft; chưa có phê duyệt pháp lý |

**Khoảng trống đặc tả đã có từ trước:** FE-15.8 Manage Supported System Configuration là **NOT VERIFIED**, chưa có danh mục settings/consumer/permission/API/UI được duyệt và triển khai. Không tự chuyển thành N/A/deferred. Phần FE-15 tổng thể là **PARTIALLY VERIFIED** nếu tính cả mục này. Payment configuration không thay thế administrative configuration. Nguồn: [quyết định FE-15](../requirements/FE-15-Administration.md).

Hai đường dẫn Phase 1/2 trong đề bài không tồn tại dưới đúng tên `propflow-phase1-security-privacy-hardening.md` và `propflow-phase2-implementation-report.md`. Đã dùng tài liệu hiện có được index trong [audits README](README.md): security-privacy-regression, public-information-verification, frontend-regression, legal-privacy-review và release-readiness; không tạo báo cáo giả thay thế. Các bộ số liệu lịch sử không được cộng vào kết quả mới.

## B. Role acceptance

| Role | Phạm vi nghiệm thu đại diện | Ranh giới giữ nguyên |
|---|---|---|
| ADMIN | Dashboard, internal account/role/status/activity, published knowledge management và payment settings | Không ngầm có nghiệp vụ Manager; fixed RBAC, không grant permission động |
| MANAGER | Cư dân/căn hộ/tài sản, service/complaint/maintenance, phí, AI review, Guides/Insights, chat và announcement | Business decisions phía server; một deployment/chung cư |
| STAFF | Service work, maintenance, complaint follow-up, published knowledge, historical assignment | Current/former assignment và từng interval; không đọc nháp hoặc quản trị asset |
| ACCOUNTANT | Finance/payment/debt/reconciliation/reminder, UtilityInsights đọc/explain | Không review Insights hoặc mở contact projection ngoài contract |
| RESIDENT | Eligibility/onboarding, tài nguyên hộ, service/complaint, invoice/payment, chatbot/facility/speech và giao tiếp | Không truy cập hộ khác; invoice recipient bất biến; chỉ nguồn AI đúng audience |

Role/menu/direct route/component assertions được đối chiếu full regression; HTTP tests kiểm dữ liệu và side effects, không chỉ trang render. Chrome mới kiểm màn hình đại diện của cả năm role ở ba chiều rộng và hai theme. Các API mutation trong kịch bản Chrome được ghi là **API-to-browser**, không gọi chúng là UI-to-UI toàn bộ workflow.

## C. Cross-module acceptance

Hai ca mới `Acceptance_cost_consent_processing_invoice_http_payment_and_feedback_form_one_journey(charged: True/False)` bổ sung đoạn nối còn thiếu. Request đã được fixture seed/review, sau đó Manager phân công và Staff nhận bằng HTTP thật. Resident submission và AI review/fallback có assertions riêng trong bộ regression; không gộp thành một ca E2E browser chưa thực hiện.

- **Có phí:** Staff đề xuất 30.000 → `AWAITING_MANAGER`; Resident consent và Staff progress sớm trả 409 → Manager duyệt đúng proposal → Resident đồng ý → Staff progress/complete/private evidence → Manager approve/close → đúng một invoice 30.000 và đúng recipient snapshot → dispatcher chạy hai lần vẫn một invoice-ready notice → Resident report-transfer chỉ tạo PENDING → feedback bị chặn → Accountant confirm bằng idempotency key, replay không nhân payment → receipt đúng hộ, hộ khác 404 → feedback hợp lệ.
- **Miễn phí:** xử lý/evidence/nghiệm thu/đóng/feedback không cần proposal/consent; không tạo invoice/payment rỗng.
- **Đối chứng:** base cost mới khác 0, stale proposal/version, cạnh tranh approve, replacement/rejection, progress/evidence/approval gates, A→B→A, former Staff, invoice single issue và thay live Resident linkage được kiểm trong CostReview/Workflow/InvoiceRecipient. Không lấy live linkage thay immutable invoice recipient.
- **Xuyên module khác:** onboarding/household transactions; complaint follow-up→resolve/notification; maintenance acceptance→asset state; Billing→Payments→balance/receipt; knowledge publication→RAG audience; utility fingerprint review→recipient/outbox đều dùng production contracts và fixtures của module sở hữu. Không thêm cross-module EF navigation.

Luồng Chrome dùng Manager approval dialog thật, kiểm Escape/cancel không approve/notify, mở lại và confirm; Resident thấy consent qua realtime; Staff bị chặn trước consent và được xử lý sau consent; former assignment bị thu hồi. Invoice-ready/payment/feedback mới được nối bằng HTTP integration, không tuyên bố đã thao tác tất cả trên Chrome.

## D. Bugs và regression

| Hạng mục | Nguyên nhân / thay đổi | RED/GREEN và kết quả |
|---|---|---|
| Chưa có một test nối consent tới payment/feedback | Khoảng trống acceptance coverage, không phải lỗi implementation mới | Thêm partial Acceptance.cs, hai nhánh PASS focused rồi nằm trong full Integration; focused không cộng thêm |
| UI của Manager review/Resident consent chưa đủ ma trận đại diện | Mở rộng harness notification-service-requests.mjs có opt-in `PROPFLOW_FINAL_ACCEPTANCE_UI=1` | Kết quả Chrome cuối ghi tại E; không thay expectation của nghiệp vụ |
| Escape/cancel đóng dialog nhưng focus về body | Blazor tháo modal khỏi DOM trước callback JS disposal; renderer cố focus vào nút Hủy đã disconnected | [JS cleanup](../../src/PropFlow.Web.Client/Shared/Feedback/PropFlowConfirmationDialog.razor.js) thêm MutationObserver cleanup/focus idempotent; `.razor` đổi version URL module. [RED](../../artifacts/acceptance/dialog-detach-red.log) → [GREEN](../../artifacts/acceptance/dialog-detach-green.log); Chrome ứng dụng và full regression cuối sau sửa đều PASS |
| Selector kiểm Register trong harness bị lỗi cú pháp | Escape dấu nháy trong chuỗi Runtime.evaluate sai; không phải lỗi form của ứng dụng | Dùng JSON.stringify cho selector, giữ nguyên assertion target=_blank và draft. Log `chrome-register-harness-first.log` được giữ; Chrome cuối PASS |

Production delta của lượt này chỉ nằm ở shared confirmation dialog JS và version URL import. Các sửa production/snapshot của lượt trước vẫn nằm trong working tree và được giữ nguyên; báo cáo này không nhận chúng là bug mới. Không sửa CSS vì font fallback offline, không nới authorization/database guards, không đổi readiness barriers, pg_ctl launcher hoặc provider settings production để đạt PASS. Probe focus/call-stack chỉ nằm trong harness tạm và đã được gỡ; log RED vẫn được giữ trong artifacts/acceptance.

## E. Final technical baseline

| Suite cuối, sau sửa dialog | PASS | FAIL | SKIP | Artifact |
|---|---:|---:|---:|---|
| Unit/component | 1.080 | 0 | 0 | acceptance-final-unit.trx |
| Architecture | 64 | 0 | 0 | acceptance-final-architecture.trx |
| Integration | 806 | 0 | 0 | acceptance-final-integration.trx |
| BillingCycle | 154 | 0 | 0 | acceptance-final-billing.trx |
| JavaScript | 50 | 0 | 0 | acceptance-final-javascript.log |
| **Tổng phân biệt** | **2.154** | **0** | **0** | [Regression summary](../../artifacts/acceptance/regression-summary.json) |

Các TRX/log mang prefix trên nằm trong artifacts/authz. Lượt sạch này kết thúc 22:16:32, sau bản sửa dialog. Lượt PASS trước sửa và các focused/retry/RED được giữ riêng, không cộng thêm. 176/176 ca PF-AUTHZ-04 đã nằm trong 806 Integration, không cộng lần nữa. 139 mappings và 167 tên evidence chính/bổ sung đều khớp PASS mới; cả 176 tên ca baseline vẫn hiện diện. [PF validation](../../artifacts/acceptance/pf-validation.json).

143/143 applicable HTTP mappings có exact PASS duy nhất từ bốn TRX mới; 116 operations N/A, không còn partial/not verified trong ma trận authorization. Không dùng 111 evidence cũ để lấp lượt cuối. [Mutation coverage](../../artifacts/acceptance/mutation-coverage.json). Đây là validation evidence, không audit lại toàn bộ operation/matrix.

Đóng băng 1.476 file đầu vào và kiểm SHA-256 sau regression: 0 thay đổi. [Frozen inputs](../../artifacts/acceptance/final-frozen-inputs.json). Solution/Billing dependency builds đều 0 warning/0 error; API/Web Release publish đạt. Web.Client của Billing build mới lúc 22:05 sau source dialog cuối; test DLL không đổi được phép giữ timestamp nếu nội dung test không đổi, dependency mới được kiểm riêng. [Dependency proof](../../artifacts/acceptance/billing-final-dependency-proof.json) · [Publish manifest](../../artifacts/acceptance/publish-manifest.json). Các warning RequestSizeLimitFilter của TestServer vẫn giữ trong logs, không suppress để báo sạch.

Chrome cuối chạy 22:17:12–22:21:46, sau lượt regression cuối. **66 tổ hợp** của Manager dialog, Resident consent, năm role và bốn public routes ở 390/768/1440 light/dark đạt; kèm Escape/focus/cancel/reopen, finance counters khớp scoped API, FAQ tìm không dấu, Register link mở tab riêng và giữ draft. Chụp sau hiệu ứng đổi màu 700 ms, cuộn tới consent; ảnh không dùng làm chứng minh workflow chưa thao tác. Đã rà trực quan dialog/consent mobile, finance desktop và public page; môi trường offline có Material Symbols fallback ở một số ảnh, không sửa CSS để che việc tải font ngoài. Không tuyên bố đã kiểm mọi route/state/contrast hoặc nghiệm thu font CDN trên hosting thật. [Chrome checks](../../artifacts/authz/acceptance-notification-service-requests.json) · [Status](../../artifacts/authz/acceptance-chrome-status.json).

Chrome STAFF session cùng origin PASS: storage tampering không mở Admin, logout 204 đưa hai tab về login, bearer cũ 401. Proxy HTTPS smoke có WebSocket, Long Polling authenticated invocation và revoked poll 401; restart vẫn active 200/revoked 401/refresh 200. Không dùng số đo loopback nhỏ làm cam kết tải production. [Session](../../artifacts/authz/acceptance-hardening-12-browser-proxy-session.json) · [WS/LP](../../artifacts/authz/acceptance-hardening-12-proxy-load-1.json) · [Restart](../../artifacts/authz/acceptance-hardening-12-proxy-restart.json). Runtime nạp vẫn 8.0.33 theo [module evidence](../../artifacts/authz/acceptance-final-loaded-runtimes.json).

Cleanup PASS lúc 22:22:16: dừng postmaster sở hữu PID 4076, xóa đúng TEMP cluster có marker; các cổng 45488/47101/47201/47301 được giải phóng. 0 runtime/profile/key/certificate/synthetic asset còn lại; 14 database được bảo vệ vẫn có, 80 core/84 gồm AiChatbot khớp; không dừng cluster ứng dụng. Backup ngoài Git giữ nguyên hash. [Cleanup](../../artifacts/acceptance/cleanup.json) · [Protected DBs/table inventory](../../artifacts/acceptance/protected-after.json).

- Git HEAD/source delta: [source-delta.json](../../artifacts/acceptance/source-delta.json). Release giữ SDK 8.0.314, net8.0, runtime 8.0.33, EF/Npgsql và model hiện có; không upgrade dependency.
- Inventory không có delta API/route: 70 frontend routes, 259 HTTP operations, 143 applicable và 116 N/A. Full MVC inventory test đối chiếu exact route/method; mutation mappings dùng exact PASS từ các TRX mới.
- SignalR: 7 paths/6 hub types; 139 applicable, 211 N/A có lý do. Exact primary/additional evidence được đối chiếu lại từ full Integration; không chạy một audit matrix trùng lặp.
- Build solution, BillingCycle dependencies, API/Web Release publish và JS syntax/diff checks có logs/manifest riêng. Incremental publish có thể giữ timestamp DLL không đổi vì production source không đổi; dựa trên lệnh thành công, source hash và manifest, không ép timestamp mới.
- Kiểm public WASM appsettings theo key: không có password/secret/private key/API key/connection string/OTP key; chỉ là inspection các file cấu hình công khai, không chứng minh secret store của hosting chưa tồn tại. [Inspection](../../artifacts/acceptance/public-configuration-inspection.json).
- Main DB chỉ đọc: 14 contexts/98 migrations, pending=0, model drift=0; không migrate/restore lại. [Status](../../artifacts/acceptance/application-status.json). 80 core/84 gồm AiChatbot và dữ liệu/recovery kế thừa theo [Service Request Review](propflow-post-pull-service-request-migration-report.md).
- Backup trước migration vẫn ngoài Git, SHA-256 khớp `38A7896C9866DF251A29EF896F4978EC1FD6F102BAB26FAA8745ABDBC967D399`; reuse restore/upgrade/fresh evidence đã kiểm, không đưa backup ứng dụng làm automated fixture. [Consistency](../../artifacts/acceptance/backup-consistency.json).
- PostgreSQL tests dùng loopback port riêng 45488, TEMP data directory có ownership marker, dedicated connection/effective configuration và TestDatabaseSafety. Baseline test root được tạo từ fixture EF mới chỉ chứa dữ liệu tổng hợp. PostgreSQL suites và Chrome tuần tự.

## F. Readiness classification

| Môi trường | Trạng thái | Evidence / điều kiện |
|---|---|---|
| Local development | PASS trong phạm vi đã triển khai | Full regression, local DB consistency và cleanup cuối; FE-15.8 chưa nghiệm thu |
| Isolated demo bằng dữ liệu tổng hợp | PASS trong phạm vi đã triển khai | Release loopback, HTTPS, giả lập provider; không đồng nghĩa live acceptance hoặc hoàn tất FE-15.8 |
| Private staging/rehearsal cô lập | CONFIGURATION REQUIRED | Có runbooks và rehearsal local; hosting/network/secrets/keys/DB riêng chưa được cấp hoặc nghiệm thu |
| Public staging | BLOCKED | Runtime release blocker kế thừa + hosting/provider/ops prerequisites |
| Pilot có dữ liệu thật | BLOCKED | Runtime, provider thật, privacy/legal và operational acceptance |
| Production | BLOCKED | Các điều kiện trên, monitoring/backup/recovery/incident response/retention chưa được sign-off môi trường đích |

Runtime 8.0.33 giữ nguyên theo yêu cầu. [Runtime assessment](propflow-runtime-security-risk-assessment.md) và [deployment README](../deployment/README.md) tiếp tục là release gate; lượt này không audit advisory lại hoặc nâng framework. HTTPS/firewall/reverse proxy không thay thế security patch. Chưa có hosting public nào được triển khai.

| Provider | Nghiệm thu local | Phần còn mở trước môi trường thật |
|---|---|---|
| SMTP | LOCAL VERIFIED: fake OTP/recovery/email change/invoice/reminder, queue/recipient snapshots | CONFIGURATION REQUIRED; LIVE VERIFICATION REQUIRED: delivery/retry/bounce và độ bền queue qua restart |
| Cloudinary | LOCAL VERIFIED: fake private storage, access/reassignment và compensation | LIVE VERIFICATION REQUIRED: legacy public inventory/migration, authenticated original/derived/CDN, upload failure/orphan reconciliation |
| SePay/VietQR | LOCAL VERIFIED: fake OAuth/webhook/reconciliation/payment idempotency | CONFIGURATION REQUIRED; LIVE VERIFICATION REQUIRED: sandbox/live account, signed callbacks, reconciliation |
| Gemini | LOCAL VERIFIED: fake classification/recommendation/RAG/Insights/facility/TTS, redaction/quota/fallback | LIVE VERIFICATION REQUIRED: model/project configuration, cost/quota, privacy/retention và audio thật |
| Open-Meteo | LOCAL VERIFIED: fake HTTP/cache/fallback | LIVE VERIFICATION REQUIRED: connectivity và behavior môi trường đích |
| STUN/TURN | LOCAL VERIFIED trong phạm vi signaling/media fixture đã có | CONFIGURATION REQUIRED; LIVE VERIFICATION REQUIRED: relay giữa hai mạng Internet, TTL/credentials và media lifecycle |

## G. Remaining work

1. Không còn blocker hồi quy local đã biết trong phạm vi đã kiểm. Chủ dự án nghiệm thu báo cáo; các giới hạn đại diện/provider/font offline đã ghi rõ, không chuyển thành cam kết toàn bộ SRS.
2. Chủ dự án chốt FE-15.8: phê duyệt phạm vi/contract hoặc quyết định deferred/N/A rõ ràng; không tự triển khai settings đề xuất.
3. Quyết định xử lý runtime/lifecycle trước public staging/pilot/production; sau quyết định cần hồi quy trên runtime/hosting đích.
4. Cấp hosting/OS/domain/tài nguyên, PostgreSQL, key ring bền vững/RSA/OTP và secrets theo môi trường; kiểm HTTPS/proxy/CORS/cookie/CSRF/WS/LP trên topology đó.
5. Nghiệm thu provider thật, Cloudinary legacy/CDN/orphan, SMTP delivery, SePay, AI privacy/quota/retention và TURN hai mạng; không lấy fake tests làm bằng chứng live.
6. Privacy/legal approval và vận hành: monitoring/alerting, backup/recovery có người giữ quyền, retention/incident response/rollback. Đây là release prerequisites, không phải yêu cầu thêm feature trong đợt local.

Giữ nguyên [guided deployment plan](../deployment/guided-deployment-plan.md) và runbooks. Khi chủ dự án yêu cầu deploy, hướng dẫn từng bước bằng tiếng Việt, xác minh từng bước trên nhà cung cấp được chọn; không tự mua hosting hoặc chuyển sang triển khai từ lượt nghiệm thu này.
