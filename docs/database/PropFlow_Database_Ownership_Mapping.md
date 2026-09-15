# PropFlow — Database Ownership & Module Mapping Document

**Tài liệu Phân định Sở hữu Cơ sở Dữ liệu & Ánh xạ Module**

- **Đường dẫn DBML gốc:** `docs/database/PropFlow.dbml`
- **Ngày thực hiện phân tích:** 14/09/2026
- **Phiên bản Kiến trúc áp dụng:** Modular Monolith (.NET 8 / `net8.0`), Clean Architecture Principles per Module, Schema-Per-Module Persistence Boundaries, PostgreSQL + EF Core.
- **Tổng số bảng đã ánh xạ:** 47 bảng (thuộc 14 PostgreSQL Schemas).
- **Tổng số Business Modules:** 15 Modules (`src/Modules/`).
- **Cam kết an toàn:**
  - File `docs/database/PropFlow.dbml` là thiết kế gốc đã được phê duyệt, không bị sửa đổi.
  - EF Core Migrations sau này sẽ là executable database schema duy nhất.
  - **Nhiệm vụ này là thuần phân tích và tạo tài liệu**, không sinh bất kỳ C# Entity, DbContext, Configuration, Migration hay mã nguồn dự án nào.

---

## A. SOURCE SUMMARY

### 1. Nguồn dữ liệu & Phạm vi

Tài liệu này thực hiện phân tích toàn bộ schema trong `docs/database/PropFlow.dbml` và phân định ranh giới sở hữu (Domain / Persistence Ownership) cho 15 Business Modules của hệ thống PropFlow:

1. `Authentication` (Schema: `auth`)
2. `Residents` (Schema: `residents`)
3. `Apartments` (Schema: `apartments`)
4. `PropertyAssets` (Schema: `property_assets`)
5. `ServiceRequests` (Schema: `service_requests`)
6. `Complaints` (Schema: `complaints`)
7. `Maintenance` (Schema: `maintenance`)
8. `Billing` (Schema: `billing`)
9. `Payments` (Schema: `payments`)
10. `AiClassification` (Schema: `ai_classification`)
11. `AiRecommendation` (Schema: `ai_recommendation`)
12. `AiChatbot` (Không có bảng source-of-truth riêng trong baseline DBML)
13. `Communication` (Schema: `communication`)
14. `Reporting` (Không có bảng source-of-truth riêng trong baseline DBML)
15. `Administration` (Schema: `administration`)

---

### 2. Các Nguyên tắc Bắt buộc (Non-negotiable Rules)

- **Ranh giới Domain độc lập:**
  - `User Account` != `Resident` != `Apartment`
  - `Service Request` != `Complaint`
  - `Service Request` != `Maintenance Task`
  - `Maintenance Schedule` != `Maintenance Task`
  - `Invoice/Billing lifecycle` != `Payment/Debt lifecycle`
  - `AI classification` != `AI recommendation/priority`
  - `Communication delivery` != `Ownership của source workflow`
  - `Reporting` != `Ownership của source business record`
  - `RBAC authorization` != `Domain-level business-scope validation`
- **Mỗi bảng thuộc duy nhất 1 Module:** Không có bảng nào được sở hữu chung hoặc nhân bản giữa các module.
- **Không có EF Navigation Property Xuyên Module (Cross-Module):** Mọi tham chiếu liên module chỉ lưu trữ **Scalar Identifier (UUID)** ở cấp persistence. Việc truy vấn/validation liên module được thực hiện ở tầng Application/Module Contract hoặc Integration Event.
- **PostgreSQL FK tuân thủ nghiêm ngặt DBML:** Nếu DBML có khai báo `ref: > target.table.col` thì quan hệ đó **phải có physical PostgreSQL Foreign Key** ở tầng database (dù là cross-module). Chỉ khi DBML không có `ref:` mà chỉ ghi chú thích "Stable scalar reference... no cross-module EF navigation" thì quan hệ đó mới là **Scalar Only (No physical PostgreSQL FK)**.

---

## B. COMPLETE OWNERSHIP MATRIX

Bảng dưới đây ánh xạ đầy đủ 47 bảng trong `docs/database/PropFlow.dbml` vào đúng 15 Business Modules và đánh dấu trạng thái sở hữu kỹ thuật:

| STT | Schema DBML | Bảng DBML | Module Sở hữu | Proposed C# Entity | Aggregate Root | Primary Key | Key Constraints & Invariants | Cross-Module References (Scalar IDs Only) | Soft-Delete / Status Lifecycle |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **1** | `auth` | `users` | `Authentication` | `UserAccount` *(xem mục E.2)* | Root | `id` (uuid) | `UNIQUE(username)`, `UNIQUE(email)`, `UNIQUE(phone_number)`; `phone_number` is contact information only | Self-ref `created_by`, `updated_by` -> `auth.users.id` (within-module) | `account_status` (`PENDING`, `ACTIVE`, `LOCKED`, `SUSPENDED`, `DISABLED`); `email_verified` only |
| **2** | `auth` | `refresh_tokens` | `Authentication` | `RefreshToken` | Child (`UserAccount`) | `id` (uuid) | `UNIQUE(token_hash)`, self-ref `replaced_by_token_id` | None | Revoked status (`revoked_at`) |
| **3** | `auth` | `password_reset_tokens` | `Authentication` | `PasswordResetToken` | Child (`UserAccount`) | `id` (uuid) | `UNIQUE(token_hash)` | None | Expired / Used (`used_at`) |
| **4** | `auth` | `resident_verifications` | `Authentication` | `ResidentVerification` | Root | `id` (uuid) | Email OTP challenge only; stores OTP hash, required `resident_id`, expiry and attempt count | `resident_id` -> `residents.residents.id` (cross-module FK per DBML), `apartment_unit_id` -> `apartments.apartment_units.id` (cross-module FK per DBML) | `verification_status` (`PENDING`, `VERIFIED`, `EXPIRED`, `CANCELLED`) |
| **5** | `administration` | `roles` | `Administration` | `Role` | Root | `id` (uuid) | `UNIQUE(code)` | None | Active flag (`is_active`) |
| **6** | `administration` | `permissions` | `Administration` | `Permission` | Root | `id` (uuid) | `UNIQUE(code)` | None | Active flag (`is_active`) |
| **7** | `administration` | `role_permissions` | `Administration` | `RolePermission` | Join Entity | `(role_id, permission_id)` | Composite PK | `created_by` -> `auth.users.id` (cross-module FK per DBML) | None |
| **8** | `administration` | `user_role_assignments` | `Administration` | `UserRoleAssignment` | Root | `id` (uuid) | `UNIQUE(user_id)` (1 primary role/user) | `user_id` -> `auth.users.id`, `assigned_by` -> `auth.users.id`, `updated_by` -> `auth.users.id` (cross-module FK per DBML) | Active assignment |
| **9** | `administration` | `user_building_accesses` | `Administration` | `UserBuildingAccess` | Root | `id` (uuid) | Partial `UNIQUE(user_id, building_id) WHERE revoked_at IS NULL`; non-unique history index `(user_id, building_id, granted_at)` | `user_id` -> `auth.users.id`, `building_id` -> `property_assets.buildings.id`, `granted_by`, `revoked_by` -> `auth.users.id` (cross-module scalar IDs only in EF) | Revoked status (`revoked_at`) |
| **10** | `administration` | `user_access_history` | `Administration` | `UserAccessHistory` | Entity | `id` (uuid) | Log lịch sử thay đổi quyền/trạng thái | `target_user_id`, `performed_by` -> `auth.users.id` (cross-module FK per DBML), `old_role_id`, `new_role_id` -> `administration.roles.id` (within-module FK) | Append-only audit log |
| **11** | `administration` | `system_configurations` | `Administration` | `SystemConfiguration` | Root | `id` (uuid) | `UNIQUE(config_key)` | `updated_by` -> `auth.users.id` (cross-module FK per DBML) | Active flag (`is_active`) |
| **12** | `administration` | `audit_logs` | `Administration` | `AuditLog` | Entity | `id` (uuid) | System-wide audit log | `actor_user_id` -> `auth.users.id` (cross-module FK per DBML) | Append-only audit log |
| **13** | `residents` | `residents` | `Residents` | `Resident` | Root | `id` (uuid) | `UNIQUE(resident_code)`, `UNIQUE(user_id)` | `user_id` -> `auth.users.id`, `created_by`, `updated_by` -> `auth.users.id` (cross-module FK per DBML) | `resident_status` (`ACTIVE`, `INACTIVE`, `MOVED_OUT`) |
| **14** | `residents` | `resident_apartments` | `Residents` | `ResidentApartment` | Child (`Resident`) | `id` (uuid) | `UNIQUE(resident_id, apartment_unit_id, start_date)`, `CHECK end_date >= start_date` | `apartment_unit_id` -> `apartments.apartment_units.id` (cross-module FK per DBML), `created_by`, `updated_by` -> `auth.users.id` (cross-module FK per DBML) | `residency_status` (`ACTIVE`, `ENDED`) |
| **15** | `apartments` | `apartment_units` | `Apartments` | `ApartmentUnit` | Root | `id` (uuid) | `UNIQUE(building_id, unit_number)` | `building_id` -> `property_assets.buildings.id`, `created_by`, `updated_by` -> `auth.users.id` (cross-module FK per DBML) | `master_data_status` (`ACTIVE`, `INACTIVE`) |
| **16** | `property_assets` | `buildings` | `PropertyAssets` | `Building` | Root | `id` (uuid) | `UNIQUE(code)`, `time_zone_id` (IANA ID) | `created_by`, `updated_by` -> `auth.users.id` (cross-module FK per DBML) | `master_data_status` (`ACTIVE`, `INACTIVE`) |
| **17** | `property_assets` | `facilities` | `PropertyAssets` | `Facility` | Root | `id` (uuid) | `UNIQUE(building_id, code)` | `building_id` -> `property_assets.buildings.id` (within-module), `created_by`, `updated_by` -> `auth.users.id` (cross-module FK per DBML) | `master_data_status` (`ACTIVE`, `INACTIVE`) |
| **18** | `property_assets` | `equipment` | `PropertyAssets` | `Equipment` | Root | `id` (uuid) | `UNIQUE(building_id, code)` | `building_id` -> `property_assets.buildings.id` (within-module), `facility_id` -> `property_assets.facilities.id` (within-module), `created_by`, `updated_by` -> `auth.users.id` (cross-module FK per DBML) | `equipment_status` (`ACTIVE`, `INACTIVE`, `OUT_OF_SERVICE`, `UNDER_MAINTENANCE`) |
| **19** | `service_requests` | `service_request_categories` | `ServiceRequests` | `ServiceRequestCategory` | Root | `id` (uuid) | `UNIQUE(code)` | `created_by`, `updated_by` -> `auth.users.id` (cross-module FK per DBML) | Active flag (`is_active`) |
| **20** | `service_requests` | `service_requests` | `ServiceRequests` | `ServiceRequest` | Root | `id` (uuid) | `UNIQUE(request_number)` | `resident_id` -> `residents.residents.id`, `resident_apartment_id` -> `residents.resident_apartments.id`, `building_id` -> `property_assets.buildings.id`, `apartment_unit_id` -> `apartments.apartment_units.id`, `facility_id` -> `property_assets.facilities.id`, `equipment_id` -> `property_assets.equipment.id`, `closed_by` -> `auth.users.id` (cross-module FK per DBML); `category_id` -> `service_requests.service_request_categories.id` (within-module) | `service_request_status` |
| **21** | `service_requests` | `service_request_assignments` | `ServiceRequests` | `ServiceRequestAssignment` | Child (`ServiceRequest`) | `id` (uuid) | Append history; partial `UNIQUE(service_request_id) WHERE status IN ('ASSIGNED','IN_PROGRESS')` | `staff_user_id`, `assigned_by` -> `auth.users.id` (cross-module scalar IDs only in EF); `service_request_id` -> within-module | `assignment_status` |
| **22** | `service_requests` | `service_request_activities` | `ServiceRequests` | `ServiceRequestActivity` | Child (`ServiceRequest`) | `id` (uuid) | Activity log | `performed_by` -> `auth.users.id` (cross-module FK per DBML); `service_request_id`, `assignment_id` -> within-module | `service_activity_type` |
| **23** | `complaints` | `complaints` | `Complaints` | `Complaint` | Root | `id` (uuid) | `UNIQUE(complaint_number)` | `resident_id` -> `residents.residents.id`, `resident_apartment_id` -> `residents.resident_apartments.id`, `building_id` -> `property_assets.buildings.id`, `apartment_unit_id` -> `apartments.apartment_units.id`, `facility_id` -> `property_assets.facilities.id`, `equipment_id` -> `property_assets.equipment.id`, `closed_by` -> `auth.users.id` (cross-module FK per DBML); **`related_service_request_id` — SCALAR ONLY, không có `ref:` trong DBML** | `complaint_status` |
| **24** | `complaints` | `complaint_followups` | `Complaints` | `ComplaintFollowup` | Child (`Complaint`) | `id` (uuid) | Operational follow-up task | `staff_user_id`, `assigned_by` -> `auth.users.id` (cross-module FK per DBML); `complaint_id` -> within-module | `complaint_followup_status` |
| **25** | `complaints` | `complaint_activities` | `Complaints` | `ComplaintActivity` | Child (`Complaint`) | `id` (uuid) | Activity log | `performed_by` -> `auth.users.id` (cross-module FK per DBML); `complaint_id`, `followup_id` -> within-module | `complaint_activity_type` |
| **26** | `maintenance` | `maintenance_schedules` | `Maintenance` | `MaintenanceSchedule` | Root | `id` (uuid) | `UNIQUE(schedule_code)`, `CHECK planned_end_at IS NULL OR planned_end_at >= planned_start_at` | `building_id`, `facility_id`, `equipment_id`, `created_by`, `updated_by` are cross-module scalar IDs only; no EF navigation or physical FK | `maintenance_schedule_status` |
| **27** | `maintenance` | `maintenance_tasks` | `Maintenance` | `MaintenanceTask` | Root | `id` (uuid) | `UNIQUE(task_number)`, lifecycle timestamp checks | `schedule_id` -> within-module; `building_id`, `facility_id`, `equipment_id`, `closed_by`, `created_by`, `source_service_request_id`, `source_complaint_id` are cross-module scalar IDs only where applicable; no EF navigation or physical FK | `maintenance_task_status` |
| **28** | `maintenance` | `maintenance_assignments` | `Maintenance` | `MaintenanceAssignment` | Child (`MaintenanceTask`) | `id` (uuid) | Partial `UNIQUE(maintenance_task_id) WHERE status IN ('ASSIGNED','IN_PROGRESS')`; assignment timestamp checks | `maintenance_task_id` -> within-module; `staff_user_id`, `assigned_by` are cross-module scalar IDs only; no EF navigation or physical FK | `assignment_status` |
| **29** | `maintenance` | `maintenance_task_activities` | `Maintenance` | `MaintenanceTaskActivity` | Child (`MaintenanceTask`) | `id` (uuid) | Activity log | `maintenance_task_id`, `assignment_id` -> within-module; `performed_by` is cross-module scalar ID only; no EF navigation or physical FK | `maintenance_activity_type` |
| **30** | `maintenance` | `maintenance_results` | `Maintenance` | `MaintenanceResult` | Child (`MaintenanceTask`) | `id` (uuid) | `UNIQUE(maintenance_task_id, attempt_no)`, `CHECK attempt_no >= 1`, review timestamp check | `maintenance_task_id` -> within-module; `submitted_by`, `reviewed_by` are cross-module scalar IDs only; no EF navigation or physical FK | `maintenance_result_status` |
| **31** | `billing` | `fee_types` | `Billing` | `FeeType` | Root | `id` (uuid) | `UNIQUE(code)` | `created_by`, `updated_by` are cross-module scalar IDs only; no EF navigation or physical FK | `master_data_status` |
| **32** | `billing` | `fee_rate_rules` | `Billing` | `FeeRateRule` | Child (`FeeType`) | `id` (uuid) | `unit_rate` numeric(18,0), `CHECK effective_to >= effective_from`, amount checks | `fee_type_id` -> within-module; `building_id`, `created_by`, `updated_by` are cross-module scalar IDs only; no EF navigation or physical FK | Active flag (`is_active`) |
| **33** | `billing` | `invoices` | `Billing` | `Invoice` | Root | `id` (uuid) | `UNIQUE(invoice_number)`, `UNIQUE(apartment_unit_id, billing_period_start, billing_period_end)`, period/date/money/lifecycle checks | `apartment_unit_id`, `issued_by`, `cancelled_by`, `created_by`, `updated_by` are cross-module scalar IDs only; no EF navigation or physical FK | `invoice_status` (`DRAFT`, `ISSUED`, `CANCELLED`) |
| **34** | `billing` | `invoice_items` | `Billing` | `InvoiceItem` | Child (`Invoice`) | `id` (uuid) | Snapshot: `quantity` numeric(18,4), `unit_rate` numeric(18,0), `line_amount` numeric(18,0) | `invoice_id`, `fee_type_id`, `fee_rate_rule_id` -> within-module | Immutable after issue |
| **35** | `billing` | `invoice_status_history` | `Billing` | `InvoiceStatusHistory` | Child (`Invoice`) | `id` (uuid) | Audit vết trạng thái hóa đơn | `invoice_id` -> within-module; `changed_by` is cross-module scalar ID only; no EF navigation or physical FK | Append-only history |
| **36** | `payments` | `payments` | `Payments` | `Payment` | Root | `id` (uuid) | `UNIQUE(payment_number)`, filtered unique `reference_number` when present, `amount` numeric(18,0), `CHECK amount > 0` | `invoice_id`, `submitted_by`, `confirmed_by`, `rejected_by` are cross-module scalar IDs only; no EF navigation or physical FK | `payment_status` (`PENDING`, `CONFIRMED`, `REJECTED`) |
| **37** | `payments` | `payment_status_history` | `Payments` | `PaymentStatusHistory` | Child (`Payment`) | `id` (uuid) | Audit vết trạng thái thanh toán | `payment_id` -> within-module; `changed_by` is cross-module scalar ID only; no EF navigation or physical FK | Append-only history |
| **38** | `shared` | `idempotency_records` | **UNRESOLVED TECHNICAL OWNER (xem mục E.1)** | `IdempotencyRecord` | Infrastructure Entity | `id` (uuid) | `UNIQUE(owner_module, idempotency_key)` | `actor_user_id` -> `auth.users.id` (cross-module FK per DBML) | Retain / Expire via `expires_at` |
| **39** | `shared` | `outbox_messages` | **UNRESOLVED TECHNICAL OWNER (xem mục E.1)** | `OutboxMessage` | Infrastructure Entity | `id` (uuid) | Transactional Outbox pattern | None | Processed timestamp (`processed_at`) |
| **40** | `ai_classification` | `ai_request_classifications` | `AiClassification` | `AiRequestClassification` | Root | `id` (uuid) | `UNIQUE(service_request_id, attempt_no)`, `CHECK attempt_no >= 1`, token/latency/confidence checks, SUCCESS/FAILED field consistency checks | `service_request_id`, `predicted_category_id` are cross-module scalar IDs only; no EF navigation or physical FK | `ai_run_status` (`SUCCESS`, `FAILED`) |
| **41** | `ai_classification` | `ai_classification_reviews` | `AiClassification` | `AiClassificationReview` | Child (`AiRequestClassification`) | `id` (uuid) | `UNIQUE(classification_id)`, final category consistency check | `classification_id` -> within-module; `reviewed_by`, `final_category_id` are cross-module scalar IDs only; no EF navigation or physical FK | `ai_review_decision` |
| **42** | `ai_recommendation` | `ai_request_recommendations` | `AiRecommendation` | `AiRequestRecommendation` | Root | `id` (uuid) | `UNIQUE(service_request_id, attempt_no)`, `CHECK attempt_no >= 1`, token/latency checks, SUCCESS/FAILED field consistency checks | **`service_request_id` — SCALAR ONLY, không có `ref:`** | `ai_run_status` (`SUCCESS`, `FAILED`) |
| **43** | `ai_recommendation` | `ai_recommendation_reviews` | `AiRecommendation` | `AiRecommendationReview` | Child (`AiRequestRecommendation`) | `id` (uuid) | `UNIQUE(recommendation_id)`, final recommendation consistency checks | `recommendation_id` -> within-module; `reviewed_by` is cross-module scalar ID only; no EF navigation or physical FK | `ai_review_decision` |
| **44** | `communication` | `notifications` | `Communication` | `Notification` | Root | `id` (uuid) | `UNIQUE(recipient_user_id, source_event_id)` filtered when `source_event_id IS NOT NULL` | `recipient_user_id` -> `auth.users.id` is a logical scalar reference only; `source_id` is a polymorphic scalar reference only | Read status (`is_read`, `read_at`) |
| **45** | `communication` | `announcements` | `Communication` | `Announcement` | Root | `id` (uuid) | Quản lý thông báo chung | `created_by`, `updated_by` -> `auth.users.id` are logical scalar references only | `announcement_status` (`DRAFT`, `PUBLISHED`, `WITHDRAWN`) |
| **46** | `communication` | `announcement_versions` | `Communication` | `AnnouncementVersion` | Child (`Announcement`) | `id` (uuid) | `UNIQUE(announcement_id, version_no)` | `changed_by` -> `auth.users.id` is a logical scalar reference only; `announcement_id` -> within-module physical FK | Revisions history |
| **47** | `communication` | `announcement_audiences` | `Communication` | `AnnouncementAudience` | Child (`Announcement`) | `id` (uuid) | Conditional scalar-target constraint; partial unique indexes prevent duplicate audience scopes per announcement | `role_id`, `building_id`, `apartment_unit_id`, `resident_id` are logical scalar references only; `announcement_id` -> within-module physical FK | `announcement_audience_type` |

