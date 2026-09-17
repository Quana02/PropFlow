# FE-01 IMPLEMENTATION REPORT

Ngày kiểm tra tiếp: 2026-09-17. Đã triển khai FE/BE cho FE-01; chưa xác nhận sẵn sàng phát hành vì còn kiểm tra browser và cấu hình môi trường. Không commit/push; không chạy migration trên database ứng dụng.

## 1. Implemented Use Cases

| Use case | Kết quả |
|---|---|
| FE-01.1 Register Resident Account | Form/DTO/API đăng ký riêng Resident; tài khoản PENDING; không cho chọn role hoặc claim căn hộ |
| FE-01.2 Login | Username/password, hash framework, lockout/rate limit, role/permission từ Administration |
| FE-01.3 Verify Resident Eligibility / Activate Account | Eligibility từ Resident hiện hữu và cư trú hiện hành, email OTP, resend/expiry/attempt; liên kết + role + activation trong transaction |
| FE-01.4 Forgot / Reset Password | Phản hồi trung tính, OTP riêng, proof ngẫu nhiên một lần, đổi password và revoke refresh sessions |
| FE-01.5 Change Password | Bearer/current password, đổi hash, revoke tất cả refresh sessions, yêu cầu đăng nhập lại |
| FE-01.6 View / Update Own Account | GET/PUT me; chỉ sửa tên hiển thị và điện thoại; không đổi username/email/role/status/quan hệ cư trú |
| FE-01.7 Secure Logout | Xóa memory/auth state, revoke chuỗi rotation của phiên hiện tại, xóa cookie; thông báo và retry nếu server chưa xác nhận |

## 2. Files Changed

M = sửa; D = bỏ UI demo đã được thay bằng feature pages; ?? = file mới chưa được git add. Danh sách working tree của task:

