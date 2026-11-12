# Automatic bank reconciliation with SePay

PropFlow supports SePay bank-transfer webhooks for Service Request invoices. The receiver is disabled by default. No bank login credentials belong in PropFlow.

## Live-only payment setup

PropFlow now exposes only live SePay payment setup and bank-transfer reconciliation. Trial setup, simulation endpoints and trial balance overlays have been removed. Existing historical trial tables and migrations remain for audit; their records never reduce an invoice balance or block a real payment. No migration converts trial money into confirmed receipts.

The default connection uses a server-side Live API Token (`Payments:SePay:OAuth:Enabled=false`). OAuth is optional and requires an approved application. Configure a public HTTPS webhook, save the receiving bank with email OTP, then enable automatic confirmation. See [the deployment guide](sepay-live-deployment.md).

## Connect a building owner's SePay account with OAuth (optional flow)

PropFlow models one Current Building per deployment. The OAuth connection belongs to that building, not to the individual Admin who authorized it. Separate condominium deployments have separate databases, token encryption keys and receiving settings. This change does not introduce multi-tenant hosting or a building switcher.

The owner uses **Admin → Thanh toán → Kết nối SePay**, verifies their Admin email OTP, signs in to their own SePay account, consents, and returns to `/admin/sepay/callback`. No owner API token is entered or sent to the browser. The owner then synchronizes accounts, selects a bank, confirms the existing email OTP, and chooses **Thiết lập nhận giao dịch** (also OTP protected). The last step creates/updates an incoming-transfer webhook through SePay. Bank linking itself happens on SePay. Residents do not register with SePay.

SePay currently requires contacting their support for OAuth application approval and credentials:
https://developer.sepay.vn/vi/sepay-oauth2/dang-ky-ung-dung

Configure the application once per deployment (secrets only on the API server):

| User-secret key | Value |
| --- | --- |
| `Payments:SePay:OAuth:Enabled` | `true` (default in appsettings) |
| `Payments:SePay:OAuth:ClientId` | Approved SePay application client ID |
| `Payments:SePay:OAuth:ClientSecret` | Approved SePay application secret |
| `Payments:SePay:OAuth:RedirectUri` | Exact HTTPS **frontend** callback, e.g. `https://app.example.com/admin/sepay/callback` |

From the repository root, store the credentials locally without overwriting existing database/mail/Cloudinary settings:

```powershell
dotnet user-secrets set "Payments:SePay:OAuth:ClientId" "YOUR_APPROVED_CLIENT_ID" --project src/PropFlow.Api/PropFlow.Api.csproj
dotnet user-secrets set "Payments:SePay:OAuth:ClientSecret" "YOUR_APPROVED_CLIENT_SECRET" --project src/PropFlow.Api/PropFlow.Api.csproj
dotnet user-secrets set "Payments:SePay:OAuth:RedirectUri" "https://YOUR_FRONTEND_HOST/admin/sepay/callback" --project src/PropFlow.Api/PropFlow.Api.csproj
```

Replace the placeholders on your machine; do not paste actual secrets into chat. Restart the API and frontend after updating/building; the Connect button is unavailable until the application credentials are configured.

Register that exact redirect with SePay. The local default is `https://localhost:7201/admin/sepay/callback`; use an approved public frontend origin if SePay does not accept a local development redirect. The webhook still targets the public **API** origin (a different setting) plus `/api/v1/payments/webhooks/sepay`.

Request only `bank-account:read webhook:read webhook:write`. OAuth tokens call `https://my.sepay.vn/api/v1`, not the legacy API v2 host. Numeric OAuth bank IDs are translated to opaque connection-bound IDs; the bank directory, amount and account checks remain server-side.

Apply `AddSePayOAuthConnection` before starting the updated API. Set the public API origin and a strong webhook credential from the configuration table below. Prefer HMAC. Set `Payments:SePay:Enabled=true` when ready to receive bank notifications. These are operator settings, not keys the building owner must obtain. The web creates the owner's webhook after their OTP approval. Do not interpret OAuth consent alone as readiness to collect payments.

