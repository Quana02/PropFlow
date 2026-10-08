# PropFlow — Database Ownership & Module Mapping

Tài liệu này ánh xạ schema hiện hành trong `docs/database/PropFlow.dbml` theo kiến trúc modular monolith. Bối cảnh vận hành chuẩn là **một deployment PropFlow cho một chung cư hiện hành**.

## 1. Baseline bắt buộc

- Toàn bộ dữ liệu trong một deployment PropFlow mặc nhiên thuộc cùng một chung cư.
- `property_assets.buildings` là hồ sơ thông tin chung; không phải tenant, authorization scope, filter hay ngữ cảnh người dùng có thể chọn.
- Không tồn tại `administration.user_building_accesses`, Building audience, Building selector, báo cáo xuyên chung cư hoặc User–Building assignment.
- Không lưu `building_id` lặp lại trong aggregate nghiệp vụ. Dữ liệu của deployment mặc nhiên thuộc tòa nhà hiện tại.
- Mỗi module sở hữu schema/migration của mình. Module khác chỉ truy cập qua narrow Contracts hoặc integration event; không dùng cross-module EF navigation/DbContext.
- Reporting chỉ đọc/tổng hợp và không sở hữu source business record.

## 2. Ownership matrix

| Schema | Các bảng | Module sở hữu |
|---|---|---|
| `auth` | `users`, `refresh_tokens`, `password_reset_tokens`, `resident_verifications` | Authentication |
| `administration` | `roles`, `permissions`, `role_permissions`, `user_role_assignments`, `user_access_history`, `system_configurations`, `audit_logs` | Administration |
| `residents` | `residents`, `resident_apartments` (residency type, household role, household-head residency và relationship-to-head) | Residents / FE-02 |
| `property_assets` | `buildings`, `facilities`, `equipment` | PropertyAssets |
| `apartments` | `apartment_unit_types`, `apartment_units`, `apartment_ownerships` | Apartments / FE-03 |
| `service_requests` | `service_request_categories`, `service_requests`, `service_request_assignments`, `service_request_activities` | ServiceRequests |
| `complaints` | `complaints`, `complaint_followups`, `complaint_activities` | Complaints |
| `maintenance` | `maintenance_schedules`, `maintenance_tasks`, `maintenance_assignments`, `maintenance_task_activities`, `maintenance_results` | Maintenance |
| `billing` | `fee_types`, `fee_rate_rules`, `invoices`, `invoice_items`, `invoice_status_history` | Billing |
| `payments` | `payments`, `payment_status_history` | Payments |
| `shared` | `idempotency_records`, `outbox_messages` | Shared technical persistence |
| `ai_classification` | `ai_request_classifications`, `ai_classification_reviews` | AiClassification |
| `ai_recommendation` | `ai_request_recommendations`, `ai_recommendation_reviews` | AiRecommendation |
| `communication` | `notifications`, `announcements`, `announcement_versions`, `announcement_audiences` | Communication |

Tổng cộng: **46 bảng** trong **14 PostgreSQL schema**. Reporting và AiChatbot hiện không có source-of-truth table riêng trong DBML.

## 3. Cấu trúc building scope đã loại bỏ

| Cấu trúc cũ | Quyết định hiện hành |
|---|---|
| `administration.user_building_accesses` | Xóa; role/permission và business assignment thay thế building access scope |
| `apartments.apartment_units.building_id` | Xóa; `unit_number` unique toàn deployment |
| `property_assets.facilities.building_id` | Xóa; `code` unique toàn deployment |
| `property_assets.equipment.building_id` | Xóa; `code` unique toàn deployment |
| `service_requests.service_requests.building_id` | Xóa |
| `complaints.complaints.building_id` | Xóa |
| `maintenance.maintenance_schedules.building_id` | Xóa |
| `maintenance.maintenance_tasks.building_id` | Xóa |
| `billing.fee_rate_rules.building_id` | Xóa |
| `communication.announcement_audiences.building_id` | Xóa; audience type `BUILDING` cũng bị loại |