- M — PropFlow.sln
- M — docs/database/PropFlow.dbml
- M — docs/database/PropFlow_Database_Ownership_Mapping.md
- M — src/Modules/Administration/PropFlow.Modules.Administration.csproj
- M — src/Modules/Authentication/Domain/Tokens/PasswordResetToken.cs
- M — src/Modules/Authentication/Infrastructure/Persistence/Configurations/PasswordResetTokenConfiguration.cs
- M — src/Modules/Authentication/Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs
- M — src/Modules/Authentication/Infrastructure/Persistence/Migrations/AuthenticationDbContextModelSnapshot.cs
- M — src/Modules/Authentication/PropFlow.Modules.Authentication.csproj
- M — src/Modules/Residents/PropFlow.Modules.Residents.csproj
- M — src/PropFlow.Api/Program.cs
- M — src/PropFlow.Api/appsettings.json
- D — src/PropFlow.Web.Client/Pages/Auth/Login.razor
- D — src/PropFlow.Web.Client/Pages/Auth/Otp.razor
- D — src/PropFlow.Web.Client/Pages/Auth/ResetPass.razor
- M — src/PropFlow.Web.Client/Pages/Resident/Index.razor
- M — src/PropFlow.Web.Client/Program.cs
- M — src/PropFlow.Web.Client/PropFlow.Web.Client.csproj
- M — src/PropFlow.Web.Client/Routes.razor
- M — src/PropFlow.Web.Client/wwwroot/appsettings.json
- M — src/PropFlow.Web.Client/wwwroot/js/landing.js
- M — src/PropFlow.Web.Client/wwwroot/js/resident.js
- M — src/PropFlow.Web/Program.cs
- M — tests/PropFlow.ArchitectureTests/ModuleBoundaryTests.cs
- M — tests/PropFlow.IntegrationTests/PropFlow.IntegrationTests.csproj
- M — tests/PropFlow.IntegrationTests/PropFlowApiFactory.cs
- M — tests/PropFlow.UnitTests/PropFlow.UnitTests.csproj
- ?? — docs/requirements/FE-01-API-and-Deployment.md
- ?? — docs/requirements/FE-01-Implementation-Decisions.md
- ?? — docs/requirements/FE-01-Implementation-Report.md
- ?? — src/Modules/Administration.Contracts/IAccountAccess.cs
- ?? — src/Modules/Administration.Contracts/PropFlow.Modules.Administration.Contracts.csproj
- ?? — src/Modules/Administration/Infrastructure/AccountAccessService.cs
- ?? — src/Modules/Authentication.Contracts/AuthContracts.cs
- ?? — src/Modules/Authentication.Contracts/PropFlow.Modules.Authentication.Contracts.csproj
- ?? — src/Modules/Authentication/Application/AuthPolicy.cs
- ?? — src/Modules/Authentication/Application/AuthPorts.cs
- ?? — src/Modules/Authentication/Application/AuthUseCases.cs
- ?? — src/Modules/Authentication/Application/OwnAccount/OwnAccount.cs
- ?? — src/Modules/Authentication/Application/Recovery/Recovery.cs
- ?? — src/Modules/Authentication/Application/Registration/Registration.cs
- ?? — src/Modules/Authentication/Application/Sessions/Sessions.cs
- ?? — src/Modules/Authentication/Infrastructure/AuthEmailQueue.cs
- ?? — src/Modules/Authentication/Infrastructure/AuthRegistration.cs
- ?? — src/Modules/Authentication/Infrastructure/AuthSecrets.cs
- ?? — src/Modules/Authentication/Infrastructure/AuthSecurityEvents.cs
- ?? — src/Modules/Authentication/Infrastructure/Persistence/AuthStore.cs
- ?? — src/Modules/Authentication/Infrastructure/Persistence/AuthenticationDesignTimeFactory.cs
- ?? — src/Modules/Authentication/Infrastructure/Persistence/Migrations/20260916120000_EnforceOnboardingReferences.cs
- ?? — src/Modules/Authentication/Infrastructure/Persistence/Migrations/20260916135849_AddAccountRecoveryProof.Designer.cs
- ?? — src/Modules/Authentication/Infrastructure/Persistence/Migrations/20260916135849_AddAccountRecoveryProof.cs
- ?? — src/Modules/Authentication/Presentation/AuthController.cs
- ?? — src/Modules/Authentication/Presentation/AuthExceptionHandler.cs
- ?? — src/Modules/Residents.Contracts/IResidentOnboarding.cs
- ?? — src/Modules/Residents.Contracts/PropFlow.Modules.Residents.Contracts.csproj
- ?? — src/Modules/Residents/Infrastructure/Persistence/Migrations/20260916120100_EnforceAccountResidencyIntegrity.cs
- ?? — src/Modules/Residents/Infrastructure/ResidentOnboarding.cs
- ?? — src/PropFlow.Web.Client/Features/Authentication/Components/ApiFeedback.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Components/AuthBootstrap.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Components/AuthLayout.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Components/AuthLayout.razor.css
- ?? — src/PropFlow.Web.Client/Features/Authentication/Components/RequireLogin.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Models/AuthForms.cs
- ?? — src/PropFlow.Web.Client/Features/Authentication/Pages/Account.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Pages/Activated.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Pages/ChangePassword.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Pages/Forbidden.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Pages/ForgotPassword.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Pages/Login.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Pages/Otp.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Pages/Register.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Pages/ResetPassword.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Pages/ResumeRegistration.razor
- ?? — src/PropFlow.Web.Client/Features/Authentication/Services/AuthenticationService.cs
- ?? — src/PropFlow.Web.Client/Features/Authentication/State/OnboardingState.cs
- ?? — src/PropFlow.Web.Client/Features/Authentication/_Imports.razor
- ?? — src/PropFlow.Web.Client/Services/Api/ApiClient.cs
- ?? — src/PropFlow.Web.Client/Services/Authentication/AuthHttpHandler.cs
- ?? — src/PropFlow.Web.Client/Services/Authentication/AuthSession.cs
- ?? — src/PropFlow.Web/WebAssemblyShellHost.cs
- ?? — tests/PropFlow.IntegrationTests/AuthenticationFlowTests.cs
- ?? — tests/PropFlow.IntegrationTests/WebShellRouteTests.cs
- ?? — tests/PropFlow.UnitTests/ClientAuthenticationTests.cs

## 3. API Contracts Used

Xem [API và triển khai](FE-01-API-and-Deployment.md) để biết đầy đủ 13 endpoints, DTO, response/error, cookie/CSRF và prerequisites.

API nằm dưới `/api/v1/auth`: csrf, register, registration/resend, registration/verify, login, refresh, logout, password/forgot, password/verify, password/reset, password/change, GET/PUT me. Ba contract projects giữ boundary Authentication ↔ Residents ↔ Administration; client chỉ dùng DTO contract.

## 4. Existing UI Reused / Refactored

Giữ phong cách màu xanh, card kính, typography và bố cục thích ứng; gom khung auth vào AuthLayout. Form HTML/JS demo cũ được thay bằng EditForm/validation/feature service thật. Xóa mock-success login/forgot/OTP và lưu profile giả. Trang cư dân dùng danh tính thật, điều hướng My Account/logout thật và role guard. Không tuyên bố đã triển khai các widget nghiệp vụ mẫu còn lại của trang cư dân.

## 5. Authentication Architecture

Global Interactive WebAssembly, prerender=false. Access JWT RS256 chỉ ở memory; API dùng Bearer. Refresh token opaque/hash trong DB, cookie Secure+HttpOnly, CSRF, rotation, expiry tuyệt đối. Reload restore qua API; 401 single-flight và retry tối đa một lần; 403 không refresh. Web host chỉ trả shell; component AuthorizeRouteView và API có trách nhiệm bảo vệ riêng.