Authorization state is random, hashed, bound to the authenticated Admin and expires in ten minutes. It is consumed before exchanging the code, including denied consent. Connection changes require email OTP and concurrent callback/refresh/provisioning writes are serialized in PostgreSQL. Tokens are encrypted using ASP.NET Data Protection and never returned by the API. Refresh-token rotation happens on the server; revoked refresh tokens require reconnecting. No shared API-token fallback is allowed while OAuth is enabled. Keep the Data Protection key ring persistent and backed up.

Consent cancellation, failed exchanges and stale callbacks leave the previous connection intact. Reconnecting does not change the receiving bank. The bank must be selected and OTP confirmed again; previous invoice destinations remain immutable. Existing webhooks are retained so older pinned invoices can still be reconciled. Webhook provisioning uses a deterministic destination name to recover after ambiguous network failures; it never modifies unrelated webhook configurations.

After setup, test SePay delivery, verify Admin's latest received timestamp, then test an invoice's complete `PF` memo. A valid connection flag is not proof that a webhook is publicly reachable. No live credentials, bank linking or bank transfers are created by automated tests.

Official flow and API references:
- https://developer.sepay.vn/vi/sepay-oauth2/luong-xac-thuc
- https://developer.sepay.vn/vi/sepay-oauth2/access-token
- https://developer.sepay.vn/vi/sepay-oauth2/tai-khoan-ngan-hang
- https://developer.sepay.vn/vi/sepay-oauth2/api-webhook

## Configuration

Store these values in the API's user-secrets (local development) or environment variables (deployment), never in source control:

| Setting | Value |
| --- | --- |
| `Payments__SePay__ApiToken` | SePay API v2 token authorized to read bank accounts; separate from the webhook API key |
| `Payments__SePay__WebhookPublicBaseUrl` | Your public API origin, e.g. `https://payments.example.com` (HTTPS, no path, credentials or query string) |
| `Payments__SePay__Enabled` | `true` after setup |
| `Payments__SePay__WebhookApiKey` | A random secret of at least 32 characters; configure the same API key in SePay |
| `Payments__SePay__AuthenticationMode` | `API_KEY` (default) or `HMAC` (recommended for live use) |
| `Payments__SePay__WebhookSecret` | In HMAC mode, the shared signing secret (at least 32 characters) configured in SePay |
| `Payments__SePay__Gateway` | Optional legacy bank alias, e.g. `Vietcombank`; new selections use the bank on the pinned destination |
| `Payments__SePay__BankCode` | Bank code corresponding to the optional legacy alias, e.g. `VCB` |

## API-token connection (default; OAuth disabled)

When OAuth is explicitly disabled, the legacy API token is used only by the server, with Bearer authentication to the fixed official endpoint `https://userapi.sepay.vn/v2/bank-accounts`. The client does not receive the token, account numbers, or balances during connection checks. HTTP redirects are disabled so the token cannot be forwarded to a different endpoint. Standard SePay API Access tokens currently have full API access; do not assume granular OAuth scopes apply to them. Never put a bank password in these settings.

- `GET /api/v1/payment-settings/sepay/connection`: reports configuration flags without contacting SePay.
- `POST /api/v1/payment-settings/sepay/connection/check`: checks API access using the first bank-account page. No request body is required. Requires Admin and the existing payment-settings permission; rate-limited.

`ApiCheck.Connected` means SePay accepted the API token and returned a valid account list. It does not mean a bank link is healthy or webhook delivery has been tested. `WebhookConfigurationReady` checks the local enable flag, selected authentication credentials, selected SePay destination (or legacy bank settings) and public HTTPS URL. Public reachability still requires an actual SePay test delivery. Admin also shows the last authenticated receipt time and the number of cases requiring review.