---

## C. CROSS-MODULE RELATIONSHIP MATRIX

Bảng Relationship Matrix dưới đây kiểm tra và phân loại riêng biệt từng quan hệ cột nguồn (`source column`) trong toàn bộ DBML.

**Phương pháp đếm:** Đếm chi tiết từng `source column relationship` (cột nguồn tham chiếu tới bảng khác).
- **Tổng số cột có quan hệ (Foreign Key hoặc Scalar Reference):** 138 cột.
- **Quan hệ nội bộ module (Within-module):** 38 quan hệ (cùng schema).
- **Quan hệ liên module (Cross-module):** **100 quan hệ**.
  - **Có physical PostgreSQL Foreign Key (DBML khai báo `ref: >`):** **94 quan hệ**.
  - **Stable Scalar Reference không có FK (DBML ghi chú scalar, không có `ref:`):** **6 quan hệ**.
- **EF Navigation xuyên module:** `No` tuyệt đối cho toàn bộ 100 quan hệ liên module.
- **Module ProjectReference trực tiếp:** `No` tuyệt đối cho toàn bộ 15 business module main projects.

### 1. Nhóm 6 Stable Scalar Relationships (Không có PostgreSQL FK trong DBML)

| # | Source Schema | Source Table | Source Column | Target Schema | Target Table | Within / Cross | DBML `Ref` | PostgreSQL FK Required | EF Navigation Allowed | ProjectReference Allowed | Delete Behavior | Application Validation Required | Lý do / Căn cứ DBML |
|:--|:---|:---|:---|:---|:---|:---|:---|:---|:---|:---|:---|:---|:---|
| 1 | `complaints` | `complaints` | `related_service_request_id` | `service_requests` | `service_requests` | **Cross-module** | **No** | **No** | **No** | **No** | N/A | Yes | Note DBML: *"Stable scalar reference to FE-05; no cross-module EF navigation."* Tách biệt hoàn toàn vòng đời khiếu nại và yêu cầu dịch vụ. |
| 2 | `maintenance` | `maintenance_tasks` | `source_service_request_id` | `service_requests` | `service_requests` | **Cross-module** | **No** | **No** | **No** | **No** | N/A | Yes | Note DBML: *"Optional stable scalar reference to FE-05; no cross-module EF navigation."* Nhiệm vụ bảo trì độc lập với yêu cầu dịch vụ nguồn. |
| 3 | `maintenance` | `maintenance_tasks` | `source_complaint_id` | `complaints` | `complaints` | **Cross-module** | **No** | **No** | **No** | **No** | N/A | Yes | Note DBML: *"Optional stable scalar reference to FE-06 when maintenance originates from complaint follow-up."* |
| 4 | `payments` | `payments` | `invoice_id` | `billing` | `invoices` | **Cross-module** | **No** | **No** | **No** | **No** | N/A | Yes | Note DBML: *"Stable scalar reference to FE-08 Invoice; no cross-module EF navigation."* Tách biệt vòng đời phát hành hóa đơn và thanh toán/công nợ. |
| 5 | `ai_classification` | `ai_request_classifications` | `service_request_id` | `service_requests` | `service_requests` | **Cross-module** | **No** | **No** | **No** | **No** | N/A | Yes | Note DBML: *"Stable scalar reference to FE-05; no cross-module EF navigation."* AI classification chỉ mang tính tư vấn, không khóa cứng FK. |
| 6 | `ai_recommendation` | `ai_request_recommendations` | `service_request_id` | `service_requests` | `service_requests` | **Cross-module** | **No** | **No** | **No** | **No** | N/A | Yes | Note DBML: *"Stable scalar reference to FE-05; no cross-module EF navigation."* Gợi ý bảo trì/mức độ ưu tiên của AI là độc lập. |

---

### 2. Danh sách 94 Cross-Module Relationships Có Physical PostgreSQL Foreign Key

