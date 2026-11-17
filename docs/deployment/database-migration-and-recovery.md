# Migration và phục hồi PostgreSQL

## Kiểm kê và migrate

Mục tiêu là áp dụng đúng migration vào database đã chọn. Người vận hành chạy sau khi DBA xác nhận đích, quyền DDL và bản sao. Thao tác tại terminal có quyền truy cập PostgreSQL, với `PROPFLOW_MIGRATION_DATABASE` cấp từ secret store.

```powershell
dotnet build scripts/authz/fixture/BrowserFixture.csproj --artifacts-path artifacts/authz/fixture-build
$runner = 'artifacts/authz/fixture-build/bin/BrowserFixture/debug/BrowserFixture.dll'
dotnet $runner status
dotnet $runner migrate
dotnet $runner status
```

Runner dùng lịch sử `__EFMigrationsHistory` riêng từng schema. Thứ tự bắt đầu bằng prerequisite Authentication, PropertyAssets, Apartments, prerequisite Residents và Administration, sau đó hoàn tất Authentication/Residents rồi các module nghiệp vụ. Reporting dùng read models và các nguồn dữ liệu đã đăng ký. Trên DB đã hoạt động, checkpoint prerequisite được đối chiếu theo lịch sử và các module chỉ áp dụng migration còn thiếu. Kiểm chứng từng context trả `pending=0`. Nếu lỗi quyền DDL hoặc thiếu extension, DBA xác minh SQLSTATE và cấp quyền cho migration identity trước khi chạy lại. Runtime dùng tài khoản khác theo quyền cần thiết.

## Sao lưu và khôi phục thử

Mục tiêu là có bản sao đã thử restore. DBA chạy công cụ tương thích phiên bản PostgreSQL trên máy quản trị hoặc trong môi trường có PostgreSQL client. Sử dụng `PGSERVICE` hoặc `PGPASSFILE` được bảo vệ, cùng TLS phù hợp môi trường.

```powershell
pg_dump --format=custom --no-owner --no-acl --file=propflow-backup.dump --dbname='service=propflow-source'
Get-FileHash propflow-backup.dump -Algorithm SHA256
pg_restore --list propflow-backup.dump
pg_restore --exit-on-error --single-transaction --no-owner --no-acl --dbname='service=propflow-restore-test' propflow-backup.dump
```

Đích restore là database rỗng dành riêng cho diễn tập, có các extension cần thiết. Đối chiếu migration history, số dòng tài khoản và phân công, kết quả bảo trì, hóa đơn và dữ liệu then chốt. Lưu riêng bản sao RSA, OTP key và Data Protection key ring trong kho được phân quyền. Sau restore test, chạy lại smoke HTTPS và kiểm tài khoản thu hồi từ lúc backup. Nếu bản sao cũ có thể làm sống lại phiên, thu hồi phiên và thay khóa ký trước khi cấp traffic. Nếu chỉ migration lỗi trên database hiện hành, ưu tiên giữ backup và sửa tiến trước khi cân nhắc khôi phục.

Rehearsal tự động với fixture local dùng `scripts/authz/database-rehearsal.ps1 -PgBin <thư-mục-bin>`. Script kiểm snapshot rồi xóa đúng database thử nghiệm và archive do nó tạo. [pg_dump](https://www.postgresql.org/docs/current/app-pgdump.html) · [pg_restore](https://www.postgresql.org/docs/current/app-pgrestore.html)
