# Post-pull Service Request review và migration local

Ngày 18/11/2026. Thực hiện trực tiếp project chính. Đã cập nhật database phát triển local an toàn; không commit/push, không deploy và chưa chạy Final Local Acceptance.

## A. Thay đổi sau pull

Reflog xác nhận trước pull là `7547f58808156060cb07a715d496505f9eeda56a` (commit chứa stabilization trước); sau fast-forward là `1906ca7bebba76ac8902942d050240f978ab83cb`. Working tree sạch khi bắt đầu. Review dùng `git diff 7547f58...HEAD`; delta pull gồm **72 files: 6 thêm, 66 sửa, 0 xóa**, 1.881 additions/358 deletions. Các commit nghiệp vụ chính: `92bb3ce` và `23c9829`; merge qua PR119/120. Không revert hoặc ghi đè thay đổi của thành viên khác. [Danh sách chính xác toàn bộ file theo name/status](../../artifacts/authz/srreview-pull-files.json).

Sáu file được pull thêm là migration `RequireManagerReviewForAdditionalCosts.cs` và `.Designer.cs`; `Shared/Feedback/NoticeTone.cs`, `PropFlowNotice.razor`, `PropFlowNotice.razor.css`; `tests/PropFlow.UnitTests/PropFlowNoticeTests.cs`. Các file nghiệp vụ sửa chính được mô tả bên dưới; danh sách 66 file sửa, gồm CSS, được giữ trong inventory delta nêu trên.

- FE-05: assignment mới không thu phí ban đầu; `BaseServiceCost` còn trong DTO để tương thích nhưng khác 0 bị từ chối, giữ phí lịch sử.
- Staff khảo sát và đề xuất phí → `AWAITING_MANAGER`; Manager duyệt → `PENDING`; đúng cư dân chấp thuận → `ACCEPTED` rồi Staff tiếp tục. Từ chối hủy yêu cầu/phân công. Đổi số tiền/lý do tạo proposal version mới, phải duyệt/chấp thuận lại.
- Giữ chốt chặn progress/completion/evidence/nghiệm thu khi chưa consent; giữ AssignmentId/current assignment, private evidence và former-Staff scope.
- Sau nghiệm thu, Billing phát hành một invoice cho yêu cầu có phí; outbox mới `ServiceRequestInvoiceReady` thông báo cư dân. Report-transfer không tự xác nhận payment; feedback vẫn theo khoản tiền đã confirmed.
- API mới: `POST /api/v1/manager/service-requests/{id:guid}/additional-cost/approve`, body `ProposalId`, policy `ServiceRequestsAuthorizationPolicies.Manage`. Không thêm role/permission. API assignment/review/consent cũ giữ route, điều chỉnh hành vi theo contract mới.
- Entity thêm transition `SubmitAdditionalCostForResident`, activity `ADDITIONAL_COST_MANAGER_APPROVED`; outbox thêm review request/invoice ready. Frontend đổi Staff work, Manager cost review/dialog, Resident consent và shared notice/theme. FE-08/09 liên quan invoice/recipient/payment; FE-10/11 giữ AI hỗ trợ; FE-13 giữ transactional delivery và recipient filtering. Không biến Service Request thành Maintenance Task.

Nguồn contract: [FE-05 Additional Cost Consent](../requirements/FE-05-Additional-Cost-Consent.md), FE-05 DOCX, CONTEXT và các backend/frontend rules. Yêu cầu mới ngày 18/11 được ưu tiên so với luồng cũ. Không suy workflow chỉ từ implementation.

### Standards review

Review song song theo skill `code-review`: 0 vi phạm MUST được xác nhận. Một nhận xét heuristic P3 về các switch Title/Tone/Message lặp trong Resident/Manager notice; đây là điểm bảo trì, không phải lỗi nghiệp vụ và không mở rộng task để redesign/extract abstraction.

### Spec review

Các bước Manager review, version/consent, chốt chặn, invoice idempotence và backfill phù hợp bản cập nhật FE-05. Điểm recipient invoice cần xác minh đã được tái hiện bằng regression synthetic và sửa ở phần C; không còn DECISION REQUIRED trong scope này. Không tìm thấy API relink hiện hành để coi đó là một exploit từ UI; regression mô phỏng historical/link drift để bảo toàn invoice recipient snapshot.

Kho chưa có `docs/agents/issue-tracker.md`; nếu cần tích hợp issue tracker cho skill, chạy `/setup-matt-pocock-skills`. Lượt review này dùng spec trong repo, diff, reflog và requirements đã xác định; không bị chặn bởi cấu hình tracker.

## B. Migration status và database local