You can configure and test `ApiToken` while leaving `Enabled=false`. API connection checks do not change the receiving account, QR configuration, OTP settings, or enable automatic confirmations.

In local development use the API project's User Secrets, e.g. `dotnet user-secrets set "Payments:SePay:ApiToken" "YOUR_TOKEN" --project src/PropFlow.Api/PropFlow.Api.csproj`. Supply the real token locally, not in chat or source control. In deployment use environment-variable names from the table.

Official API v2 reference: https://developer.sepay.vn/en/sepay-api/v2/tai-khoan-ngan-hang/danh-sach

In SePay, link the receiving bank account already configured by the Admin in PropFlow. Create an incoming-transfer webhook with JSON body, API Key authentication, and URL:

`https://YOUR_PUBLIC_API_HOST/api/v1/payments/webhooks/sepay`

Use HTTPS. For local development, configure your own HTTPS tunnel to the API. Configure payment-code prefix `PF` with 22 following hexadecimal characters; if filtering transactions without codes, ensure SePay recognizes the complete 24-character code. PropFlow also extracts the code directly from transfer content. In HMAC mode, requests must include `X-SePay-Timestamp` and `X-SePay-Signature: sha256=...`, signing `{timestamp}.{raw_body}`; timestamps outside five minutes are rejected. HMAC mode never falls back to API Key authentication.

Official references:
- https://developer.sepay.vn/en/sepay-webhooks/tao-webhook
- https://developer.sepay.vn/en/sepay-webhooks/xac-thuc
- https://developer.sepay.vn/en/sepay-webhooks/tich-hop-webhook

## Step 2: Admin account picker

The Admin payment page now shows connection status, a connection check, account synchronization, a masked account picker, local QR preview and the existing version history. There are no editable account-number or account-holder inputs in this page.

`GET /api/v1/payment-settings/sepay/accounts` fetches all account pages (up to 100 pages) and returns masked account numbers without balances. Accounts that are inactive, unsupported or incomplete cannot be selected. Where SePay omits bank BIN/code, the public VietQR bank directory supplies the mapping.

The preview, OTP challenge and save endpoints under `/api/v1/payment-settings/sepay/accounts` accept an account ID and expected configuration version. The backend fetches the account detail from SePay each time. OTP binds the exact SePay account ID, normalized banking data and version; changes made in SePay between requesting and confirming OTP require a new confirmation. Synchronizing the list never changes the saved receiving account.

This step does not turn on automatic reconciliation or provision a SePay webhook. The status card reports configuration readiness only. Test the account picker with a real API token after linking a bank account in SePay.

## Step 3: OTP binding and account history

New destination versions persist `sepay_account_id`, along with the existing creator, creation timestamp, update and deactivation audit fields. The nullable source ID preserves old destination versions and invoices; historical manual configurations are labeled as legacy in the Admin screen. They are never automatically assigned to a SePay account based only on the last four account digits.

OTP challenges now bind the complete normalized destination snapshot including the SePay ID and configuration version. An OTP for a different SePay ID cannot be used even if bank details happen to be identical. Account details and active status are fetched again on confirmation. A stale configuration version fails before consuming the OTP; the database lock/version check still protects concurrent writes. Any OTP requested before this update should be discarded and requested again.

Legacy manual preview/save and UPSERT challenge endpoints return HTTP 409 `sepay_selection_required`. The legacy DEACTIVATE challenge remains available because it binds to the existing immutable destination ID and version, and supports retiring a legacy configuration. Deactivation itself still requires valid Admin email OTP.

Run the `AddSePayDestinationIdentity` Payments migration before starting the updated API.

## Payment behavior

Open a newly generated QR after updating the application. Its transfer memo is a unique `PF` code tied to the invoice and pinned bank destination. Old downloaded QR images containing truncated invoice numbers cannot be auto-matched; use a freshly opened QR.

