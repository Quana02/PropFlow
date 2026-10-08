# FE-01 implementation baseline

The user approved proceeding after the codebase audit in this task. Implementation follows the recommended interpretations, with unresolved external dependencies reported rather than mocked.

- Resident-only registration: username, display name, email, password, phone number, identity type and identity number. Phone, identity type and identity number are required. No role/apartment selector.
- Match exactly one `ACTIVE`, unlinked Resident by the same four normalized values on one Resident row: email, phone number, identity type and identity number. Email is trimmed/lowercased; phone reuses the canonical FE-02 representation; identity type is `CCCD` or `CMND`; identity number is digits-only (`CCCD`: exactly 12 digits; `CMND`: 9 or 12 digits). Phone is required for matching but is not a globally unique Resident key and is never used alone to identify a Resident.
- The matched Resident must have at least one current qualifying apartment relationship: an active residency or a current ownership. Active residency keeps the FE-02 date/status semantics; current ownership is read from the FE-03 Apartments public relationship contract and requires `StartDate <= today` with `EndDate == null`. `OWNER_ONLY`, `RESIDENT_ONLY` and `OWNER_AND_RESIDENT` are therefore all eligible when their required current relationship exists. Historical-only relationships do not qualify. No-match, ambiguity or ineligibility returns the same generic public response and no account/OTP is created.
- Registration eligibility only links the account to the Resident. Ownership eligibility does not create `ResidentApartment`, does not turn an owner into an occupant/household member, and does not grant access to Apartment List/Detail or other residents' PII. Resource authorization continues to check the relationship required by each use case.
- Registration uses email OTP only. Six digits, five-minute expiry, five failed attempts, sixty-second resend interval, five sends per account per hour. Resend cancels the previous challenge. Refreshing the onboarding page requires restarting/resuming with credentials; no secret browser persistence.
- The OTP challenge is bound to the exact Resident candidate fingerprint. Before activation, the system revalidates that the Resident still exists, is `ACTIVE`, remains unlinked, still has a qualifying current relationship and still matches the registration fingerprint.
- Login uses username/password. No Remember Me switch. All users land on their own account unless a safe authorized return URL exists.
- Access token: RS256, fifteen minutes, WASM memory only. Refresh: Secure/HttpOnly cookie, rotation, absolute seven-day session lifetime. Cookie operations require CSRF protection.
- Recovery uses a separate email OTP and a one-use five-minute reset proof held in memory. Accounts without verified email require administrative assistance.
- Password: 12–128 characters, no trimming/normalization. Framework PasswordHasher. Five failed logins lock for fifteen minutes. IP rate limits: login 10/minute; registration/recovery 5/15 minutes.
- Normal logout revokes the current session. Password reset/change revokes all refresh sessions and requires login. Already issued JWTs expire within their normal TTL.
- Own profile edits display name/contact phone only. Username/email/role/status are read-only. No mutation of Resident records or residency, no avatar/vehicle scope.
- Security events have a thirty-day retention baseline; no password, OTP, token or sensitive payload logging.
- Keep approved DBML relationships; corrective migrations must fail on inconsistent existing data rather than delete or invent records. Do not apply migrations to an unverified database automatically.
- Authentication consumes only `Residents.Contracts` for Resident registration lookup/revalidation/linking. It does not access `ResidentsDbContext`, `ApartmentsDbContext` or either module's infrastructure/domain entities.

`PropFlow_15FE.docx` and the Report 3 SRS were not present during audit. FE-01.1–FE-01.7 are used for traceability; consolidated UC IDs must not be invented. Existing FE-01/02/15 specifications and architecture rules remain the available source documents.

Backend endpoints and DTOs are being added as the approved backend dependency, not inferred from the previous demo UI. Operational SMTP credentials, JWT keys and real eligible Resident records are external prerequisites.