| DbContext | Model/snapshot | Migration hiện có/mới | Pending trước | Pending sau |
|---|---|---|---:|---:|
| PropertyAssets | Khớp | 10 hiện có | 0 | 0 |
| Apartments | Khớp | 7 hiện có | 0 | 0 |
| Administration | Khớp | 6 hiện có | 0 | 0 |
| Authentication | Khớp | 9 hiện có | 0 | 0 |
| Residents | Snapshot thiếu metadata của migration đã có; đã đồng bộ | 7 hiện có, **không tạo mới** | 0 | 0 |
| ServiceRequests | Khớp migration pull | `20261118105731_RequireManagerReviewForAdditionalCosts` | 1 | 0 |
| Maintenance | Khớp | 7 hiện có | 0 | 0 |
| Complaints | Khớp | 2 hiện có | 0 | 0 |
| Billing | Khớp | 12 hiện có | 0 | 0 |
| Payments | Khớp | 11 hiện có | 0 | 0 |
| AiClassification | Khớp | 2 hiện có | 0 | 0 |
| AiRecommendation | Khớp; migration cũ chưa áp dụng ở local | `20261117135903_AddFacilityWeekendNotices` | 1 | 0 |
| AiChatbot | Khớp | 2 hiện có | 0 | 0 |
| Communication | Khớp | 10 hiện có | 0 | 0 |

**Không tạo migration mới.** Migration ServiceRequests được pull về; migration AI đã tồn tại trước pull nhưng local còn thiếu và code hiện hành cần bảng đó. Chỉ áp dụng hai migration hiện có. Không sửa/xóa migration đã áp dụng, reset history, drop/recreate application database hoặc dùng EnsureCreated/SQL thủ công thay EF migration.

`RequireManagerReviewForAdditionalCosts.Up` thay check constraint, mở rộng trạng thái `AWAITING_MANAGER`, chuyển chỉ quote cũ PENDING của yêu cầu ASSIGNED/IN_PROGRESS chưa có invoice về Manager review; ngưng queued resident notices và tạo Manager outbox. Không drop bảng/cột hoặc đổi kiểu dữ liệu. Down đã được đọc; không chạy downgrade trên application database. Synthetic upgrade test kiểm legacy pending/accepted/issued, phí lịch sử, invoice ID và outbox. Bảng `ai_recommendation.facility_weekend_notice_runs` cùng hai unique indexes là bảng duy nhất thêm vào local; owner AiRecommendation. Ownership mapping được cập nhật.

Residents drift là snapshot lỗi thời: các migration `SeparateHouseholdRoleAndResidencyType` và `AddResidentProfileFields` đã có schema đúng, nhưng snapshot còn `ResidencyRole/residency_role` và thiếu profile fields. Đồng bộ snapshot với model và migration hiện có; không tạo một migration trùng rename/add-column. Regression `ResidentsModelSnapshotTests` RED trước, GREEN sau. Sau sửa, **14/14 DbContext không có pending model changes**; local dotnet-ef vẫn 8.0.11.

### Backup, restore và áp dụng

Đích được xác minh từ cấu hình phát triển hiện tại và PostgreSQL thực tế: **localhost:5432/propflow**, không recovery/staging/production. Không có API/Web người dùng đang chạy được agent dừng. Catalog trước/sau giữ đủ 14 database bảo vệ.

Backup custom-format `pg_dump` được giữ ngoài repo tại:

`C:\Users\Hong Quan\AppData\Local\PropFlow\Backups\propflow-before-service-request-20261118-195945.dump`

Archive 724.269 bytes, SHA256 `38A7896C9866DF251A29EF896F4978EC1FD6F102BAB26FAA8745ABDBC967D399`; ACL chỉ tài khoản hiện tại và SYSTEM. Đã kiểm TOC và restore thành công bằng pg_restore vào fixture có ownership marker trên cluster riêng 127.0.0.1:45488. Hash/count của **83 bảng trước migration** khớp bản đọc ứng dụng, không xuất PII hoặc credentials thô.

Fresh migration và upgrade từ clone backup đều đạt: 14 DbContext, 98 migration, pending/model drift bằng 0. So hash các bảng cũ theo backfill contract; chỉ thêm một bảng AI rỗng. Trước apply local kiểm lại backup hash, dữ liệu không đổi từ backup, migration allowlist, model và writer; EF áp dụng migration với lock timeout hữu hạn. Không ghi SQL schema thủ công.

Local update hoàn tất lúc 13:22:20 UTC. Toàn bộ 98 migration đã applied, pending=0, model changes=0. Local có **80 core business tables**, hoặc **84** khi tính thêm bốn bảng AiChatbot. Local không có quote cũ cần backfill trong lượt này; migration data cases được kiểm bằng synthetic upgrade fixture.

