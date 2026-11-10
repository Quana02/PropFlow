# FE-01 API and deployment

Tài liệu hợp nhất quyết định triển khai, API và vận hành FE-01; cập nhật theo lời làm rõ nghiệp vụ ngày 03/11/2026. Đây là bổ sung cho đặc tả Word, không phải SRS thay thế. Xem [mục lục và giới hạn nghiệm thu](README.md).

## Quyết định nghiệp vụ và bảo mật

- Chỉ cư dân tự đăng ký; không chọn role hoặc căn hộ. Username, tên hiển thị, email, mật khẩu, điện thoại, loại/số giấy tờ là bắt buộc.
- Đủ điều kiện khi là **chủ sở hữu hiện hành**, hoặc **chủ hộ TENANT hiện hành tại căn hộ không có chủ sở hữu hiện hành** (thuê của đơn vị vận hành). Chủ hộ thuê căn có owner khác, chủ hộ AUTHORIZED_OCCUPANT và thành viên hộ không sở hữu không đủ điều kiện. Ownership cần `StartDate <= today`, `EndDate == null`; quan hệ tương lai hoặc đã hết hạn không đủ điều kiện.
- Resident phải ACTIVE, chưa liên kết tài khoản và khớp cùng bốn giá trị đã chuẩn hóa trên một hồ sơ. Không khớp, không đủ điều kiện hoặc kết quả mơ hồ dùng chung phản hồi công khai; không tạo account/OTP.
- OTP gắn fingerprint hồ sơ; khi kích hoạt phải kiểm tra lại toàn bộ điều kiện. FE-01 chỉ liên kết account với Resident; không tạo residency/ownership hoặc cấp quyền xem PII/căn hộ ngoài business scope.
- Xác thực tiêu thụ `Residents.Contracts`; Residents có thể dùng public contract của Apartments để kiểm tra ownership. Không truy cập DbContext/domain của module khác.
- Mật khẩu 12–128 ký tự, không trim/normalize, hash bằng PasswordHasher. Mặc định khóa 15 phút sau 5 lần sai; rate limit login 10/phút, registration/recovery 5/15 phút.
- Không có Remember Me. Token chỉ giữ trong bộ nhớ WASM; không lưu password/OTP/token vào browser storage. Route sau đăng nhập theo role và safe authorized return URL.
- Hồ sơ tự sửa gồm tên hiển thị và điện thoại. Username, role, status và email chỉ đọc trong form cập nhật hồ sơ. ADMIN, MANAGER, STAFF và ACCOUNTANT tự đổi email qua luồng riêng. RESIDENT liên hệ Ban quản lý để khởi tạo yêu cầu, rồi tự xác minh trong Resident Portal. FE-01 giữ nguyên hồ sơ cư dân và quan hệ cư trú.
- ADMIN quản lý tên hiển thị và số điện thoại của tài khoản Ban quản lý qua API và quyền FE-15.1. [Hợp đồng quản trị hồ sơ](FE-15-Administration.md#fe-151--xem-và-chỉnh-sửa-hồ-sơ-tài-khoản-ban-quản-lý)
- Security event có baseline retention 30 ngày tại log sink; không lưu secret hoặc payload nhạy cảm. Quy tắc này độc lập với lịch sử nghiệp vụ của [FE-15](FE-15-Administration.md).
- Giữ quan hệ DBML đã duyệt; corrective migration phải báo lỗi khi dữ liệu bất nhất, không tự xóa hoặc dựng dữ liệu để chạy qua.

## API contract

All paths below are relative to `/api/v1/auth`. Request/response DTOs live in `Authentication.Contracts/AuthContracts.cs`. Errors use JSON ProblemDetails with `code`, Vietnamese `title`, optional validation `errors`, and `traceId`. No refresh token is returned in JSON.

| Method/path | Request | Success | Authorization |
|---|---|---|---|
| GET csrf | None | 200 CsrfResponse | Anonymous; sets secure HttpOnly antiforgery cookie |
| POST register | RegisterRequest | 200 ChallengeResponse | Public, rate limited |
| POST registration/resend | ResumeRegistrationRequest | 200 ChallengeResponse | Username/password of PENDING account; rate limited |
| POST registration/verify | VerifyChallengeRequest | 204 | Challenge + OTP; rate limited |
| POST login | LoginRequest | 200 SessionResponse | Username/password + CSRF |
| POST refresh | None | 200 SessionResponse | Refresh cookie + CSRF |
| POST logout | None | 204 | Refresh cookie + CSRF; idempotent |
| POST password/forgot | ForgotPasswordRequest | 200 ChallengeResponse | Public, neutral response |
| POST password/verify | VerifyChallengeRequest | 200 ResetProofResponse | Recovery challenge + OTP |
| POST password/reset | ResetPasswordRequest | 204 | One-use reset proof + CSRF |
| POST password/change | ChangePasswordRequest | 204 | Bearer, current password + CSRF |
| GET me | None | 200 AccountResponse | Bearer; current account only |
| PUT me | UpdateProfileRequest | 200 AccountResponse | Bearer; current account only |
| POST email/change | ChangeEmailRequest | 200 ChallengeResponse | Bearer ADMIN/MANAGER/STAFF/ACCOUNTANT, current password, CSRF, auth-login limit |
| POST email/verify | VerifyEmailChangeRequest | 204 | Bearer ADMIN/MANAGER/STAFF/ACCOUNTANT, own challenge, CSRF, auth-login limit |
| GET resident-accounts | search, pageIndex, pageSize | 200 ResidentAccountPage | Bearer MANAGER + READ_RESIDENT_ACCOUNTS |
| POST resident-accounts/{residentId}/email-change | RequestResidentEmailChange | 204 | Bearer MANAGER + REQUEST_RESIDENT_EMAIL_CHANGE, CSRF, auth-login limit |
| GET resident-email-change | — | 200 ResidentEmailChangePending | Bearer RESIDENT, own pending request or null challengeId |
| POST resident-email-change/verify | VerifyResidentEmailChange | 204 | Bearer RESIDENT, own manager-issued challenge, current password, CSRF, auth-login limit |

`RegisterRequest` requires username, display name, email, password, phone number, identity type and identity number. Resident matching uses exactly four normalized values on the same Resident row: email (`trim + lowercase`), phone (canonical FE-02 representation), identity type (`CCCD` or `CMND`) and digits-only identity number (`CCCD`: 12 digits; `CMND`: 9 or 12 digits). Phone is required but is not a globally unique Resident identifier and is never used as a standalone lookup key.

`ChallengeId == Guid.Empty` means the generic registration mismatch response and no account or OTP was created. Eligibility requires exactly one matching Resident that is `ACTIVE`, has `UserId == null`, and has either an active HOUSEHOLD_HEAD residency or a current ownership (user decision 2026-11-03). A non-owning household head qualifies only with an active TENANT residency in an apartment with no current owner (a rental from the operator). A tenant head in an apartment owned by another person does not qualify. A household member without current ownership does not qualify; historical residency/ownership alone does not qualify. Public mismatch never reveals which field failed, whether the Resident exists, whether an account is already linked, or whether the Resident is inactive.

The email OTP challenge is bound to the matched Resident fingerprint. Activation revalidates Resident existence, `ACTIVE` status, unlinked state, a qualifying current relationship, and the same registration fingerprint before linking. Registration success does not authenticate. Activation links the existing Resident, assigns RESIDENT through Administration, verifies email, and activates the account in one database transaction. The client cannot supply resident/apartment IDs or privileged roles.

400 = invalid input/challenge/proof/CSRF; 401 = invalid credentials/session or a bearer token whose account, role or effective permissions are no longer current; 403 = an authenticated/current identity lacking permission for an operation, or an account that cannot receive an access grant during login/refresh; 409 = uniqueness/eligibility conflict; 429 = rate/resend limit; 500 = safe generic error. An absent route returns 404. Client distinguishes network and timeout failures from HTTP failures.

## Session behavior

- RS256 access JWT: sub, jti, name, role, permission; default 15 minutes, memory only in WASM.
- Refresh token: random 256 bits, database SHA-256 hash only, cookie `__Secure-PropFlow.Refresh`, Secure, HttpOnly, Path `/api/v1/auth`, SameSite=Strict by default. Seven-day absolute expiry is preserved across rotation.
- CSRF: GET csrf, then X-CSRF-TOKEN header on cookie operations. Use credentials=include. The token is requested under the same Bearer identity as the following operation.
- Browser reload bootstraps through refresh; no localStorage/sessionStorage bearer or password/OTP persistence.
- Direct client routes are public Web-host shell endpoints with prerender disabled. Web routing removes copied server authorization metadata only for client components; their `[Authorize]` attributes remain on the component types for WASM AuthorizeRouteView. Protected API endpoints independently enforce Bearer authentication and account scope. Ten HTTP route tests cover direct navigation without rendering protected data.
- Concurrent 401s within one WASM instance share a refresh and retry at most once. 403 never refreshes. Buffered JSON requests are replayable; large/streaming bodies are not automatically retried.
- Tabs have independent memory/refresh locks. A stale refresh returns 401 without deleting a newer cookie issued concurrently in another tab. The losing tab may require login; cross-tab coordination is not implemented.
- Logout clears local auth immediately and requests revocation of the current rotation chain. Other device sessions remain. HTTP 204 confirms logout and the Login page stays silent. If the outcome is unconfirmed, Login offers retry below the form. The tab defers login, refresh and password reset completion until revocation is confirmed, because login replaces the refresh cookie and password reset clears it. The tab keeps a boolean pending marker in session storage so reloading it does not restore the old cookie. The marker contains no account identifier, access token, refresh cookie or credentials.
- Password change/reset revokes all refresh tokens. Existing JWTs remain valid until normal expiry; this implementation does not claim immediate JWT revocation.
- OTP: six digits, five minutes, five attempts; resend after 60 seconds, five/hour/account; old challenges are cancelled. Recovery proof is separately random, hashed, one-use, and expires after five minutes.

## Required environment configuration

Configure API secrets with environment variables or a deployment secret provider, never in committed appsettings or WASM assets:

| Key | Required value |
|---|---|
| ConnectionStrings__PropFlowDatabase | Target PostgreSQL connection supplied by environment/secret provider; never use production for tests |
| Authentication__Issuer / Authentication__Audience | Stable issuer and intended API audience |
| Authentication__PrivateKeyPem | RSA private PEM, at least 2048 bits |
| Authentication__OtpHashKey | Base64 of at least 32 cryptographically random bytes |
| Authentication__Mail__Host / Port / From | SMTP server supporting STARTTLS; default port 587 |
| Authentication__Mail__Username / Password | Credentials supplied by the mail operator when required |
| Cors__AllowedOrigins__0, etc. | Exact HTTPS frontend origin(s), credentials allowed, never wildcard |
| Authentication__CrossSiteCookie | true only when deployment actually requires cross-site cookies (SameSite=None; Secure) |
| Cloudinary__CloudName / Cloudinary__ApiKey / Cloudinary__ApiSecret | Cloudinary account credentials used by the API for Service Request and Maintenance evidence; never expose these in the Web/WASM client |

AuthPolicy exposes AccessMinutes, RefreshDays, OtpMinutes, OtpAttempts, ResendSeconds, SendsPerHour, LockoutFailures and LockoutMinutes. Rate policy overrides are under `Authentication:RateLimits:{auth-login|auth-entry|auth-session}:{PermitLimit|WindowMinutes}`. Defaults follow the approved interpretations. Changing these values is a policy change, not a UI assumption.

Client `Api:BaseUrl` must be HTTPS. Default `/` requires a same-origin reverse proxy forwarding `/api/v1/*` to PropFlow.Api. The Web host does not proxy or authenticate on the browser's behalf. Development uses the existing configured HTTPS API origin. Client configuration is public; it must never contain secrets. Run the HTTPS launch profiles for both hosts.

The API's committed `appsettings.json` intentionally contains no application database connection or credentials. Startup validates that `ConnectionStrings__PropFlowDatabase` is supplied. Testing has a separate local `propflow_test` configuration; it must never be copied into an application deployment.

Persist ASP.NET Core Data Protection keys in the deployment secret/key storage when running multiple instances or restarting without invalidating antiforgery cookies. Configure a trusted reverse proxy explicitly if using forwarded client IPs; do not trust arbitrary forwarded headers. Current in-process rate limiting is per API instance and sees RemoteIpAddress. Shared/edge rate limiting is an operational prerequisite for scaled deployments.

## Database deployment

No migration is applied by API startup. Back up and inspect existing rows before applying the new constraints; migrations deliberately fail on orphan/overlapping data instead of deleting records.

On an empty database, apply initial migrations in dependency order:

1. Authentication through `20260914101250_CorrectAuthenticationEmailOtpVerification`.
2. PropertyAssets and Apartments initial migrations.
3. Residents through `20260914063807_InitialResidents`.
4. Administration migrations (includes the fixed role catalog).
5. Remaining Authentication migrations: `EnforceOnboardingReferences`, `AddAccountRecoveryProof`.
6. Remaining Residents migration: `EnforceAccountResidencyIntegrity` (requires permission to install btree_gist).

The new cross-schema FKs are migration SQL and scalar IDs only, without cross-module EF navigations. The resident/apartment ACTIVE date-range exclusion constraint is also migration SQL. Do not expect EF model snapshots alone to recreate these raw SQL constraints. The auth reset-proof columns/self-audit FKs are represented in the generated snapshot.

Real eligible Resident records and active `ResidentApartment` rows are managed through FE-02; current ownership is managed through FE-03. FE-01 consumes the Residents public onboarding contract only; Residents may compose qualifying ownership through the Apartments public relationship contract. Authentication does not access `ResidentsDbContext`, `ApartmentsDbContext`, or cross-module entities. FE-01 never creates, converts, or changes either relationship during registration. A deterministic qualifying Apartment ID may be stored as verification audit context, but it is not account authorization scope. Privileged self-registration is not supported; không suy ra credential bootstrap từ tài liệu này.

## Operations and known verification limits

Các kết quả kiểm thử cũ không chứng minh đã nghiệm thu toàn bộ browser/cookie/CORS đa tab hoặc SMTP trên deployment đích. Cần xác minh lại theo môi trường phát hành.

- SMTP uses TLS, an in-memory queue bounded at 200 messages, up to three attempts, and skips expired messages. It is not durable across process restart. A full queue or exhausted retry is logged without recipient/code/body; the user may request a new code after cooldown. Production delivery has not been verified without SMTP credentials.
- Authentication security events contain action, HTTP status, account ID when available and trace ID, never request bodies/passwords/OTP/tokens. The deployment logging sink must enforce the approved 30-day retention and access controls; this repository does not provision that sink or its retention policy.
- Isolated integration tests capture outbound email in the test host only. Production always uses SMTP; no mock login, OTP, token or role is used in product code.

## Reproducible checks

```powershell
dotnet build PropFlow.sln
dotnet test tests/PropFlow.UnitTests
dotnet test tests/PropFlow.ArchitectureTests
dotnet test tests/PropFlow.IntegrationTests --filter FullyQualifiedName~AuthenticationFlowTests
dotnet test tests/PropFlow.IntegrationTests --filter FullyQualifiedName~WebShellRouteTests
```

AuthenticationFlowTests require PostgreSQL and CREATE DATABASE permission. The test connection comes from `ConnectionStrings:PropFlowTestDatabase` in API User Secrets/environment; when that key is absent, the test helper derives the dedicated database name `propflow_test` from `ConnectionStrings:PropFlowDatabase`. It rejects every database name other than `propflow_test`. Each authentication flow then creates a uniquely named `propflow_fe01_test_<guid>`, verifies every context points at it, applies migrations, and drops only that isolated database afterward. Existing DatabaseVerificationTests use the separately prepared full schema in `propflow_test`; they are not part of the isolated FE-01 test command above.

## Tự đổi email tài khoản nội bộ

ADMIN, MANAGER, STAFF và ACCOUNTANT mở “Tài khoản của tôi” và chọn “Đổi email”. Máy chủ xác định tài khoản từ bearer identity. Tài khoản phải ACTIVE, có quyền truy cập hiện hành và không bị khóa tạm thời. `ChangeEmailRequest` gồm `NewEmail` và `CurrentPassword`. Email phải hợp lệ, khác email hiện tại và chưa được tài khoản khác sử dụng sau chuẩn hóa `trim + uppercase`. Máy chủ kiểm tra mật khẩu theo password hasher và chính sách lockout hiện hành, rồi gửi OTP **chỉ đến email mới**. Email tài khoản giữ giá trị cũ trong khi chờ xác minh.

`VerifyEmailChangeRequest` gồm `ChallengeId`, `NewEmail`, `Code`. Challenge gắn với tài khoản hiện tại, mục đích đổi email, email cũ/mới và password hash hiện hành. Mã dùng một lần; 6 số, 5 phút, 5 lần thử; gửi lại sau 60 giây, tối đa 5 lần/giờ/tài khoản theo policy FE-01. Yêu cầu mới hủy challenge cũ; việc quay lại form hoặc rời trang không thay đổi email, challenge chưa dùng tự hết hạn. Gửi lại cần nhập lại mật khẩu hiện tại, không lưu mật khẩu ở browser storage.

Khi xác minh, máy chủ kiểm tra lại trạng thái, quyền truy cập, lockout, ràng buộc challenge và tính duy nhất của email. Giao dịch khóa dòng tài khoản trong lúc kiểm tra và cập nhật để tuần tự hóa với thao tác thay đổi trạng thái. Unique index trên `auth.users.email` chặn xung đột tại commit. Giao dịch cập nhật email đã xác minh, tiêu thụ challenge, hủy mã khôi phục mật khẩu cũ và thu hồi toàn bộ refresh session của tài khoản. Access token cũ bị từ chối vì claim `email` được đối chiếu với email hiện hành trên mỗi request. Phản hồi xóa refresh cookie. Giao diện yêu cầu đăng nhập lại bằng username hiện có và hiển thị email mới từ API sau khi đăng nhập.

Request sai mật khẩu, mã hoặc CSRF trả 400. Bearer thiếu, không hợp lệ hoặc không còn hiện hành trả 401. Mất quyền truy cập giữa lúc xác thực request và kiểm tra trong giao dịch trả 403. Trùng email trả 409, vượt giới hạn gửi trả 429. DTO chỉ chứa trường phục vụ đổi email và xác minh challenge. Bản ghi OTP dùng `auth.privileged_action_challenges` và chỉ lưu hash. Giữ mã purpose lưu trữ `ADMIN_EMAIL_CHANGE` để mã đang chờ và giới hạn gửi tiếp tục có hiệu lực sau triển khai. Nhật ký xác thực chỉ ghi action/status/account/trace, không ghi email/password/OTP/payload.

## Đổi email cư dân theo yêu cầu Ban quản lý

Thay thế quyết định mở tự đổi email cho cả năm role. Hai endpoint tự đổi email trả 403 cho RESIDENT. Challenge tự đổi email cũ của cư dân bị từ chối kể cả với OTP đúng và tự hết hạn. Khi hoàn tất yêu cầu do MANAGER khởi tạo, máy chủ hủy thêm challenge tự đổi email cũ của tài khoản đó. Challenge nội bộ giữ nguyên purpose và thời hạn.

Danh sách phân trang theo hồ sơ FE-02 của deployment hiện hành, tìm theo mã, họ tên hoặc email liên hệ. Tài khoản chỉ được hiển thị khi liên kết có vai trò RESIDENT. Hồ sơ chưa liên kết và liên kết không khả dụng có trạng thái riêng. Chỉ hồ sơ ACTIVE có liên kết hợp lệ với tài khoản RESIDENT đang hoạt động, không bị khóa, mới được gửi yêu cầu.

Yêu cầu đổi email gồm `newEmail`, `reason` (1 mất quyền truy cập hộp thư, 2 sửa địa chỉ khai báo sai, 3 đổi hộp thư sử dụng) và `identityCheckedInPerson` bắt buộc là true. Phản hồi 204 không chứa mã hay ID thử thách. Cư dân đã đăng nhập lấy ID và hạn của yêu cầu riêng, rồi gửi `challengeId`, `newEmail`, `currentPassword` và `code`. OTP giữ thời hạn, số lần thử, giới hạn gửi lại và giới hạn theo giờ của Authentication. Gửi yêu cầu mới sau thời gian chờ sẽ thay thế yêu cầu cũ.

Máy chủ kiểm tra lại quyền MANAGER, trạng thái hai tài khoản, liên kết đang hoạt động và tính duy nhất của email khi xác minh. Giao dịch khóa dòng tài khoản và liên kết, cập nhật email đã xác minh, hủy mã khôi phục và thu hồi refresh session. Cư dân đăng nhập lại bằng username hiện có. `auth.users.email` phục vụ xác minh và khôi phục FE-01. `residents.residents.email` là email liên hệ do MANAGER quản lý theo FE-02 và có thể khác email tài khoản. [Quy trình vận hành](FE-01-Implementation-Decisions.md) · [Ranh giới FE-02](FE-02-Resident-Onboarding-API.md) · [Quyền FE-15](FE-15-Administration.md)

Triển khai sau khi áp dụng migration `20261109154611_AddManagedResidentEmailChallenge` của Authentication và `20261109154621_GrantManagerResidentAccountPermissions` của Administration. Access token MANAGER đã cấp được làm mới để nhận tập quyền hiện hành. Nếu phiên không làm mới được, MANAGER đăng nhập lại.

## Truy vết FE-01

| FE item | Frontend | Application | Kiểm chứng chính |
|---|---|---|---|
| FE-01.1 | Register | Registration.RegisterAsync | Eligible và pending/no-match |
| FE-01.2 | Login, AuthSession/AuthHttpHandler | Sessions.LoginAsync/RefreshAsync | Login, lockout, JWT expiry, rotation, state/concurrency |
| FE-01.3 | Otp, ResumeRegistration | Registration.ActivateAsync/ResumeRegistrationAsync | Activation, resend cancellation, expiry, attempts |
| FE-01.4 | ForgotPassword, Otp, ResetPassword | Recovery | Neutral recovery, one-use proof, revoke |
| FE-01.5 | ChangePassword | OwnAccount.ChangePasswordAsync | Password change, revoked refresh |
| FE-01.6 | Account, ChangeEmail, ResidentAccounts, VerifyResidentEmail | OwnAccount và ManagedResidentEmail | Bốn role nội bộ tự đổi, cư dân xác minh yêu cầu do MANAGER tạo, OTP, uniqueness, thu hồi phiên, giữ riêng email liên hệ |
| FE-01.7 | Account/resident logout, Login retry | Sessions.LogoutAsync | Current-chain revoke, other device survives, local race handling |
