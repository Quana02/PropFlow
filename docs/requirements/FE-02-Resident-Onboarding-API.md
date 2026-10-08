# FE-02 Resident Onboarding API

`POST /api/v1/residents` tạo một hồ sơ cư dân cùng quan hệ với căn hộ bằng một request duy nhất.

## Contract

`ApartmentUnitId` thuộc request gốc và luôn bắt buộc. `RelationshipKind` quyết định quan hệ được tạo:

| RelationshipKind | Residency | Kết quả |
| --- | --- | --- |
| `OWNER_ONLY` | Bắt buộc `null` / omitted | Tạo ownership, không tạo `ResidentApartment` |
| `RESIDENT_ONLY` | Bắt buộc | Tạo `ResidentApartment`, không tạo ownership |
| `OWNER_AND_RESIDENT` | Bắt buộc | Tạo cả ownership và `ResidentApartment`; server áp dụng `OWNER_OCCUPIED` |

`OWNER_ONLY` không được gửi payload residency. API trả lỗi validation an toàn cho payload mâu thuẫn hoặc thiếu `ApartmentUnitId`.

Với residency là `HOUSEHOLD_MEMBER`, request phải có household head hợp lệ cùng căn hộ và quan hệ với chủ hộ. Ownership không tự suy ra hoặc thay đổi household role; occupancy luôn được suy ra từ `ResidentApartment` đang hiệu lực.

`RESIDENT_ONLY` chỉ chấp nhận `TENANT` hoặc `AUTHORIZED_OCCUPANT`; `OWNER_OCCUPIED` chỉ hợp lệ khi chính Resident có current ownership của Apartment. `OWNER_AND_RESIDENT` lấy `OWNER_OCCUPIED` là giá trị authoritative, không suy ra ownership từ household head.

## Resident identity và duplicate detection

- Resident identity duy nhất theo cặp `(IdentityType, IdentityNumber)` đã chuẩn hóa. `IdentityType` trim + uppercase; `IdentityNumber` chỉ lưu chữ số, bỏ khoảng trắng và dấu phân cách. Cùng số khác loại giấy tờ không tự động bị xem là cùng Resident.
- Email, nếu có, duy nhất sau `trim + lowercase`. Quy tắc này giữ FE-01 ở trạng thái khớp chính xác một Resident.
- Phone bắt buộc cho onboarding nhưng không phải khóa unique toàn cục; nhiều người có thể dùng chung số liên hệ. Họ tên và ngày sinh chỉ là tín hiệu audit, không phải khóa chặn tạo.
- Create và Update dùng chung policy; Update loại trừ chính Resident đang sửa. Conflict trả HTTP 409 với `RESIDENT_IDENTITY_ALREADY_EXISTS` hoặc `RESIDENT_EMAIL_ALREADY_EXISTS` và field error tương ứng.
- PostgreSQL advisory transaction lock bảo vệ hai khóa canonical trong luồng Create/Update, ngăn hai request đồng thời cùng commit. Toàn bộ onboarding Resident + ownership + residency vẫn rollback nguyên tử khi duplicate.

### Audit dữ liệu 2026-10-03

Duplicate `RES-000003` đã được reconcile vào survivor `RES-000002` trong DEV bằng script one-off có snapshot, precondition và transaction `SERIALIZABLE`. Ownership P202 trùng của duplicate bị xóa như một duplicate relationship; không remap, không gán `EndDate` và không tạo ownership history giả. Residency P202 của survivor được giữ nguyên.

Migration `20261003103000_EnforceResidentCanonicalUniqueness` tạo hai PostgreSQL expression index:

- `ux_residents_identity_canonical`: unique theo `upper(btrim(identity_type))` và `regexp_replace(identity_number, '[^0-9]', '', 'g')` khi cả hai canonical value không rỗng.
- `ux_residents_email_canonical`: unique theo `lower(btrim(email))` khi email không null/rỗng.

Phone tiếp tục không unique. Hai ownership history P302 tương tự của `RES-000002` là data-quality item riêng, còn tồn tại và không bị thay đổi trong reconciliation này.

## Residency cardinality và lifecycle

- Một Resident có thể có `0..N` active `ResidentApartment` ở các Apartment khác nhau.
- Cùng Resident + cùng Apartment chỉ có tối đa một active residency tại cùng thời điểm. Ended residency không ngăn quay lại; re-entry tạo row mới, không revive history.
- Add Residency tạo thêm active residency và không end residency cũ.
- Move Residency end source và create target trong một transaction. Nếu create target thất bại, source vẫn `ACTIVE`; không có partial state.

## Household semantics

`HouseholdRole` (`HOUSEHOLD_HEAD`, `HOUSEHOLD_MEMBER`) và `ResidencyType` (`OWNER_OCCUPIED`, `TENANT`, `AUTHORIZED_OCCUPANT`) độc lập. Member phải tham chiếu một active head cùng Apartment, có `RelationshipToHead`, không self-reference/cycle. Member không bắt buộc cùng `ResidencyType` với head; `OWNER_OCCUPIED` head + `AUTHORIZED_OCCUPANT` member và `OWNER_OCCUPIED` head + `TENANT` member đều hợp lệ nếu các constraint khác đúng.

Resident Detail hiển thị current ownership `0..N`, ownership history, current residency `0..N` và residency history; hỗ trợ Add/Move/End Residency và Add/End Ownership. Mutation dùng confirmation popup, known blocker dùng information popup, success dùng toast.

## Liên hệ với FE-01 Registration

Self-registration phải khớp đúng cùng một Resident theo bốn giá trị normalized: email, phone, identity type và identity number. Resident phải `ACTIVE`, chưa liên kết tài khoản và có active residency hoặc current ownership. `OWNER_ONLY`, `RESIDENT_ONLY` và `OWNER_AND_RESIDENT` đều có thể đăng ký khi quan hệ hiện hành tương ứng còn hiệu lực; history đơn thuần không đủ. Quy tắc eligibility không tạo residency, không cấp quyền occupant/household và không thay thế authorization theo từng resource.