Password hashing dùng PasswordHasher. OTP HMAC với salt ngẫu nhiên và secret cấu hình. SMTP thật nằm sau IAuthEmail; chỉ test host thay bằng capture email. Không seed fake account/credential/resident/role trong product code.

## 6. Tests Executed

| Kiểm tra | Kết quả |
|---|---|
| PropFlow.UnitTests | 126/126 đạt: bao gồm restore, concurrent refresh, 401/403/429/500, logout race, reset-proof expiry/single-use |
| PropFlow.ArchitectureTests | 61/61 đạt |
| AuthenticationFlowTests | 9/9 đạt trên PostgreSQL tạm: registration/activation, login, cookie/CSRF, refresh/replay/logout scope, reset/change/profile, lockout, resend/expiry/attempt, 401 so với 403, CORS credentials chỉ cho origin được cấu hình |
| WebShellRouteTests | 10/10 đạt: GET trực tiếp các auth/account/resident routes trả 200, có shell, không prerender protected UI |
| Diff whitespace | Không có lỗi whitespace; Git có cảnh báo chuyển LF/CRLF |
| Browser UI / responsive / real browser cookies | Chưa xác nhận: công cụ browser bị lỗi tải request-header policy và timeout; development certificate hợp lệ, nằm trong CurrentUser Trusted Root |
| SMTP delivery thật | Chưa chạy: chưa có cấu hình SMTP môi trường |
| DatabaseVerificationTests cũ | Không chạy trong lượt FE-01; bộ này phụ thuộc full schema riêng của propflow_test |

PostgreSQL tests tự tạo database riêng có tên GUID, xác minh kết nối trước migration rồi xóa đúng database đó. Không sửa database ứng dụng. Test email capture và test fixtures không chạy trong production.

Các lỗi được tìm và sửa trong quá trình kiểm chứng: cấu hình API cần bind sau cấu hình test host; Web shell bị lỗi 500 vì server authorization metadata; stale refresh không được xóa cookie mới vừa rotate ở tab khác. Connection string/mật khẩu mẫu của database ứng dụng đã bỏ khỏi `appsettings.json`; API yêu cầu giá trị từ môi trường/secret provider lúc khởi động.

## 7. Build Result

Build toàn solution bằng .NET 8. Kết quả cuối: 0 warnings, 0 errors. Một lần build trước bị khóa apphost vì Web host kiểm thử còn chạy; đã dừng process và build lại thành công.

## 8. Remaining Blockers

- Cần cấu hình khóa RSA, OTP hash secret, SMTP, HTTPS/CORS/proxy và database deployment theo hướng dẫn; chưa triển khai môi trường ứng dụng.
- Cần browser end-to-end và responsive check khi công cụ browser hoạt động; test HTTP xác nhận CORS response nhưng không chứng minh cookie/CORS thực trong browser. `dotnet dev-certs https --check` xác nhận certificate còn hạn, và kiểm tra read-only certificate store xác nhận thumbprint của certificate trong CurrentUser Trusted Root. Công cụ browser hai lần không tải được request-header policy.
- Cần log sink với retention 30 ngày và quyền truy cập vận hành; code phát security event an toàn nhưng không provision hạ tầng lưu log.
- Email queue ở memory, không durable qua restart; retry tối đa ba lần; có thể cần yêu cầu mã mới nếu queue đầy/gửi thất bại.
- Chưa có PropFlow_15FE.docx và Report3 SRS trong repository để đối chiếu consolidated UC IDs.
- JWT đã phát vẫn có hiệu lực đến expiry; logout/password reset/change revoke refresh sessions theo baseline. Single-flight chỉ trong một WASM instance, không phối hợp nhiều tab.

## 9. Requirement Traceability

| FE item | Frontend | Application | Kiểm chứng chính |
|---|---|---|---|
| FE-01.1 | Register | Registration.RegisterAsync | Eligible và pending/no-match |
| FE-01.2 | Login, AuthSession/AuthHttpHandler | Sessions.LoginAsync/RefreshAsync | Login, lockout, JWT expiry, rotation, state/concurrency |
| FE-01.3 | Otp, ResumeRegistration | Registration.ActivateAsync/ResumeRegistrationAsync | Activation, resend cancellation, expiry, attempts |
| FE-01.4 | ForgotPassword, Otp, ResetPassword | Recovery | Neutral recovery, one-use proof, revoke |
| FE-01.5 | ChangePassword | OwnAccount.ChangePasswordAsync | Password change, revoked refresh |
| FE-01.6 | Account | OwnAccount.GetAccountAsync/UpdateAccountAsync | Own profile, forbidden access, privilege fields ignored |
| FE-01.7 | Account/resident logout, Login retry | Sessions.LogoutAsync | Current-chain revoke, other device survives, local race handling |