Incoming transfers must match the full code, configured bank, and complete receiving account number. Only verified funds create confirmed receipts. Partial transfers reduce the outstanding balance; overpayments are left for accountant review. Replays use the SePay transaction ID as an idempotency key, sharing the invoice lock used for manual accountant confirmations. Superseded resident claims are closed with an audit reason after automatic collection.

Bill settlement is derived from confirmed receipts: `UNPAID`, `PARTIAL`, or `PAID`. The document itself remains `ISSUED` to preserve invoice lifecycle semantics. Open resident payment views check for balance changes every five seconds; rating unlocks only when the outstanding amount reaches zero.

Authenticated webhooks are saved to a durable encrypted inbox before returning `{"success":true}`. A worker claims each transaction with an optimistic-concurrency revision and a two-minute lease, retries transient processing failures (five attempts), and resumes expired claims after a crash. Provider transaction IDs have a unique database index; retries with different transaction data return HTTP 409 and never overwrite the original receipt. Payment collection uses the existing per-invoice database lock and idempotency record. Superseded resident reports are closed in the same database transaction as the verified receipt.

Unmatched codes, wrong bank/account, overpayments and exhausted retries appear at `/finance/payments/bank-transfers`. Only Accountant users with `finance.manage` may view or resolve them. An Accountant can select an issued invoice with pinned bank details, allocate up to the received amount and outstanding invoice balance, and supply a mandatory note. The worker rechecks bank/account and remaining balance before collecting. Any excess remains in review. After processing the excess outside PropFlow (e.g. refund), the Accountant records the result and closes the case. Closing a case does not initiate a refund or change already confirmed receipts. All review notes/actors/timestamps are retained in an append-only audit history on the transfer record.

Disabling new webhook reception does not disable manual accounting; explicitly assigned Accountant decisions on previously authenticated inbox records can still be processed. Bank account numbers in lists are masked; full provider payloads are encrypted. Persist and back up ASP.NET Data Protection keys, and share the same key ring across API/worker instances. Losing these keys prevents decrypting saved accounts and inbox payloads.

Resident payment cards show unpaid/partial/paid balances, regenerate QR for the remainder, and poll every five seconds without clearing rating drafts. In automatic mode no resident report is required; a fallback form remains available for delayed transfers. Reporting a transfer alone never unlocks feedback.

For a real end-to-end test, first verify wrong API keys are rejected, then use SePay's test delivery against a test invoice. Confirm duplicate delivery does not increase paid totals twice. Finally, use a small real transfer only after the account owner authorizes it. This implementation does not create accounts, link bank accounts, or initiate transfers.

Automated receipts use dedicated non-login audit identity `7bba1639-1f4c-46c3-9716-3792634925ca`.

## Verification and deployment

1. Apply all Payments, Billing, and Service Requests migrations, including `AddSePayTransferInbox` and `AddIndexedTransferCodes`.
2. Configure the API token and public HTTPS origin. Select the linked bank account in Admin and confirm the email OTP.
3. Configure SePay's incoming-transfer webhook with authentication matching PropFlow. In HMAC mode configure `WebhookSecret` instead of `WebhookApiKey`.
4. Enable reception and use SePay test delivery. Verify the Admin receipt timestamp and Accountant review queue. Configuration readiness is not proof of live bank delivery.
5. Open a current invoice QR containing the complete `PF` code. Verify a small real transfer only with the account owner's authorization.

Automated tests cover authentication/HMAC, CRC payload, OTP/source binding, encrypted inbox/retry, mismatches, manual allocation/excess, and resident settlement. `SePayDatabaseFlowTests` additionally runs against PostgreSQL when `PROPFLOW_PAYMENT_TEST_DATABASE` is explicitly set to a migrated dev/test database. It inserts random fixture IDs, tests concurrent delivery/claims, partial and full settlement, excess handling and replay protection, then deletes only its own fixture records. Do not point this variable at a production database.
