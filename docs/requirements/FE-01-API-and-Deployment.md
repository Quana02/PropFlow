# FE-01 API and deployment

Implementation baseline: [approved interpretations](FE-01-Implementation-Decisions.md). This document describes the implementation, not a replacement SRS. Missing consolidated SRS/UC documents remain unresolved.

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

`ChallengeId == Guid.Empty` on registration means pending eligibility; no OTP was issued. Recovery uses an opaque nonempty challenge even for unknown/ineligible accounts. Registration success does not authenticate. Activation links the existing Resident, assigns RESIDENT through Administration, verifies email, and activates the account in one database transaction. The client cannot supply resident/apartment IDs or privileged roles.

400 = invalid input/challenge/proof/CSRF; 401 = invalid credentials/session or a bearer token whose account, role or effective permissions are no longer current; 403 = an authenticated/current identity lacking permission for an operation, or an account that cannot receive an access grant during login/refresh; 409 = uniqueness/eligibility conflict; 429 = rate/resend limit; 500 = safe generic error. An absent route returns 404. Client distinguishes network and timeout failures from HTTP failures.

## Session behavior

- RS256 access JWT: sub, jti, name, role, permission; default 15 minutes, memory only in WASM.
- Refresh token: random 256 bits, database SHA-256 hash only, cookie `__Secure-PropFlow.Refresh`, Secure, HttpOnly, Path `/api/v1/auth`, SameSite=Strict by default. Seven-day absolute expiry is preserved across rotation.
- CSRF: GET csrf, then X-CSRF-TOKEN header on cookie operations. Use credentials=include. The token is requested under the same Bearer identity as the following operation.
- Browser reload bootstraps through refresh; no localStorage/sessionStorage bearer or password/OTP persistence.
- Direct client routes are public Web-host shell endpoints with prerender disabled. Web routing removes copied server authorization metadata only for client components; their `[Authorize]` attributes remain on the component types for WASM AuthorizeRouteView. Protected API endpoints independently enforce Bearer authentication and account scope. Ten HTTP route tests cover direct navigation without rendering protected data.
- Concurrent 401s within one WASM instance share a refresh and retry at most once. 403 never refreshes. Buffered JSON requests are replayable; large/streaming bodies are not automatically retried.
- Tabs have independent memory/refresh locks. A stale refresh returns 401 without deleting a newer cookie issued concurrently in another tab. The losing tab may require login; cross-tab coordination is not implemented.
- Logout clears local auth immediately and revokes the current rotation chain. Other device sessions remain. If server logout fails, the login page offers retry and says server revocation is unconfirmed.
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

Real eligible Resident records and current ResidentApartment rows must be managed through FE-02; FE-01 never creates them for onboarding. No bootstrap user/password or privileged self-registration is supplied.

## Operations and known verification limits

- SMTP uses TLS, an in-memory queue bounded at 200 messages, up to three attempts, and skips expired messages. It is not durable across process restart. A full queue or exhausted retry is logged without recipient/code/body; the user may request a new code after cooldown. Production delivery has not been verified without SMTP credentials.
- Authentication security events contain action, HTTP status, account ID when available and trace ID, never request bodies/passwords/OTP/tokens. The deployment logging sink must enforce the approved 30-day retention and access controls; this repository does not provision that sink or its retention policy.
- Isolated integration tests capture outbound email in the test host only. Production always uses SMTP; no mock login, OTP, token or role is used in product code.
- `/resident` remains an existing business UI prototype outside FE-01. Its account identity, profile navigation and logout use real auth and the route requires RESIDENT; unrelated sample billing/apartment widgets are not presented as completed FE-02/financial functionality.
- Browser automation was blocked by request-header policy loading failures and timeouts in this environment. Read-only checks found the local development certificate valid and present in CurrentUser Trusted Root. Successful .NET tests establish API CORS response behavior for an allowed and a denied origin, but not end-to-end browser cookie/CORS behavior, rendered responsiveness, or real SMTP delivery. Those checks remain required against the configured deployment before release.

## Reproducible checks

```powershell
dotnet build PropFlow.sln
dotnet test tests/PropFlow.UnitTests
dotnet test tests/PropFlow.ArchitectureTests
dotnet test tests/PropFlow.IntegrationTests --filter FullyQualifiedName~AuthenticationFlowTests
dotnet test tests/PropFlow.IntegrationTests --filter FullyQualifiedName~WebShellRouteTests
```

AuthenticationFlowTests require PostgreSQL and CREATE DATABASE permission. The test connection comes from `ConnectionStrings:PropFlowTestDatabase` in API User Secrets/environment; when that key is absent, the test helper derives the dedicated database name `propflow_test` from `ConnectionStrings:PropFlowDatabase`. It rejects every database name other than `propflow_test`. Each authentication flow then creates a uniquely named `propflow_fe01_test_<guid>`, verifies every context points at it, applies migrations, and drops only that isolated database afterward. Existing DatabaseVerificationTests use the separately prepared full schema in `propflow_test`; they are not part of the isolated FE-01 test command above.
