# FE-01 implementation baseline

The user approved proceeding after the codebase audit in this task. Implementation follows the recommended interpretations, with unresolved external dependencies reported rather than mocked.

- Resident-only registration: username, display name, email, password and optional contact phone. No role/apartment selector.
- Match exactly one ACTIVE, unlinked Resident by normalized email, with a currently active residency. Management owns those records. No-match/ambiguous requests remain pending and cannot activate.
- Registration uses email OTP only. Six digits, five-minute expiry, five failed attempts, sixty-second resend interval, five sends per account per hour. Resend cancels the previous challenge. Refreshing the onboarding page requires restarting/resuming with credentials; no secret browser persistence.
- Login uses username/password. No Remember Me switch. All users land on their own account unless a safe authorized return URL exists.
- Access token: RS256, fifteen minutes, WASM memory only. Refresh: Secure/HttpOnly cookie, rotation, absolute seven-day session lifetime. Cookie operations require CSRF protection.
- Recovery uses a separate email OTP and a one-use five-minute reset proof held in memory. Accounts without verified email require administrative assistance.
- Password: 12–128 characters, no trimming/normalization. Framework PasswordHasher. Five failed logins lock for fifteen minutes. IP rate limits: login 10/minute; registration/recovery 5/15 minutes.
- Normal logout revokes the current session. Password reset/change revokes all refresh sessions and requires login. Already issued JWTs expire within their normal TTL.
- Own profile edits display name/contact phone only. Username/email/role/status are read-only. No mutation of Resident records or residency, no avatar/vehicle scope.
- Security events have a thirty-day retention baseline; no password, OTP, token or sensitive payload logging.
- Keep approved DBML relationships; corrective migrations must fail on inconsistent existing data rather than delete or invent records. Do not apply migrations to an unverified database automatically.

`PropFlow_15FE.docx` and the Report 3 SRS were not present during audit. FE-01.1–FE-01.7 are used for traceability; consolidated UC IDs must not be invented. Existing FE-01/02/15 specifications and architecture rules remain the available source documents.

Backend endpoints and DTOs are being added as the approved backend dependency, not inferred from the previous demo UI. Operational SMTP credentials, JWT keys and real eligible Resident records are external prerequisites.