API Release sau migration được khởi động tạm ở chế độ **database read-only**, tắt background workers/providers: kiểm đủ 14 contexts từ DI thật, Swagger 200, anonymous private FE-05 API 401. Không tạo user, login session hoặc business data thật. Listener mở trước khi schema probe hoàn tất ở lượt smoke đầu; harness được sửa để đợi probe, lượt cuối PASS. API/cert/key/cổng smoke đã dọn. Sau smoke, hash **mọi cột/mọi dòng của 84 bảng** khớp clone backup đã upgrade, xác nhận dữ liệu thật không bị smoke thay đổi.

Evidence: `srreview-backup.json`, `srreview-migration-verification.json`, `srreview-app-migrations-before/after.json`, `srreview-local-migration-result.json`, `srreview-local-smoke.json`, `srreview-post-smoke-preservation.json` trong `artifacts/authz`.

## C. Lỗi và bản sửa

| Lỗi | Root cause | File sửa | Regression |
|---|---|---|---|
| Invoice-ready notice có thể đến account khác invoice snapshot khi Resident linkage khác lịch sử | Dispatcher resolve `Residents.UserId` live, trong khi Billing snapshot từ `ServiceRequest.SubmittedByUserId` | `ServiceRequestCostNoticeDispatcher.cs`: lấy chính immutable submitter của request liên kết đúng invoice; không thêm contract hoặc đọc Billing implementation | Synthetic historical/link drift RED → GREEN; dispatch hai lần chỉ một notice tới account đã được Billing snapshot |
| Residents báo pending model changes dù các cột đã có trong migration/database | Snapshot chưa phản ánh các migration household/profile hiện có | `ResidentsDbContextModelSnapshot.cs` | `Existing_household_and_profile_migrations_are_represented_in_the_model_snapshot`: RED → GREEN; fresh/upgrade/local cả 14 contexts hết drift |
| Một Unit test ghim màu nút đóng modal cũ | Pull chuyển `#94a3b8` sang `var(--pf-muted)` theo theme, assertion còn literal cũ | `DropdownUiTests.cs`: dùng token theme hiện hành, giữ checks đóng/modal scroll/date picker | Lượt đầu 165 PASS/1 FAIL; lượt cuối 166/166 PASS |

Thêm `Manager_cost_review_checks_effective_permission_and_commits_one_approval_under_concurrency`: JWT login thật, anonymous/wrong roles/revoked/mất quyền, missing request/stale proposal, actor-body tampering, hai approve đồng thời và replay. Chỉ một approval activity/outbox, chưa tạo invoice/payment. Các denied controls so business/realtime/storage snapshots trước–sau.

Review Standards/Spec không dẫn tới redesign hoặc đổi nghiệp vụ. Các bản sửa khác là manifest/runner/test để mô tả đúng delta được pull.

## D. Regression trên source mới

| Suite/lượt cuối | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Relevant Unit/component | 166 | 0 | 0 |
| Focused Integration FE-05/consent/AI/migration/schema/inventory | 97 | 0 | 0 |
| Service-request report-transfer HTTP bổ sung | 1 | 0 | 0 |
| Relevant BillingCycle/payment | 6 | 0 | 0 |
| Architecture | 64 | 0 | 0 |
| Frontend JavaScript | 50 | 0 | 0 |
| **Tổng phân biệt đã chạy mới** | **384** | **0** | **0** |

Không dùng 2.136 PASS cũ làm kết quả chạy mới; không cộng các RED/focused retry trùng tên. Solution build cuối và API/Web Release publish đạt; BillingCycle rebuild dependency source hiện hành, không dùng stale DLL. Các helper restore/build đầu có NU1900 do không truy cập được NuGet vulnerability feed; giữ nguyên warnings/logs, không suppress hoặc nâng dependency. Đây không phải kết quả quét vulnerability mới.

Chrome HTTPS same-origin Release `ServiceRequests` PASS: API assignment invalidates workspace cho former/current Staff; wrong household/former staff không đọc; notice đúng assignee. Staff đề xuất về Manager, resident response/progress trước duyệt bị 409. Manager duyệt thật qua confirmation dialog; sau đó đúng cư dân mới nhận proposal, consent và tiếp tục công việc. Reassignment giữ current-result/evidence actions, former progress/completion trả404. Đã xem ảnh dialog Manager dark và bell Resident light. Harness offline chặn external providers và font CDN; một số Material icon trong ảnh dùng text fallback, không coi ảnh đó là nghiệm thu pixel toàn web. Shared notice/dialog dùng SVG hiển thị rõ.