## 4. Cross-module access policy

```text
Consumer module
    -> OwnerModule.Contracts (narrow DTO/query/command)
    -> Owner module implementation
    -> Owner module DbContext
```

- Không expose `DbContext`, repository, `IQueryable` hoặc domain entity qua Contracts.
- Cross-module references là stable scalar IDs; không tạo EF navigation xuyên module.
- Physical FK chỉ tồn tại khi `PropFlow.dbml` khai báo `ref:`. Scalar-only references được kiểm tra ở Application layer/Contracts.
- `residents.resident_apartments.apartment_unit_id` và `apartments.apartment_ownerships.owner_resident_id` là logical scalar references qua module boundary; DBML không mô tả physical FK cho hai cột này.
- Reporting Administration Overview lấy aggregate từ Apartments, Residents, PropertyAssets và ServiceRequests qua source contracts.

## 5. Invariants của chung cư hiện hành

1. `property_assets.buildings` cung cấp hồ sơ và timezone chung cho deployment; không dùng nó làm business scope.
2. Timezone của Building phải là timezone ID hợp lệ và là nguồn cho mọi phép tính ngày cục bộ.
3. ACTIVE apartment không có ResidentApartment effective tại ngày cục bộ hiện tại là vacant; không tạo cột/status VACANT.
4. Không dùng UserAccount role để đếm Resident business entity.
5. ADMIN không trở thành business super-user; quyền đọc aggregate Reporting không cấp quyền CRUD dữ liệu nguồn.
6. FE-02 sở hữu Resident và residency: `HOUSEHOLD_HEAD`/`HOUSEHOLD_MEMBER` là HouseholdRole, còn `OWNER_OCCUPIED`/`TENANT`/`AUTHORIZED_OCCUPANT` là ResidencyType. Ownership khác residency; household head không mặc định là owner.
7. FE-03 sở hữu Apartment master, danh mục `apartments.apartment_unit_types` và `apartments.apartment_ownerships`. `apartment_units.apartment_unit_type_id` là physical FK nội bộ cùng Apartments module; tên loại là unique theo normalized lower/trim semantics. Một apartment có thể có 0..N ownership hiện hành; cùng một resident chỉ có tối đa một ownership hiện hành trên cùng apartment. `owner_resident_id` là logical scalar reference đến Resident, được xác thực/đọc qua Residents public Contracts; Apartments không truy cập ResidentsDbContext. Ownership không tạo hoặc thay đổi quan hệ cư trú.
8. ApartmentStatus khác occupancy: ACTIVE/INACTIVE là trạng thái quản lý; occupancy được suy diễn từ active residency. Owner không tự trở thành occupant nếu không có ResidentApartment active. Tuy nhiên, apartment chỉ được chuyển INACTIVE khi đồng thời không có active residency và không có current ownership (`EndDate IS NULL`); apartment INACTIVE không được nhận current ownership mới.
9. FE-02 canonical Resident identity là normalized `(IdentityType, IdentityNumber)` và unique toàn cục; normalized non-null email cũng unique. Phone bắt buộc cho onboarding mới nhưng không unique trong Resident domain.
10. Một Resident có thể có `0..N` active `resident_apartments` ở các Apartment khác nhau. Cùng Resident + Apartment không được overlap active; ended row không bị revive khi re-entry.
11. FE-01 sở hữu account/registration. Authentication chỉ gọi Residents public Contracts; Residents có thể compose current ownership qua Apartments public Contracts. Không module nào truy cập DbContext của module khác.

## 6. Migration policy

- Giữ historical migrations để EF Core nâng cấp database theo chuỗi; `building_id` trong migration cũ chỉ phản ánh lịch sử schema, không phải khả năng chọn nhiều chung cư của live model.
- Model snapshot và migration đích phải khớp `PropFlow.dbml` hiện hành.
- Trước khi áp dụng migration xóa building scope lên database cũ, phải xác nhận dữ liệu đã chuẩn hóa về đúng một Building; không được âm thầm trộn dữ liệu nhiều Building.
