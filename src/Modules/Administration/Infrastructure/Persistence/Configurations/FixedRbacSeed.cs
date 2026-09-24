using PropFlow.Modules.Administration.Domain.Permissions;

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Configurations;

internal static class FixedRbacSeed
{
    internal static readonly DateTimeOffset CreatedAt = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);

    internal static readonly (Guid Id, string Code, string Name, string Module, string Description)[] Permissions =
    [
        (Guid.Parse("66666666-6666-4666-8666-666666666661"), SystemPermissionCodes.UseResidentServices, "Sử dụng dịch vụ cư dân", "Residents", "Truy cập các chức năng tự phục vụ dành cho cư dân."),
        (Guid.Parse("66666666-6666-4666-8666-666666666662"), SystemPermissionCodes.PerformAssignedOperations, "Thực hiện công việc được giao", "Operations", "Xử lý các công việc vận hành được phân công."),
        (Guid.Parse("66666666-6666-4666-8666-666666666663"), SystemPermissionCodes.ManageFinance, "Quản lý tài chính", "Finance", "Thực hiện nghiệp vụ hóa đơn và thanh toán."),
        (Guid.Parse("66666666-6666-4666-8666-666666666664"), SystemPermissionCodes.ManageOperations, "Quản lý vận hành", "Operations", "Quản lý dữ liệu và quy trình vận hành chung cư."),
        (Guid.Parse("66666666-6666-4666-8666-666666666665"), SystemPermissionCodes.ManageInternalAccounts, "Quản lý tài khoản nội bộ", "Administration", "Tạo và quản lý vai trò, trạng thái tài khoản Ban quản lý."),
        (Guid.Parse("66666666-6666-4666-8666-666666666666"), SystemPermissionCodes.ViewAdministrationActivity, "Xem nhật ký quản trị", "Administration", "Xem lịch sử thay đổi tài khoản nội bộ."),
        (Guid.Parse("66666666-6666-4666-8666-666666666667"), SystemPermissionCodes.ViewSystemOverview, "Xem tổng quan hệ thống", "Reporting", "Xem chỉ số tổng hợp toàn hệ thống ở chế độ chỉ đọc.")
    ];
}
