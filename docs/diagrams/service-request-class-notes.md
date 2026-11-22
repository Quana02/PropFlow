# Service Request — Class Diagram dùng trong báo cáo

File nguồn: `service-request-class-compact.puml`. Đối chiếu code ngày 19/11/2026. Bản này mô tả các lớp miền chính và quan hệ logic, không phải sơ đồ use case hoặc thiết kế lại hệ thống.

## Các vai trò được biểu diễn thế nào?

- `Resident` là entity hồ sơ cư dân. `UserAccount` là tài khoản đăng nhập. Hai lớp không kế thừa nhau; hồ sơ có thể được tạo trước khi liên kết tài khoản.
- `Role` và `UserRoleAssignment` là lớp thực tế của Administration. `RESIDENT`, `MANAGER`, `STAFF` là giá trị vai trò, không phải ba lớp con của `UserAccount`.
- Hai association giữa `UserAccount` và `ServiceRequestAssignment` thể hiện hai quan hệ khác nhau: `AssignedBy` là tài khoản phân công; `StaffUserId` là tài khoản được giao việc. `{MANAGER / AssignedBy}` và `{STAFF / StaffUserId}` nêu vai trò lúc thao tác. Nếu tài khoản đổi vai trò sau đó, lịch sử phân công vẫn giữ nguyên.
- Manager phân công, duyệt phí và nghiệm thu; Resident gửi yêu cầu và chấp nhận/từ chối phí; Staff thực hiện và báo phí. Các quyền này do application services kiểm soát. Không thêm các phương thức nghiệp vụ giả vào `Role` hoặc `UserAccount`.

## Bội số đã kiểm chứng

| Quan hệ | Bội số ở hai đầu | Căn cứ |
| --- | --- | --- |
| UserAccount — UserRoleAssignment | 1 — 0..1 | `UserId` bắt buộc và unique: tối đa một bản ghi vai trò chính trên một tài khoản. |
| UserRoleAssignment — Role | 0..* — 1 | Nhiều bản ghi phân vai trò có thể cùng tham chiếu một Role. |
| Resident — UserAccount | 0..1 — 0..1 | `Resident.UserId` nullable và unique. |
| Resident — ServiceRequest | 1 — 0..* | `ServiceRequest.ResidentId` bắt buộc. |
| ServiceRequest — ServiceRequestCategory | 0..* — 0..1 | `CategoryId` nullable. |
| ServiceRequest — ServiceRequestAssignment | 1 — 0..* | Yêu cầu mới có thể chưa được phân công. Có nhiều bản ghi qua các lần phân công lại. |
| UserAccount — ServiceRequestAssignment (AssignedBy) | 1 — 0..* | Một tài khoản tạo mỗi lần phân công; một tài khoản có thể tạo nhiều lần phân công. |
| UserAccount — ServiceRequestAssignment (StaffUserId) | 1 — 0..* | Mỗi bản ghi giao việc cho một tài khoản Staff. |
| ServiceRequest — ServiceRequestActivity | 1 — 0..* | Mỗi activity thuộc một yêu cầu. |
| ServiceRequestAssignment — ServiceRequestActivity | 0..1 — 0..* | `AssignmentId` nullable; activity cấp yêu cầu không cần gắn một lần phân công. |

## Quy ước và phạm vi

- Dấu `+` là thuộc tính/phương thức public. Các thuộc tính được liệt kê có public getter và private setter trong C#. `?` là kiểu nullable. Tên và chữ ký phương thức được giữ đúng; không dùng dấu `...` để thay tham số.
- Các đường liền là association, kèm bội số ở hai đầu. Quan hệ khác module chỉ là tham chiếu logic bằng scalar ID, không khẳng định có EF navigation hoặc database FK giữa module.
- Không vẽ kế thừa giữa các vai trò và tài khoản; không thêm hình thoi composition khi chưa có căn cứ về quyền sở hữu vòng đời. Quan hệ phân công/activity trong mapping sử dụng `DeleteBehavior.Restrict`.
- Chỉ chọn các thuộc tính/phương thức chính để hình vừa tài liệu. Các enum được dùng làm kiểu nhưng không mở thêm hộp; xem giá trị thực tế trong `ServiceRequestStatus.cs` và `AssignmentStatus.cs`.
- Không đưa AI, hóa đơn, feedback, tệp bằng chứng và outbox vào hình tổng quan này. `PerformedBy` của activity cũng là scalar ID tùy chọn tham chiếu `UserAccount`; đường association phụ này được lược bỏ để tránh thêm đường cắt ngang hình.
- Trạng thái phí phát sinh nằm trên `ServiceRequest`, không có entity `AdditionalCostProposal` riêng trong triển khai hiện tại.
- Nên chèn SVG vào Word/Docs để giữ chữ sắc nét. Hình được bố trí ba cột, không cần co một sơ đồ toàn hệ thống xuống kích thước quá nhỏ.

## Nguồn đối chiếu trong repository

- `src/Modules/Authentication/Domain/Users/UserAccount.cs`
- `src/Modules/Administration/Domain/Roles/Role.cs`
- `src/Modules/Administration/Domain/UserRoleAssignments/UserRoleAssignment.cs`
- `src/Modules/Administration/Infrastructure/Persistence/Configurations/UserRoleAssignmentConfiguration.cs`
- `src/Modules/Residents/Domain/Residents/Resident.cs`
- `src/Modules/Residents/Infrastructure/Persistence/Configurations/ResidentConfiguration.cs`
- `src/Modules/ServiceRequests/Domain/ServiceRequests/ServiceRequest.cs`
- `src/Modules/ServiceRequests/Domain/ServiceRequestAssignments/ServiceRequestAssignment.cs`
- `src/Modules/ServiceRequests/Domain/ServiceRequestActivities/ServiceRequestActivity.cs`
- `src/Modules/ServiceRequests/Domain/ServiceRequestCategories/ServiceRequestCategory.cs`
- `src/Modules/ServiceRequests/Infrastructure/Persistence/Configurations/ServiceRequestConfiguration.cs`
- `src/Modules/ServiceRequests/Infrastructure/Persistence/Configurations/ServiceRequestActivityConfiguration.cs`
- `src/Modules/ServiceRequests/Infrastructure/AdditionalCostConsentService.cs`