Toàn bộ 94 quan hệ này có khai báo `ref: > target_schema.target_table.id` tường minh trong DBML. Chúng **bắt buộc tạo physical Foreign Key trong PostgreSQL** (với `ON DELETE RESTRICT` hoặc `NO ACTION` theo DBML Invariant #12), nhưng **không tạo EF Core Navigation property** trên C# Entity và **không thêm ProjectReference giữa các Business Module**.

| # | Source Schema | Source Table | Source Column | Target Schema | Target Table | Within / Cross | DBML `Ref` | PostgreSQL FK Required | EF Navigation Allowed | ProjectReference Allowed | Delete Behavior | Application Validation Required | Lý do / Căn cứ |
|:--|:---|:---|:---|:---|:---|:---|:---|:---|:---|:---|:---|:---|:---|
| 1 | `administration` | `role_permissions` | `created_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit audit actor reference |
| 2 | `administration` | `user_role_assignments` | `user_id` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | User phải tồn tại; 1 role per user |
| 3 | `administration` | `user_role_assignments` | `assigned_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 4 | `administration` | `user_role_assignments` | `updated_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 5 | `administration` | `user_building_accesses` | `user_id` | `auth` | `users` | Cross-module | Yes | No | No | No | None | Yes | User scalar reference; existence checked through module contract/application orchestration |
| 6 | `administration` | `user_building_accesses` | `building_id` | `property_assets` | `buildings` | Cross-module | Yes | No | No | No | None | Yes | Building scalar reference; existence checked through module contract/application orchestration |
| 7 | `administration` | `user_building_accesses` | `granted_by` | `auth` | `users` | Cross-module | Yes | No | No | No | None | No | Audit actor scalar reference |
| 8 | `administration` | `user_building_accesses` | `revoked_by` | `auth` | `users` | Cross-module | Yes | No | No | No | None | No | Audit actor scalar reference |
| 9 | `auth` | `resident_verifications` | `resident_id` | `residents` | `residents` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Resident profile cần xác minh |
| 10 | `auth` | `resident_verifications` | `apartment_unit_id` | `apartments` | `apartment_units` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Apartment unit cần xác minh |
| 11 | `administration` | `user_access_history` | `target_user_id` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Lịch sử phân quyền user |
| 12 | `administration` | `user_access_history` | `performed_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 13 | `administration` | `system_configurations` | `updated_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 14 | `administration` | `audit_logs` | `actor_user_id` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 15 | `residents` | `residents` | `user_id` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Liên kết tài khoản đăng nhập (nullable) |
| 16 | `residents` | `residents` | `created_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 17 | `residents` | `residents` | `updated_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 18 | `property_assets` | `buildings` | `created_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 19 | `property_assets` | `buildings` | `updated_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 20 | `apartments` | `apartment_units` | `building_id` | `property_assets` | `buildings` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Căn hộ bắt buộc thuộc tòa nhà hợp lệ |
| 21 | `apartments` | `apartment_units` | `created_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 22 | `apartments` | `apartment_units` | `updated_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 23 | `residents` | `resident_apartments` | `apartment_unit_id` | `apartments` | `apartment_units` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Quan hệ cư trú gắn với căn hộ cụ thể |
| 24 | `residents` | `resident_apartments` | `created_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 25 | `residents` | `resident_apartments` | `updated_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 26 | `property_assets` | `facilities` | `created_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 27 | `property_assets` | `facilities` | `updated_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 28 | `property_assets` | `equipment` | `created_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 29 | `property_assets` | `equipment` | `updated_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 30 | `service_requests` | `service_request_categories` | `created_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 31 | `service_requests` | `service_request_categories` | `updated_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Audit actor reference |
| 32 | `service_requests` | `service_requests` | `resident_id` | `residents` | `residents` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Người gửi yêu cầu phải là cư dân hợp lệ |
| 33 | `service_requests` | `service_requests` | `resident_apartment_id` | `residents` | `resident_apartments` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Ngữ cảnh cư trú để thẩm quyền gửi |
| 34 | `service_requests` | `service_requests` | `building_id` | `property_assets` | `buildings` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Vị trí sự cố thuộc tòa nhà |
| 35 | `service_requests` | `service_requests` | `apartment_unit_id` | `apartments` | `apartment_units` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Căn hộ xảy ra sự cố (nếu có) |
| 36 | `service_requests` | `service_requests` | `facility_id` | `property_assets` | `facilities` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Tiện ích xảy ra sự cố (nếu có) |
| 37 | `service_requests` | `service_requests` | `equipment_id` | `property_assets` | `equipment` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Thiết bị xảy ra sự cố (nếu có) |
| 38 | `service_requests` | `service_requests` | `closed_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Manager đóng yêu cầu |
| 39 | `service_requests` | `service_request_assignments` | `staff_user_id` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Nhân viên được phân công |
| 40 | `service_requests` | `service_request_assignments` | `assigned_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Manager thực hiện phân công |
| 41 | `service_requests` | `service_request_activities` | `performed_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Người thực hiện hành động |
| 42 | `complaints` | `complaints` | `resident_id` | `residents` | `residents` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Cư dân gửi khiếu nại |
| 43 | `complaints` | `complaints` | `resident_apartment_id` | `residents` | `resident_apartments` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Ngữ cảnh cư trú khi gửi khiếu nại |
| 44 | `complaints` | `complaints` | `building_id` | `property_assets` | `buildings` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Tòa nhà xảy ra vấn đề |
| 45 | `complaints` | `complaints` | `apartment_unit_id` | `apartments` | `apartment_units` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Căn hộ liên quan (nếu có) |
| 46 | `complaints` | `complaints` | `facility_id` | `property_assets` | `facilities` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Tiện ích liên quan (nếu có) |
| 47 | `complaints` | `complaints` | `equipment_id` | `property_assets` | `equipment` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Thiết bị liên quan (nếu có) |
| 48 | `complaints` | `complaints` | `closed_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Manager đóng khiếu nại |
| 49 | `complaints` | `complaint_followups` | `staff_user_id` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | Yes | Nhân viên xử lý follow-up |
| 50 | `complaints` | `complaint_followups` | `assigned_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Manager giao việc follow-up |
| 51 | `complaints` | `complaint_activities` | `performed_by` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | Người thực hiện hành động |
| 52 | `maintenance` | `maintenance_schedules` | `building_id` | `property_assets` | `buildings` | Cross-module | Yes | No | No | No | N/A | Yes | Scalar-only theo Modular Monolith boundary; validate qua Application/contracts |
| 53 | `maintenance` | `maintenance_schedules` | `facility_id` | `property_assets` | `facilities` | Cross-module | Yes | No | No | No | N/A | Yes | Scalar-only theo Modular Monolith boundary; validate qua Application/contracts |
| 54 | `maintenance` | `maintenance_schedules` | `equipment_id` | `property_assets` | `equipment` | Cross-module | Yes | No | No | No | N/A | Yes | Scalar-only theo Modular Monolith boundary; validate qua Application/contracts |
| 55 | `maintenance` | `maintenance_schedules` | `created_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 56 | `maintenance` | `maintenance_schedules` | `updated_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 57 | `maintenance` | `maintenance_tasks` | `building_id` | `property_assets` | `buildings` | Cross-module | Yes | No | No | No | N/A | Yes | Scalar-only theo Modular Monolith boundary; validate qua Application/contracts |
| 58 | `maintenance` | `maintenance_tasks` | `facility_id` | `property_assets` | `facilities` | Cross-module | Yes | No | No | No | N/A | Yes | Scalar-only theo Modular Monolith boundary; validate qua Application/contracts |
| 59 | `maintenance` | `maintenance_tasks` | `equipment_id` | `property_assets` | `equipment` | Cross-module | Yes | No | No | No | N/A | Yes | Scalar-only theo Modular Monolith boundary; validate qua Application/contracts |
| 60 | `maintenance` | `maintenance_tasks` | `closed_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only manager/audit reference |
| 61 | `maintenance` | `maintenance_tasks` | `created_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 62 | `maintenance` | `maintenance_assignments` | `staff_user_id` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | Yes | Scalar-only staff reference |
| 63 | `maintenance` | `maintenance_assignments` | `assigned_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only manager/audit reference |
| 64 | `maintenance` | `maintenance_task_activities` | `performed_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 65 | `maintenance` | `maintenance_results` | `submitted_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only staff reference |
| 66 | `maintenance` | `maintenance_results` | `reviewed_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only manager reference |
| 67 | `billing` | `fee_types` | `created_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 68 | `billing` | `fee_types` | `updated_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 69 | `billing` | `fee_rate_rules` | `building_id` | `property_assets` | `buildings` | Cross-module | Yes | No | No | No | N/A | Yes | Scalar-only building reference; validate via Application/contracts |
| 70 | `billing` | `fee_rate_rules` | `created_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 71 | `billing` | `fee_rate_rules` | `updated_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 72 | `billing` | `invoices` | `apartment_unit_id` | `apartments` | `apartment_units` | Cross-module | Yes | No | No | No | N/A | Yes | Scalar-only apartment reference; validate via Application/contracts |
| 73 | `billing` | `invoices` | `issued_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only accountant/audit reference |
| 74 | `billing` | `invoices` | `cancelled_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only accountant/audit reference |
| 75 | `billing` | `invoices` | `created_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 76 | `billing` | `invoices` | `updated_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 77 | `billing` | `invoice_status_history` | `changed_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 78 | `payments` | `payments` | `submitted_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only actor reference |
| 79 | `payments` | `payments` | `confirmed_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only accountant reference |
| 80 | `payments` | `payments` | `rejected_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only accountant reference |
| 81 | `payments` | `payment_status_history` | `changed_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only audit actor reference |
| 82 | `shared` | `idempotency_records` | `actor_user_id` | `auth` | `users` | Cross-module | Yes | Yes | No | No | RESTRICT | No | User thực hiện idempotency action |
| 83 | `ai_classification` | `ai_request_classifications` | `predicted_category_id` | `service_requests` | `service_request_categories` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only suggested category reference |
| 84 | `ai_classification` | `ai_classification_reviews` | `reviewed_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only manager reference |
| 85 | `ai_classification` | `ai_classification_reviews` | `final_category_id` | `service_requests` | `service_request_categories` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only final category reference |
| 86 | `ai_recommendation` | `ai_recommendation_reviews` | `reviewed_by` | `auth` | `users` | Cross-module | Yes | No | No | No | N/A | No | Scalar-only manager reference; no cross-module EF navigation or physical FK |
| 87 | `communication` | `notifications` | `recipient_user_id` | `auth` | `users` | Cross-module scalar | Yes | No | No | No | N/A | Yes | Người nhận thông báo; Application/API kiểm tra quyền |
| 88 | `communication` | `announcements` | `created_by` | `auth` | `users` | Cross-module scalar | Yes | No | No | No | N/A | No | Audit actor reference |
| 89 | `communication` | `announcements` | `updated_by` | `auth` | `users` | Cross-module scalar | Yes | No | No | No | N/A | No | Audit actor reference |
| 90 | `communication` | `announcement_versions` | `changed_by` | `auth` | `users` | Cross-module scalar | Yes | No | No | No | N/A | No | Audit actor reference |
| 91 | `communication` | `announcement_audiences` | `role_id` | `administration` | `roles` | Cross-module scalar | Yes | No | No | No | N/A | Yes | Phân nhóm đối tượng theo vai trò |
| 92 | `communication` | `announcement_audiences` | `building_id` | `property_assets` | `buildings` | Cross-module scalar | Yes | No | No | No | N/A | Yes | Phân nhóm đối tượng theo tòa nhà |
| 93 | `communication` | `announcement_audiences` | `apartment_unit_id` | `apartments` | `apartment_units` | Cross-module scalar | Yes | No | No | No | N/A | Yes | Phân nhóm đối tượng theo căn hộ |
| 94 | `communication` | `announcement_audiences` | `resident_id` | `residents` | `residents` | Cross-module scalar | Yes | No | No | No | N/A | Yes | Phân nhóm gửi đích danh cư dân |

---

## D. PER-MODULE GENERATION PLAN

Kế hoạch chi tiết cho từng module, sử dụng **đầy đủ tên bảng chính xác (Exact Table Names)** từ DBML:

### 1. Module `Authentication` (`src/Modules/Authentication/`)
- **Target Schema:** `auth`
- **DbContext Name:** `AuthenticationDbContext`
- **Owned Tables (4):** `auth.users`, `auth.refresh_tokens`, `auth.password_reset_tokens`, `auth.resident_verifications`.
- **Proposed Entities:** `UserAccount` *(xem mục E.2)*, `RefreshToken`, `PasswordResetToken`, `ResidentVerification`.
- **Aggregate Roots:** `UserAccount`, `ResidentVerification`.
- **Enums:** `account_status`, `verification_status`.
- **Dự kiến Configuration Files:** `UserAccountConfiguration.cs`, `RefreshTokenConfiguration.cs`, `PasswordResetTokenConfiguration.cs`, `ResidentVerificationConfiguration.cs`.
- **Cross-Module References:** `resident_verifications.resident_id` (FK -> `residents.residents`), `resident_verifications.apartment_unit_id` (FK -> `apartments.apartment_units`). Lưu dạng scalar `Guid`. Không dùng navigation property.

### 2. Module `Administration` (`src/Modules/Administration/`)
- **Target Schema:** `administration`
- **DbContext Name:** `AdministrationDbContext`
- **Owned Tables (8):** `administration.roles`, `administration.permissions`, `administration.role_permissions`, `administration.user_role_assignments`, `administration.user_building_accesses`, `administration.user_access_history`, `administration.system_configurations`, `administration.audit_logs`.
- **Proposed Entities:** `Role`, `Permission`, `RolePermission`, `UserRoleAssignment`, `UserBuildingAccess`, `UserAccessHistory`, `SystemConfiguration`, `AuditLog`.
- **Aggregate Roots:** `Role`, `Permission`, `UserRoleAssignment`, `UserBuildingAccess`, `SystemConfiguration`.
- **Enums:** `access_action_type`, `config_value_type`, `account_status`.
- **Dự kiến Configuration Files:** `RoleConfiguration.cs`, `PermissionConfiguration.cs`, `RolePermissionConfiguration.cs`, `UserRoleAssignmentConfiguration.cs`, `UserBuildingAccessConfiguration.cs`, `UserAccessHistoryConfiguration.cs`, `SystemConfigurationConfiguration.cs`, `AuditLogConfiguration.cs`.
- **Cross-Module References:** Các cột liên kết tới `auth.users` (`user_id`, `assigned_by`, `target_user_id`, `performed_by`, `actor_user_id`, `updated_by`, `created_by`) và `property_assets.buildings` (`building_id`). Toàn bộ lưu dạng scalar `Guid`, có physical FK.

### 3. Module `Residents` (`src/Modules/Residents/`)
- **Target Schema:** `residents`
- **DbContext Name:** `ResidentsDbContext`
- **Owned Tables (2):** `residents.residents`, `residents.resident_apartments`.
- **Proposed Entities:** `Resident`, `ResidentApartment`.
- **Aggregate Roots:** `Resident`.
- **Enums:** `resident_status`, `residency_status`.
- **Dự kiến Configuration Files:** `ResidentConfiguration.cs`, `ResidentApartmentConfiguration.cs`.
- **Cross-Module References:** `residents.user_id` (FK -> `auth.users`), `resident_apartments.apartment_unit_id` (FK -> `apartments.apartment_units`), `created_by`, `updated_by` (FK -> `auth.users`). Lưu dạng scalar `Guid`.

### 4. Module `Apartments` (`src/Modules/Apartments/`)
- **Target Schema:** `apartments`
- **DbContext Name:** `ApartmentsDbContext`
- **Owned Tables (1):** `apartments.apartment_units`.
- **Proposed Entities:** `ApartmentUnit`.
- **Aggregate Roots:** `ApartmentUnit`.
- **Enums:** `master_data_status`.
- **Dự kiến Configuration Files:** `ApartmentUnitConfiguration.cs`.
- **Cross-Module References:** `apartment_units.building_id` (FK -> `property_assets.buildings`), `created_by`, `updated_by` (FK -> `auth.users`). Lưu dạng scalar `Guid`.

### 5. Module `PropertyAssets` (`src/Modules/PropertyAssets/`)
- **Target Schema:** `property_assets`
- **DbContext Name:** `PropertyAssetsDbContext`
- **Owned Tables (3):** `property_assets.buildings`, `property_assets.facilities`, `property_assets.equipment`.
- **Proposed Entities:** `Building`, `Facility`, `Equipment`.
- **Aggregate Roots:** `Building`, `Facility`, `Equipment`.
- **Enums:** `master_data_status`, `equipment_status`.
- **Dự kiến Configuration Files:** `BuildingConfiguration.cs`, `FacilityConfiguration.cs`, `EquipmentConfiguration.cs`.
- **Cross-Module References:** `created_by`, `updated_by` (FK -> `auth.users`). Toàn bộ lưu dạng scalar `Guid`.

### 6. Module `ServiceRequests` (`src/Modules/ServiceRequests/`)
- **Target Schema:** `service_requests`
- **DbContext Name:** `ServiceRequestsDbContext`
- **Owned Tables (4):** `service_requests.service_request_categories`, `service_requests.service_requests`, `service_requests.service_request_assignments`, `service_requests.service_request_activities`.
- **Proposed Entities:** `ServiceRequestCategory`, `ServiceRequest`, `ServiceRequestAssignment`, `ServiceRequestActivity`.
- **Aggregate Roots:** `ServiceRequestCategory`, `ServiceRequest`.
- **Enums:** `service_request_status`, `assignment_status`, `service_activity_type`.
- **Dự kiến Configuration Files:** `ServiceRequestCategoryConfiguration.cs`, `ServiceRequestConfiguration.cs`, `ServiceRequestAssignmentConfiguration.cs`, `ServiceRequestActivityConfiguration.cs`.
- **Cross-Module References:** `resident_id`, `resident_apartment_id` (FK -> `residents`); `building_id`, `facility_id`, `equipment_id` (FK -> `property_assets`); `apartment_unit_id` (FK -> `apartments`); `closed_by`, `staff_user_id`, `assigned_by`, `performed_by` (FK -> `auth.users`). Toàn bộ lưu dạng scalar `Guid`.

### 7. Module `Complaints` (`src/Modules/Complaints/`)
- **Target Schema:** `complaints`
- **DbContext Name:** `ComplaintsDbContext`
- **Owned Tables (3):** `complaints.complaints`, `complaints.complaint_followups`, `complaints.complaint_activities`.
- **Proposed Entities:** `Complaint`, `ComplaintFollowup`, `ComplaintActivity`.
- **Aggregate Roots:** `Complaint`.
- **Enums:** `complaint_status`, `complaint_followup_status`, `complaint_activity_type`.
- **Dự kiến Configuration Files:** `ComplaintConfiguration.cs`, `ComplaintFollowupConfiguration.cs`, `ComplaintActivityConfiguration.cs`.
- **Cross-Module References:**
  - Cột có FK: `resident_id`, `resident_apartment_id` (FK -> `residents`); `building_id`, `facility_id`, `equipment_id` (FK -> `property_assets`); `apartment_unit_id` (FK -> `apartments`); `closed_by`, `staff_user_id`, `assigned_by`, `performed_by` (FK -> `auth.users`).
  - **Cột Scalar Only (No FK):** `related_service_request_id` (tham chiếu scalar sang `service_requests.service_requests.id`, không tạo FK trong database).

### 8. Module `Maintenance` (`src/Modules/Maintenance/`)
- **Target Schema:** `maintenance`
- **DbContext Name:** `MaintenanceDbContext`
- **Owned Tables (5):** `maintenance.maintenance_schedules`, `maintenance.maintenance_tasks`, `maintenance.maintenance_assignments`, `maintenance.maintenance_task_activities`, `maintenance.maintenance_results`.
- **Proposed Entities:** `MaintenanceSchedule`, `MaintenanceTask`, `MaintenanceAssignment`, `MaintenanceTaskActivity`, `MaintenanceResult`.
- **Aggregate Roots:** `MaintenanceSchedule`, `MaintenanceTask`.
- **Enums:** `maintenance_schedule_status`, `maintenance_task_status`, `assignment_status`, `maintenance_activity_type`, `maintenance_result_status`.
- **Dự kiến Configuration Files:** `MaintenanceScheduleConfiguration.cs`, `MaintenanceTaskConfiguration.cs`, `MaintenanceAssignmentConfiguration.cs`, `MaintenanceTaskActivityConfiguration.cs`, `MaintenanceResultConfiguration.cs`.
- **Cross-Module References:**
  - Cột có FK: `building_id`, `facility_id`, `equipment_id` (FK -> `property_assets`); `created_by`, `updated_by`, `closed_by`, `staff_user_id`, `assigned_by`, `performed_by`, `submitted_by`, `reviewed_by` (FK -> `auth.users`).
  - **Cột Scalar Only (No FK):** `source_service_request_id` (tham chiếu scalar sang `service_requests.service_requests.id`), `source_complaint_id` (tham chiếu scalar sang `complaints.complaints.id`).

### 9. Module `Billing` (`src/Modules/Billing/`)
- **Target Schema:** `billing`
- **DbContext Name:** `BillingDbContext`
- **Owned Tables (5):** `billing.fee_types`, `billing.fee_rate_rules`, `billing.invoices`, `billing.invoice_items`, `billing.invoice_status_history`.
- **Proposed Entities:** `FeeType`, `FeeRateRule`, `Invoice`, `InvoiceItem`, `InvoiceStatusHistory`.
- **Aggregate Roots:** `FeeType`, `Invoice`.
- **Enums:** `master_data_status`, `invoice_status`.
- **Dự kiến Configuration Files:** `FeeTypeConfiguration.cs`, `FeeRateRuleConfiguration.cs`, `InvoiceConfiguration.cs`, `InvoiceItemConfiguration.cs`, `InvoiceStatusHistoryConfiguration.cs`.
- **Cross-Module References:** `invoices.apartment_unit_id` references `apartments.apartment_units`; `fee_rate_rules.building_id` references `property_assets.buildings`; `created_by`, `updated_by`, `issued_by`, `cancelled_by`, `changed_by` reference `auth.users`. Toàn bộ lưu dạng scalar `Guid` only; không tạo EF navigation hoặc physical FK xuyên module.

### 10. Module `Payments` (`src/Modules/Payments/`)
- **Target Schema:** `payments`
- **DbContext Name:** `PaymentsDbContext`
- **Owned Tables (2):** `payments.payments`, `payments.payment_status_history`.
- **Proposed Entities:** `Payment`, `PaymentStatusHistory`.
- **Aggregate Roots:** `Payment`.
- **Enums:** `payment_status`.
- **Dự kiến Configuration Files:** `PaymentConfiguration.cs`, `PaymentStatusHistoryConfiguration.cs`.
- **Cross-Module References:**
  - **Cột Scalar Only (No FK):** `invoice_id` (tham chiếu scalar sang `billing.invoices.id`), `submitted_by`, `confirmed_by`, `rejected_by`, `changed_by` (tham chiếu scalar sang `auth.users.id`). Không tạo EF navigation hoặc physical FK xuyên module.

### 11. Module `AiClassification` (`src/Modules/AiClassification/`)
- **Target Schema:** `ai_classification`
- **DbContext Name:** `AiClassificationDbContext`
- **Owned Tables (2):** `ai_classification.ai_request_classifications`, `ai_classification.ai_classification_reviews`.
- **Proposed Entities:** `AiRequestClassification`, `AiClassificationReview`.
- **Aggregate Roots:** `AiRequestClassification`.
- **Enums:** `ai_run_status`, `ai_review_decision`.
- **Dự kiến Configuration Files:** `AiRequestClassificationConfiguration.cs`, `AiClassificationReviewConfiguration.cs`.
- **Cross-Module References:**
  - **Cột Scalar Only (No FK):** `service_request_id` (tham chiếu scalar sang `service_requests.service_requests.id`), `predicted_category_id`, `final_category_id` (tham chiếu scalar sang `service_requests.service_request_categories.id`), `reviewed_by` (tham chiếu scalar sang `auth.users.id`). Không tạo EF navigation hoặc physical FK xuyên module.

### 12. Module `AiRecommendation` (`src/Modules/AiRecommendation/`)
- **Target Schema:** `ai_recommendation`
- **DbContext Name:** `AiRecommendationDbContext`
- **Owned Tables (2):** `ai_recommendation.ai_request_recommendations`, `ai_recommendation.ai_recommendation_reviews`.
- **Proposed Entities:** `AiRequestRecommendation`, `AiRecommendationReview`.
- **Aggregate Roots:** `AiRequestRecommendation`.
- **Enums:** `ai_run_status`, `ai_review_decision`.
- **Dự kiến Configuration Files:** `AiRequestRecommendationConfiguration.cs`, `AiRecommendationReviewConfiguration.cs`.
- **Cross-Module References:**
  - Cột có FK: `reviewed_by` (FK -> `auth.users`).
  - **Cột Scalar Only (No FK):** `service_request_id` (tham chiếu scalar sang `service_requests.service_requests.id`, không tạo FK).

### 13. Module `Communication` (`src/Modules/Communication/`)
- **Target Schema:** `communication`
- **DbContext Name:** `CommunicationDbContext`
- **Owned Tables (4):** `communication.notifications`, `communication.announcements`, `communication.announcement_versions`, `communication.announcement_audiences`.
- **Proposed Entities:** `Notification`, `Announcement`, `AnnouncementVersion`, `AnnouncementAudience`.
- **Aggregate Roots:** `Notification`, `Announcement`.
- **Enums:** `notification_type`, `announcement_status`, `announcement_audience_type`.
- **Dự kiến Configuration Files:** `NotificationConfiguration.cs`, `AnnouncementConfiguration.cs`, `AnnouncementVersionConfiguration.cs`, `AnnouncementAudienceConfiguration.cs`.
- **Cross-Module References:** `recipient_user_id`, `created_by`, `updated_by`, `changed_by`, `role_id`, `building_id`, `apartment_unit_id`, `resident_id` và `source_id` đều là logical scalar references. Không tạo navigation, EF relationship hoặc physical FK xuyên module; authorization/audience resolution thuộc Application/API phase.
- **Internal Physical FKs:** `announcement_audiences.announcement_id -> communication.announcements.id`; `announcement_versions.announcement_id -> communication.announcements.id`.
- **Baseline Channel:** In-app only. Email/SMS/push/SignalR delivery runtime là future/runtime scope, không nằm trong persistence prompt.
- **Communication History:** `announcement_versions` là append-only history; published/withdrawn communication không được sửa/xóa tùy tiện.

### 14. Module `AiChatbot` (`src/Modules/AiChatbot/`)
- **Target Schema:** N/A (Baseline không lưu lịch sử hội thoại chatbot vào database; dòng 10 và 1335 trong DBML ghi rõ: *"FE-12 Chatbot and FE-14 Reporting do not require source-of-truth tables in the baseline."*).
- **Source-of-truth Tables:** 0.

### 15. Module `Reporting` (`src/Modules/Reporting/`)
- **Target Schema:** N/A (Baseline tổng hợp dữ liệu từ các module nguồn; dòng 10 và 1336 trong DBML ghi rõ không có bảng riêng).
- **Source-of-truth Tables:** 0.

---

## E. CONFLICT REPORT & DECISION REQUIRED

### E.1 — DECISION REQUIRED: Shared Technical Persistence Ownership

**Tình trạng hiện tại:**
- DBML khai báo schema `shared` chứa 2 bảng: `shared.idempotency_records` và `shared.outbox_messages` (dòng 1082-1127).
- DBML ghi chú: *"Cross-cutting technical infrastructure in shared schema; business data remains module-owned... Each module may write only its own rows."*
- Solution hiện chỉ có 15 business module class libraries trong `src/Modules/`, không có `Shared` module hay technical persistence project nào trong solution baseline.
- `PropFlow_Backend_Rules.md` mục 2.1 liệt kê thư mục `src/Shared/Kernel`, `src/Shared/Infrastructure`, `src/Shared/AI`, nhưng không có project quản lý persistence hay migration riêng cho schema `shared`.

**Các lựa chọn kiến trúc:**

#### Lựa chọn A: Tạo một technical infrastructure project riêng (ví dụ: `src/Shared/PropFlow.Infrastructure.Shared.csproj` hoặc `src/Shared/Shared.Persistence.csproj`)
- **Tác động project count:** Tăng thêm 1 project trong solution (từ 20 lên 21 projects).
- **Module isolation:** Tốt. Các business module có thể tham chiếu project này để tái sử dụng `IdempotencyRecord` và `OutboxMessage` abstractions hoặc DbContext.
- **Transaction atomicity:** Để đạt tính atomicity của transactional outbox (outbox message được ghi trong cùng transaction với thay đổi nghiệp vụ), DbContext của business module cần kế thừa hoặc map chung bảng outbox, HOẶC dùng chung transaction qua `DbTransaction`.
- **Outbox ownership:** Tập trung technical entity tại một nơi.
- **Migration ownership:** Project này sẽ sở hữu `SharedDbContext` và quản lý migration riêng cho schema `shared`.
- **Dependency direction:** Business Modules -> Shared Infrastructure -> EF Core / Npgsql.
- **Phù hợp với Backend Rules:** Phù hợp với mục 2.1 (`src/Shared/Infrastructure/`) và mục 2.7 (Shared chỉ chứa technical/cross-cutting primitives).
- **Ưu điểm:** Tách biệt hoàn toàn technical concern khỏi `PropFlow.Api`, tái sử dụng dễ dàng trong kiểm thử.
- **Nhược điểm:** Tăng project count, cần cấu hình thêm migration pipeline cho project mới.

#### Lựa chọn B: Đặt technical shared persistence trong `PropFlow.Api` composition root
- **Tác động project count:** Không tăng project count (giữ nguyên 20 projects).
- **Module isolation:** Kém. Business modules không thể reference `PropFlow.Api` (ngược chiều phụ thuộc). Khi một business module cần ghi outbox message trong transaction của nó, module đó không thể truy cập DbContext được khai báo ở `PropFlow.Api`.
- **Transaction atomicity:** Rất khó đạt được trừ khi `PropFlow.Api` đóng vai trò coordinator hoặc phải expose technical entity ra ngoài.
- **Outbox ownership:** Thuộc về composition root.
- **Migration ownership:** `PropFlow.Api` sở hữu `SharedDbContext` và migration history của schema `shared`.
- **Dependency direction:** Không tự nhiên vì module nghiệp vụ cần ghi outbox nhưng không được phụ thuộc vào `PropFlow.Api`.
- **Ưu điểm:** Giữ nguyên 20 projects như đã scaffold.
- **Nhược điểm:** Phá vỡ Clean Architecture principles; business module không thể thực thi transactional outbox cục bộ trong transaction của chính nó.

#### Lựa chọn C: Mỗi module tự sở hữu idempotency và outbox persistence trong schema riêng của mình
- **Tác động project count:** Giữ nguyên 20 projects.
- **Module isolation:** Hoàn hảo. Không có shared schema; mỗi module có bảng outbox riêng (ví dụ: `billing.outbox_messages`, `payments.outbox_messages`).
- **Transaction atomicity:** Cực kỳ tự nhiên và đơn giản; outbox message được ghi trực tiếp vào DbContext của chính module đó trong cùng một `SaveChangesAsync()`.
- **Outbox ownership:** Module nào phát sinh event thì module đó sở hữu outbox message của mình.
- **Migration ownership:** Từng module tự chạy migration cho outbox của mình trong schema của module đó.
- **Dependency direction:** Độc lập hoàn toàn, không có shared database dependency.
- **Phù hợp với Backend Rules:** Hoàn toàn khớp với mục 2.8 ("Mỗi module sở hữu persistence boundary và migration history của mình").
- **Ưu điểm:** Đạt độ cô lập cao nhất, đúng chuẩn Modular Monolith / Clean Architecture, transaction hoàn toàn cục bộ.
- **Nhược điểm:** **YÊU CẦU SỬA DBML** (vì DBML hiện tại gom chung vào `shared.idempotency_records` và `shared.outbox_messages`). Vì quy tắc là "Không sửa DBML", lựa chọn này không thể tự ý áp dụng.

**RECOMMENDATION — NOT APPROVED:**
> Khuyến nghị **Lựa chọn A** (tạo `src/Shared/PropFlow.Infrastructure.Shared.csproj` hoặc tương đương) nếu muốn giữ nguyên DBML hiện tại mà không làm hỏng dependency rule; HOẶC **Lựa chọn C** nếu người dùng phê duyệt cập nhật DBML.
> **Trạng thái:** `DECISION REQUIRED` — Chờ người dùng phê duyệt trước khi tạo bất kỳ C# code nào cho schema `shared`.
> **Mức độ ảnh hưởng:** **BLOCKS BATCH 6 (Shared persistence)**, KHÔNG block Batch 1 đến Batch 5 của các business modules.

---

### E.2 — DECISION REQUIRED: Authentication Account Entity Name

- **Câu hỏi cần quyết định:** Bảng `auth.users` trong C# Entity nên đặt tên là `User` hay `UserAccount`?
- **Bối cảnh nghiệp vụ:**
  - DBML đặt tên bảng là `auth.users` (dòng 258).
  - Quy tắc cốt lõi của PropFlow (`.agents/rules/propflow-core.md` dòng 79 và `PropFlow_Backend_Rules.md` dòng 367) nhấn mạnh một invariant quan trọng: **`User Account != Resident != Apartment`**.
  - Bảng `auth.users` chỉ đại diện cho tài khoản đăng nhập (identity/credential), không phải là con người cư dân (`Resident` thuộc module Residents).
- **Các lựa chọn:**
  - **Lựa chọn 1: `UserAccount`** *(Khuyến nghị)* — Thể hiện rõ ràng ranh giới domain, tránh nhầm lẫn giữa tài khoản đăng nhập (`UserAccount`) và hồ sơ cư dân (`Resident`) trong code C#.
  - **Lựa chọn 2: `User`** — Bám sát tên bảng vật lý `users` trong DBML, ngắn gọn, nhưng dễ gây ngộ nhận là con người cư dân trong các biểu thức nghiệp vụ.
- **RECOMMENDATION — NOT APPROVED:** Lựa chọn 1 (`UserAccount`).
- **Mức độ ảnh hưởng:** **CẦN CHỐT TRƯỚC KHI SINH ENTITY CỦA AUTHENTICATION (BATCH 1)**.

---

### E.3 — RESOLVED: `auth.resident_verifications` email OTP only

- **Tình trạng cập nhật:** Quyết định nghiệp vụ mới chốt rằng cư dân chỉ xác minh đăng ký bằng email OTP. Không có SMS/phone OTP, Firebase Phone Authentication, Twilio Verify hoặc manual review trong luồng đăng ký cư dân.
- **Căn cứ giải quyết:**
  - `auth.users.phone_number` được giữ làm thông tin liên hệ; `phone_verified` bị loại bỏ.
  - `auth.resident_verifications.method_code` bị loại bỏ vì hệ thống không còn nhiều phương thức verification.
  - `reviewed_by`, reviewer relationship và `failure_reason` bị loại bỏ khỏi Authentication resident verification vì email OTP không có nhân viên duyệt thủ công.
  - `verification_status` của resident verification chỉ gồm `PENDING`, `VERIFIED`, `EXPIRED`, `CANCELLED`.
  - OTP chỉ được lưu bằng `verification_code_hash`; plaintext OTP không được lưu trong database.
- **Trạng thái:** `RESOLVED` — Corrective migration của `AuthenticationDbContext` chịu trách nhiệm đưa database về mô hình email OTP.

---

### E.4 — RESOLVED: Giao thoa giữa `Authentication` và `Administration`

- **Tình trạng:** Bảng `auth.users` lưu identity/credential; việc gán vai trò (`user_role_assignments`) và phân quyền tòa nhà (`user_building_accesses`) do module `Administration` sở hữu.
- **Căn cứ giải quyết:**
  - Quy tắc `RBAC authorization != User Account` đã được phê duyệt ở `PropFlow_Backend_Rules.md` mục 9 và 11.
  - Phân chia ownership hoàn toàn rõ ràng: Module `Authentication` sở hữu 4 bảng của schema `auth`; module `Administration` sở hữu 8 bảng của schema `administration`.
  - Khi cấp token JWT, `Authentication` sẽ truy vấn thông tin quyền thông qua hợp đồng công khai giữa các module hoặc composition root.
  - **Lưu ý chuẩn hóa:** Các tên gọi interface như `IAdministrationModuleContract`, `IResidentsModuleContract`, `IApartmentsModuleContract` được ghi nhận là **proposed future contracts** (hợp đồng đề xuất cho giai đoạn triển khai ứng dụng), chưa phải là specification đã đóng đinh trong architecture rules.
- **Trạng thái:** `RESOLVED` — Không block model generation.

---

### E.5 — RESOLVED: Finance UI Grouping vs Backend Modules

- **Tình trạng:** Trên giao diện có thể gom chung FE-08 (Hóa đơn) và FE-09 (Thanh toán) thành menu "Tài chính".
- **Căn cứ giải quyết:**
  - `PropFlow_Backend_Rules.md` mục 11 và DBML mục 6 & 7 tách biệt hoàn toàn hai module: `Billing` (schema `billing`) và `Payments` (schema `payments`).
  - Cột `payments.payments.invoice_id` là scalar reference độc lập, không có physical FK và không có EF navigation property.
- **Trạng thái:** `RESOLVED` — Không block model generation.

---

## F. CONSTRAINT ENFORCEMENT & INTEGRITY MAPPING

Rà soát toàn bộ 14 nhóm Data Integrity Invariants bắt buộc từ `docs/database/PropFlow.dbml` (dòng 1343-1416), đối chiếu và phân bổ chính xác vào các tầng thực thi (không hạ cấp bất kỳ database constraint nào):

| STT | Invariant / Constraint Name | Chi tiết Ràng buộc Nghiệp vụ & Kỹ thuật | Tầng Thực thi Bắt buộc (Enforcement Layer) |
| :--- | :--- | :--- | :--- |
| **1** | `residency_date_check` | `resident_apartments`: `CHECK (end_date IS NULL OR end_date >= start_date)`. Đồng thời ngăn chặn các khoảng thời gian cư trú active bị trùng lặp đè lên nhau cho cùng một quan hệ cư dân - căn hộ. | **PostgreSQL CHECK Constraint** (migration SQL) + **Application Validation** (kiểm tra khoảng thời gian overlap trước khi lưu) + **Integration Test** |
| **2** | `user_building_access_current_unique` | `user_building_accesses`: Một user có thể có nhiều lần được cấp quyền vào cùng building trong lịch sử, nhưng chỉ được có tối đa một record active tại một thời điểm (`revoked_at IS NULL`). | **PostgreSQL partial UNIQUE Index**: `UNIQUE(user_id, building_id) WHERE revoked_at IS NULL` + **Application Validation/Concurrency** |
| **3** | `single_active_sr_assignment` | `service_request_assignments`: Mỗi yêu cầu dịch vụ chỉ có tối đa 1 phân công ở trạng thái active (`ASSIGNED` hoặc `IN_PROGRESS`) tại một thời điểm. | **Application Validation** + **Concurrency Control** (Version/RowVersion hoặc Pessimistic Lock) + **Integration Test** |
| **4** | `single_active_maint_assignment` | `maintenance_assignments`: Mỗi nhiệm vụ bảo trì chỉ có tối đa 1 phân công ở trạng thái active tại một thời điểm. | **Application Validation** + **Concurrency Control** + **Integration Test** |
| **5** | `payment_amount_and_status_check` | `payments`: `CHECK (amount > 0)`, filtered unique `reference_number` khi khác NULL. Khi trạng thái là `CONFIRMED` thì bắt buộc `confirmed_by` và `confirmed_at` khác NULL, đồng thời `rejection_reason` phải NULL. Khi `REJECTED` thì bắt buộc có `rejected_by` và `rejected_at`. | **PostgreSQL CHECK Constraint** (`amount > 0`) + **Domain Invariant** (state transition methods trong Entity bảo vệ trạng thái) |
| **6** | `payment_concurrency_and_overpay` | Giao dịch tài chính phải concurrency-safe và idempotent (sử dụng `idempotency_key`). Tổng số tiền các khoản thanh toán `CONFIRMED` không được vượt quá số dư phải trả của Hóa đơn. | **Application Transaction** + **Idempotency Check** + **Domain Service** + **Integration Test** |
| **7** | `fee_rate_rule_date_and_amount_check` | `fee_rate_rules`: `CHECK (effective_to IS NULL OR effective_to >= effective_from)`. `CHECK (minimum_amount IS NULL OR maximum_amount IS NULL OR minimum_amount <= maximum_amount)`. | **PostgreSQL CHECK Constraints** (migration) + **Domain Validation** |
| **8** | `invoice_period_and_dates_check` | `invoices`: `CHECK (billing_period_end >= billing_period_start)`. Các mốc thời gian `issued_at`, `cancelled_at` phải khớp logic với `invoice_status`. | **PostgreSQL CHECK Constraint** (kỳ hóa đơn) + **Domain State Invariant** |
| **9** | `attempt_no_and_scores_check` | `maintenance_results`, `ai_request_classifications`, `ai_request_recommendations`: `CHECK (attempt_no >= 1)`. Các trường AI: `CHECK (input_tokens >= 0 AND output_tokens >= 0 AND latency_ms >= 0)`, `CHECK (confidence_score >= 0 AND confidence_score <= 1)` nếu cột confidence tồn tại. FE10 `ai_request_classifications`: `SUCCESS` bắt buộc có `predicted_category_id` và không có `error_message`; `FAILED` bắt buộc có `error_message` và không có `predicted_category_id`, `confidence_score`, `reasoning_summary`. FE11 `ai_request_recommendations`: `SUCCESS` bắt buộc có `suggested_priority_code` và không có `error_message`; `FAILED` bắt buộc có `error_message` và không có suggested priority / maintenance / action / resource / reasoning output. | **PostgreSQL CHECK Constraints** (migration) + **Domain Validation** |
| **10** | `announcement_audience_target_check` | `announcement_audiences`: Tùy theo `audience_type`, chỉ duy nhất 1 scalar target id tương ứng được mang giá trị; các loại `ALL_*` bắt buộc tất cả target ids đều `NULL`. Không có physical FK xuyên module. | **PostgreSQL CHECK Constraint** (migration SQL biểu diễn điều kiện logic tương hỗ) + **Application Validator** |
| **10A** | `announcement_audience_scope_unique` | `announcement_audiences`: Một Announcement không được chứa hai audience giống nhau trong cùng scope. PostgreSQL partial UNIQUE indexes: `(announcement_id, audience_type)` với `ALL_USERS`/`ALL_RESIDENTS`; `(announcement_id, role_id)` với `ROLE`; `(announcement_id, building_id)` với `BUILDING`; `(announcement_id, apartment_unit_id)` với `APARTMENT`; `(announcement_id, resident_id)` với `RESIDENT`. Target IDs vẫn là logical scalar references, không physical FK xuyên module. | **PostgreSQL partial UNIQUE Indexes** + **Domain Aggregate Invariant** + **Integration Test** |
| **11** | `consistent_building_context_check` | Tham chiếu tài sản trong thiết bị, yêu cầu dịch vụ, khiếu nại, bảo trì phải thuộc cùng một ngữ cảnh tòa nhà (`building_id` nhất quán giữa căn hộ, tiện ích, thiết bị). | **Application Validation** (truy vấn kiểm tra tính nhất quán building context trước khi ghi) + **Integration Test** |
| **12** | `restrict_delete_behavior` | Mọi quan hệ lịch sử nghiệp vụ bắt buộc sử dụng `ON DELETE RESTRICT` hoặc `NO ACTION`; ưu tiên đánh dấu status / deactivation thay vì cascade delete. | **EF Core Fluent Configuration** (`OnDelete(DeleteBehavior.Restrict)`) + **PostgreSQL Foreign Key DDL** |
| **13** | **APPROVED MONEY BASELINE** | Tiền tệ duy nhất: **VND**. Không dùng float/double. C# dùng `decimal`; PostgreSQL dùng `numeric(18,0)`. Khối lượng tính toán (quantity) dùng `numeric(18,4)`. Mỗi `InvoiceItem.LineAmount` phải được làm tròn về 0 chữ số thập phân bằng `MidpointRounding.AwayFromZero` TRƯỚC khi cộng tổng `Subtotal`/`TotalAmount`. Hóa đơn `ISSUED` là immutable, không tính lại khi biểu phí thay đổi. Tổng confirmed payment không vượt dư nợ. | **Domain Model** (công thức tính & làm tròn) + **EF Core Column Type Mapping** (`HasColumnType("numeric(18,0)")`) + **Integration Test** |
| **13A** | **APPROVED TIMEZONE BASELINE** | Instant/event/audit timestamps dùng PostgreSQL `timestamptz` (lưu UTC). Calendar-only business dates dùng PostgreSQL `date`. Mỗi Building sở hữu `time_zone_id` là IANA timezone ID (mặc định `Asia/Ho_Chi_Minh`). Mọi tính toán quá hạn, báo cáo ngày phải quy đổi theo timezone của Building. API trả chuẩn ISO-8601. Không lưu display string hay raw offset (+07:00). | **PostgreSQL Column Types** (`timestamptz`, `date`) + **Domain Timezone Conversion Service** + **Integration Test** |
| **14** | `cross_module_scalar_references` | Các tham chiếu vận hành liên module (Payments -> Invoices, Complaints -> ServiceRequests, Maintenance -> ServiceRequests/Complaints, AI -> ServiceRequests) là scalar reference, không dùng cross-module EF navigation; kiểm tra tồn tại qua Module Contracts / Application rules. | **Application Layer Existence Check** + **Module Contracts** + **Integration Test** |

---

## G. RECOMMENDED GENERATION ORDER

Thứ tự triển khai mã nguồn C# Models, DbContexts và EF Core Configurations phải tuân thủ nghiêm ngặt nguyên tắc phụ thuộc dữ liệu nền tảng.

**Lưu ý bắt buộc:** Prompt triển khai model tiếp theo **phải bám theo generation order đã duyệt** dưới đây. Không triển khai đơn lẻ các module con nếu các module gốc chưa được tạo.

### Batch 1: Core Domain Baseline (Hoàn thành — 4 Modules, 10 Entities, 4 DbContexts, 4 Initial Migrations, Refactored Prompt 4.1)
1. **`PropertyAssets`** (`src/Modules/PropertyAssets/`): `Building`, `Facility`, `Equipment` — ✅ ĐÃ TRIỂN KHAI & REFACTOR RICH/SIMPLE DOMAIN
2. **`Apartments`** (`src/Modules/Apartments/`): `ApartmentUnit` — ✅ ĐÃ TRIỂN KHAI & REFACTOR SIMPLE DOMAIN
3. **`Authentication`** (`src/Modules/Authentication/`): `UserAccount`, `RefreshToken`, `PasswordResetToken`, `ResidentVerification` — ✅ ĐÃ TRIỂN KHAI & REFACTOR RICH/TOKEN DOMAIN
4. **`Residents`** (`src/Modules/Residents/`): `Resident`, `ResidentApartment` — ✅ ĐÃ TRIỂN KHAI & REFACTOR RICH DOMAIN

### Batch 2: Core Authorization & Administration (Hoàn thành — 1 Module, 8 Entities, 1 DbContext, Refactored Prompt 4.1)
5. **`Administration`** (`src/Modules/Administration/`): `Role`, `Permission`, `RolePermission`, `UserRoleAssignment`, `UserBuildingAccess`, `UserAccessHistory`, `SystemConfiguration`, `AuditLog` — ✅ ĐÃ TRIỂN KHAI & REFACTOR (Rich/Join/Append-only/Simple Domain)

### Batch 3: Core Operations (Yêu cầu Dịch vụ, Phản ánh & Bảo trì)
6. **`ServiceRequests`** (`src/Modules/ServiceRequests/`): `ServiceRequestCategory`, `ServiceRequest`, `ServiceRequestAssignment`, `ServiceRequestActivity`.
7. **`Complaints`** (`src/Modules/Complaints/`): `Complaint`, `ComplaintFollowup`, `ComplaintActivity`.
8. **`Maintenance`** (`src/Modules/Maintenance/`): `MaintenanceSchedule`, `MaintenanceTask`, `MaintenanceAssignment`, `MaintenanceTaskActivity`, `MaintenanceResult`.

### Batch 4: Finance (Tài chính & Thanh toán)
9. **`Billing`** (`src/Modules/Billing/`): `FeeType`, `FeeRateRule`, `Invoice`, `InvoiceItem`, `InvoiceStatusHistory`.
10. **`Payments`** (`src/Modules/Payments/`): `Payment`, `PaymentStatusHistory`.

### Batch 5: AI & Communication
11. **`AiClassification`** (`src/Modules/AiClassification/`): `AiRequestClassification`, `AiClassificationReview`.
12. **`AiRecommendation`** (`src/Modules/AiRecommendation/`): `AiRequestRecommendation`, `AiRecommendationReview`.
13. **`Communication`** (`src/Modules/Communication/`): `Notification`, `Announcement`, `AnnouncementVersion`, `AnnouncementAudience`.

### Batch 6: Shared Technical Persistence (Chờ quyết định E.1)
14. **Hạ tầng kỹ thuật dùng chung** (`shared.idempotency_records`, `shared.outbox_messages`): **Tạm dừng cho đến khi con người phê duyệt phương án tại mục E.1.**

---

## H. FINAL VALIDATION CHECKLIST

| # | Hạng mục kiểm tra | Kết quả kiểm tra | Chi tiết đối soát |
|:--|:---|:---|:---|
| 1 | Tổng số bảng ánh xạ đủ 47/47 | ✅ ĐẠT | Đúng 47 bảng thuộc 14 schemas trong DBML. |
| 2 | Mỗi bảng xuất hiện đúng 1 lần trong Ownership Matrix | ✅ ĐẠT | 47 bảng tương ứng 47 dòng tại Mục B, không trùng lặp, không bỏ sót. |
| 3 | Mỗi relationship được phân loại riêng biệt | ✅ ĐẠT | 138 quan hệ cột đã được phân loại (38 within-module, 100 cross-module). |
| 4 | PostgreSQL FK policy bám đúng DBML | ✅ ĐẠT | 94 quan hệ có `ref:` trong DBML bắt buộc có physical FK; 6 quan hệ ghi chú scalar không tạo FK. |
| 5 | Hai bảng `shared` không gán owner tùy tiện | ✅ ĐẠT | Đánh dấu `UNRESOLVED TECHNICAL OWNER` và tạo mục `DECISION REQUIRED E.1` với 3 lựa chọn. |
| 6 | Mọi `DECISION REQUIRED` có câu hỏi và lựa chọn rõ ràng | ✅ ĐẠT | Mục E.1 (Shared Owner) và E.2 (Entity Name) trình bày đầy đủ các phương án, ưu/nhược điểm. |
| 7 | Tên bảng đầy đủ, không viết tắt | ✅ ĐẠT | Đã dùng toàn bộ tên chuẩn: `service_request_categories`, `service_request_assignments`, `service_request_activities`, `maintenance_schedules`, `maintenance_tasks`, `maintenance_results`. |
| 8 | Constraint enforcement không bị hạ cấp | ✅ ĐẠT | Toàn bộ 14 nhóm invariant từ DBML được ghi nhận chính xác tầng thực thi (PostgreSQL CHECK, Partial Index, Domain). |
| 9 | Đếm cross-module relationship có thể truy vết | ✅ ĐẠT | Đã đếm từng cột nguồn cụ thể: 100 cross-module (94 có FK + 6 scalar-only). |
| 10 | Generation order Batch 1 chuẩn xác | ✅ ĐẠT | Đúng thứ tự (1) PropertyAssets -> (2) Apartments -> (3) Authentication -> (4) Residents. |
| 11 | Chất lượng định dạng Markdown | ✅ ĐẠT | Đã loại bỏ toàn bộ ký tự escape thừa (`\*\*`, `\|`, `\_`), inline code hiển thị chuẩn. |
| 12 | Không sửa source code hoặc DBML | ✅ ĐẠT | Không có file C#, Migration hay file DBML nào bị sửa đổi. |

---

## FINAL VERDICT

> ### 🛑 **MODEL GENERATION BLOCKED**
>
> **Lý do dừng:**
> 1. **DECISION REQUIRED E.1 (Bắt buộc cho Shared Persistence):** Cần quyết định phương án sở hữu cho hai bảng `shared.idempotency_records` và `shared.outbox_messages` (Lựa chọn A: Thêm project `PropFlow.Infrastructure.Shared`; Lựa chọn B: Đặt trong `PropFlow.Api`; Lựa chọn C: Sửa DBML để mỗi module tự quản lý).
> 2. **DECISION REQUIRED E.2 (Bắt buộc cho Batch 1):** Cần phê duyệt tên gọi C# Entity cho bảng `auth.users`: `UserAccount` (theo domain invariant `User Account != Resident != Apartment`) hay `User` (theo tên bảng).
>
> Sau khi người dùng đưa ra quyết định cho các mục trên, hệ thống sẽ sẵn sàng triển khai mã nguồn tuần tự theo đúng **Batch 1 (PropertyAssets -> Apartments -> Authentication -> Residents)**.