Proxy restart/valid-vs-revoked credential, WebSocket/Long Polling smoke của runner đạt. Không chạy lại toàn bộ 139 tổ hợp hoặc 105 timing samples; session architecture/filter không thay đổi. Existing FE-05 HTTP tests giữ private evidence, consent, invoice one-to-one, payment/feedback và resource authorization. AI dùng existing fake providers; không gọi Gemini/SMTP/SePay/Cloudinary live.

Lượt focused đầu có 40 PASS/57 FAIL vì database gốc **test** mới chưa bootstrap schema (các isolated business fixtures đã pass). Đã giữ artifacts `srreview-integration-empty-root-first.*`, restore baseline từ fixture fresh đã EF-migrate vào đúng `propflow_test` của cluster sở hữu, rồi cùng bộ test đạt 97/97. Không sửa expected schema để che FAIL. Sandbox abort testhost 90 giây và các RED được giữ riêng. Không chạy automated tests trên application database.

Artifacts cuối: `srreview-unit.trx`, `srreview-integration.trx`, `srreview-payment-transfer.trx`, `srreview-billing.trx`, `srreview-architecture.trx`, `srreview-javascript.log`, `srreview-chrome-status.json`. [Tổng hợp outcome/hash và kiểm trùng tên](../../artifacts/authz/srreview-results.json).

## E. Baseline chuẩn bị Final Local Acceptance

| Hạng mục | Baseline hiện tại |
|---|---|
| Source HEAD | `1906ca7` + working diff của lượt này; [source manifest SHA256](../../artifacts/authz/srreview-source-snapshot.json), working patch và ZIP có đủ các file đổi/thêm được lưu trong artifacts |
| Frontend routes | 70 |
| HTTP operations | 259, exact keys khớp MVC metadata |
| Applicable authorization | 143 |
| VERIFIED / PARTIAL / NOT VERIFIED / N/A | 143 / 0 / 0 / 116 |
| Evidence mới/kế thừa | 32 operation chạy lại; 111 operation không bị ảnh hưởng giữ baseline evidence có provenance/hash |
| FE-05 applicable mappings | 21/21 có evidence mới; giữ 142 method/route/test mappings, cập nhật hai mô tả workflow và thêm một Manager-approve mapping |
| SignalR | Baseline 139/139 VERIFIED được giữ; relevant Chrome smoke mới PASS, không audit lại matrix |
| DbContexts / migrations | 14 / 98 |
| Business tables | 80 core; 84 gồm AiChatbot |
| Local pending migrations/model changes | 0 / 0 |
| Tests mới trong lượt này | 384 PASS / 0 FAIL / 0 SKIP; targeted regression, không phải full acceptance |

`final-authorization-mutation-coverage.json` đã cập nhật. Mỗi applicable row có `evidenceMode`; manifest ghi current TRX và baseline TRX SHA256. Các baseline rows trùng tên test mới được loại trước khi tổng hợp, không double count hoặc thay FAIL mới bằng PASS cũ. Tất cả applicable service-request operations bắt buộc có tên test PASS từ source hiện tại.

Final Local Acceptance tiếp theo cần full regression trên snapshot hiện tại, xác minh lại phần evidence kế thừa, end-to-end theo role/luồng dịch vụ và các điều kiện provider/runtime/hosting còn mở. 2.136 PASS và 139 matrix là evidence lịch sử, không tự chuyển thành một lần chạy full mới. Không tự chuyển sang Final Local Acceptance ở lượt này.

## F. Cleanup và blockers

Cleanup hoàn tất lúc 13:42:06 UTC: drop đúng restore/fresh fixtures qua ownership guard, dừng postmaster PID 9340, xóa cluster TEMP đã kiểm marker/path. Các cổng **45488/47101/47201/47301/47401** đã giải phóng; **0** artifact runtime processes, browser profiles, key directories, certificate files và synthetic assets còn lại. Backup local phía trên **được giữ lại** với SHA256/ACL đã ghi; không đưa vào Git. Application database và đủ **14 database được bảo vệ** tiếp tục tồn tại; không dừng cluster ứng dụng. Evidence: `srreview-cleanup.json`, `srreview-protected-after.json`.

Không còn migration, model, focused regression hoặc authorization-delta blocker trong scope này. Chỉ các điều kiện đã tách riêng tiếp tục mở: full Final Local Acceptance chưa chạy; live provider/TURN/Internet chưa kiểm; runtime release blocker từ audit trước không được giải quyết bằng việc giữ runtime8.0.33 theo yêu cầu. Không tuyên bố production-ready.

Giữ C#12/net8.0, SDK8.0.314, .NET/ASP.NET Core8.0.33, EF8.0.11, Npgsql EF8.0.11/driver8.0.6, local dotnet-ef8.0.11, PostgreSQL/Gemini model hiện hành, fixed RBAC và business contracts đã duyệt. Không thêm feature, dependency, migration mới hoặc redesign UI.
