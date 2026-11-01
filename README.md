# Chạy PropFlow trên máy local

`global.json` cho phép dùng SDK .NET 8 trở lên. Ứng dụng vẫn dùng runtime .NET 8 và ASP.NET Core 8.

Máy này đã có PostgreSQL, API User Secrets và dữ liệu demo trong database. Xem [danh sách dữ liệu và tài khoản](docs/local-demo-data.md).

Chạy hai lệnh trong hai terminal riêng tại thư mục dự án:

```powershell
dotnet run --project .\src\PropFlow.Api\PropFlow.Api.csproj --launch-profile https
dotnet run --project .\src\PropFlow.Web\PropFlow.Web.csproj --launch-profile https
```

Web: https://localhost:7201 — API/Swagger: https://localhost:7101/swagger.

Trên máy khác, cấu hình API User Secrets theo [hướng dẫn cấu hình](docs/requirements/FE-01-API-and-Deployment.md).
