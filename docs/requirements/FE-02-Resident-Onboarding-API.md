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

Migration `20261003103000_EnforceResidentCanonicalUniqueness` tạo hai PostgreSQL expression index:

- `ux_residents_identity_canonical`: unique theo `upper(btrim(identity_type))` và `regexp_replace(identity_number, '[^0-9]', '', 'g')` khi cả hai canonical value không rỗng.
- `ux_residents_email_canonical`: unique theo `lower(btrim(email))` khi email không null/rỗng.

Phone tiếp tục không unique. Lịch sử reconciliation dữ liệu DEV không phải quy trình migration cho môi trường khác.

## Residency cardinality và lifecycle

- Một Resident có thể có `0..N` active `ResidentApartment` ở các Apartment khác nhau.
- Cùng Resident + cùng Apartment chỉ có tối đa một active residency tại cùng thời điểm. Ended residency không ngăn quay lại; re-entry tạo row mới, không revive history.
- Add Residency tạo thêm active residency và không end residency cũ.
- Move Residency end source và create target trong một transaction. Nếu create target thất bại, source vẫn `ACTIVE`; không có partial state.

## Household semantics

`HouseholdRole` (`HOUSEHOLD_HEAD`, `HOUSEHOLD_MEMBER`) và `ResidencyType` (`OWNER_OCCUPIED`, `TENANT`, `AUTHORIZED_OCCUPANT`) độc lập. Member phải tham chiếu một active head cùng Apartment, có `RelationshipToHead`, không self-reference/cycle. Member không bắt buộc cùng `ResidencyType` với head; `OWNER_OCCUPIED` head + `AUTHORIZED_OCCUPANT` member và `OWNER_OCCUPIED` head + `TENANT` member đều hợp lệ nếu các constraint khác đúng.

Resident Detail hiển thị current ownership `0..N`, ownership history, current residency `0..N` và residency history; hỗ trợ Add/Move/End Residency và Add/End Ownership. Mutation dùng confirmation popup, known blocker dùng information popup, success dùng toast.

## Liên hệ với FE-01 Registration

Self-registration phải khớp đúng cùng một Resident theo bốn giá trị normalized: email, phone, identity type và identity number. Resident phải `ACTIVE`, chưa liên kết tài khoản và có current ownership hoặc active residency với vai trò `HOUSEHOLD_HEAD` (quyết định người dùng ngày 03/11/2026). Chủ hộ không sở hữu chỉ được đăng ký khi có cư trú `TENANT` hiện hành tại căn hộ không có chủ sở hữu hiện hành, tức thuê căn hộ của đơn vị vận hành. Chủ hộ thuê căn có chủ sở hữu khác không đủ điều kiện. Thành viên hộ không có quyền sở hữu không được tự đăng ký; history đơn thuần không đủ. Quy tắc eligibility không tạo residency, không cấp quyền occupant/household và không thay thế authorization theo từng resource.

Sau khi kích hoạt, `residents.residents.email` là email liên hệ do MANAGER quản lý theo FE-02.3. Cư dân xem hồ sơ của mình theo FE-02.7. Với yêu cầu đổi email tài khoản, MANAGER đối chiếu danh tính rồi khởi tạo quy trình FE-01.6. Cư dân tự xác minh mã tại email mới và nhập mật khẩu hiện tại. Quy trình chỉ cập nhật `auth.users.email`, giữ nguyên email liên hệ, liên kết Resident–UserAccount và dữ liệu căn hộ. [Hợp đồng FE-01](FE-01-API-and-Deployment.md#đổi-email-cư-dân-theo-yêu-cầu-ban-quản-lý)


## Quy tắc ngày hiệu lực và thao tác đồng thời

- Danh sách lấy toàn bộ cư trú hiện hành của Resident trong trang và hợp nhất với ownership; vẫn phân trang phía server.
- Backend dùng timezone tòa nhà qua public contract của PropertyAssets. Cư trú hiện hành xét status cùng `StartDate`/`EndDate`; ngày đầu và cuối được tính trong khoảng. UI tách hiện hành, sắp tới và lịch sử thay vì chỉ xét `EndDate == null`.
- Không kết thúc/chuyển head nếu còn member ACTIVE chưa hết hạn, bao gồm member có hạn và sắp bắt đầu. Member phải có khoảng thời gian nằm trong khoảng hiệu lực của head; head có hạn không nhận member vô thời hạn hoặc kéo dài quá head.
- Kiểm tra trùng/head theo khoảng chồng lấn. Add/Move/End khóa giao dịch theo căn hộ; Move khóa các căn theo thứ tự cố định để tránh race làm member mất head.
- End lại residency trả conflict và giữ nguyên lịch sử; ngày kết thúc trước ngày bắt đầu trả validation. Move thất bại phải rollback cả source và target.

Giới hạn đã ghi nhận trong đợt sửa ngày 03/11/2026: UI chưa nhận business date từ response và còn phụ thuộc ngày máy người dùng. Nếu cần hỗ trợ máy ở múi giờ khác, phải chốt contract ngày/timezone; không coi kiểm thử backend là nghiệm thu toàn bộ UI.

## Tổng quan danh sách cư dân

`GET /api/v1/residents/summary` dùng policy `residents.read` như danh sách cư dân. Phản hồi chỉ có bốn số đếm trên toàn bộ hồ sơ trong deployment.

| Trường | Nguồn tính |
| --- | --- |
| `totalResidents` | Tất cả hồ sơ Resident |
| `activeResidents` | Hồ sơ có `Resident.Status == ACTIVE` |
| `linkedAccounts` | Hồ sơ có `Resident.UserId` |
| `newResidentsThisMonth` | Hồ sơ có `Resident.CreatedAt` trong khoảng đầu tháng đến trước đầu tháng tiếp theo |

API lấy múi giờ chung cư qua `ICurrentBuildingTimeZone` của PropertyAssets. Backend đổi hai mốc đầu tháng sang UTC để so sánh với `CreatedAt`. Kết quả độc lập với từ khóa, bộ lọc và trang hiện tại của danh sách.
