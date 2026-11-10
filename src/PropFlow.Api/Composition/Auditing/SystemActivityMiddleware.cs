using Microsoft.AspNetCore.Mvc.Controllers;
using PropFlow.Modules.Administration.Application.InternalAccounts;
using PropFlow.Modules.Administration.Contracts;

namespace PropFlow.Api.Composition.Auditing;

public sealed class SystemActivityMiddleware(RequestDelegate next, ILogger<SystemActivityMiddleware> logger)
{
    private static readonly HashSet<string> ReadOnlyActions = new(StringComparer.OrdinalIgnoreCase)
        { "Refresh", "Csrf", "Preview", "PreviewDestination", "PreviewSePayAccount", "CheckConnection", "CheckSePayConnection" };

    public async Task InvokeAsync(HttpContext context, AuditCapture capture, ISystemAuditWriter writer, TimeProvider clock)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var method = context.Request.Method;
        if (action is null || method is not ("POST" or "PUT" or "PATCH" or "DELETE") || ReadOnlyActions.Contains(action.ActionName))
        { await next(context); return; }
        var module = action.ControllerTypeInfo.Namespace?.Split('.').SkipWhile(x => x != "Modules").Skip(1).FirstOrDefault();
        if (module is null) { await next(context); return; }
        // Exception handling can clear routing values before the response starts.
        Guid? entityId = null;
        foreach (var key in new[] { "id", "residentId", "apartmentId", "taskId", "requestId", "userId" })
            if (Guid.TryParse(context.Request.RouteValues[key]?.ToString(), out var id)) { entityId = id; break; }
        var recorded = false;
        async Task RecordAsync(int status)
        {
            if (recorded) return;
            recorded = true;
            var success = status is >= 200 and < 300;
            var internalProfileUpdate = module == "Administration" && action.ActionName == "UpdateProfile";
            var explicitAccountAudit = internalProfileUpdate || module == "Administration" && action.ActionName is "Create" or "ChangeRole" or "ChangeStatus";
            // Data changes already have transactional audit; keep failures and security events separately.
            if (!success || module == "Authentication" || capture.RecordedChanges == 0 && !explicitAccountAudit)
            {
                var security = module == "Authentication";
                var label = internalProfileUpdate ? ManageInternalAccountProfile.AuditLabel
                    : security ? SecurityLabel(action.ActionName) : OperationLabel(action.ActionName);
                var eventAction = internalProfileUpdate ? ManageInternalAccountProfile.AuditAction : security ? "security_event" : "operation";
                var entry = new SystemAuditEvent(module, eventAction, label, internalProfileUpdate ? "InternalAccount" : action.ControllerName,
                    entityId, capture.Actor, success ? "SUCCESS" : status is 401 or 403 ? "DENIED" : "FAILED", clock.GetUtcNow(), context.TraceIdentifier, [], status);
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await writer.AppendAsync(entry, timeout.Token);
                }
                catch (Exception ex)
                {
                    // Never log request bodies, credentials, headers, query values or exception messages.
                    logger.LogCritical("System audit append failed ({ErrorType}); module {Module}; action {Action}; status {Status}; actor {Actor}; trace {Trace}.",
                        ex.GetType().Name, module, action.ActionName, status, capture.Actor, context.TraceIdentifier);
                }
            }
        }
        // Persist before headers/body are released; readers immediately see the completed operation.
        context.Response.OnStarting(() => RecordAsync(context.Response.StatusCode));
        try { await next(context); }
        catch { await RecordAsync(500); throw; }
        if (!recorded) await RecordAsync(context.Response.StatusCode);
    }

    private static string SecurityLabel(string action) => action switch
    {
        "Login" => "Đăng nhập", "Logout" => "Đăng xuất", "Register" => "Đăng ký tài khoản",
        "Activate" => "Xác minh đăng ký", "Resume" => "Gửi lại mã đăng ký", "ChangePassword" => "Đổi mật khẩu",
        "Forgot" => "Yêu cầu đặt lại mật khẩu", "Reset" => "Đặt lại mật khẩu", "VerifyRecovery" => "Xác minh khôi phục mật khẩu",
        "Update" => "Cập nhật hồ sơ tài khoản", _ => "Xác thực tài khoản"
    };

    private static string OperationLabel(string action) => action switch
    {
        "Create" or "Submit" => "Tạo dữ liệu", "Update" or "Save" => "Cập nhật dữ liệu", "Delete" => "Xóa dữ liệu",
        "Confirm" => "Xác nhận thanh toán", "Reject" => "Từ chối thanh toán", "Collect" => "Ghi nhận thanh toán",
        "Assign" or "AssignTask" => "Phân công xử lý", "Reassign" => "Phân công lại", "Dismiss" => "Bỏ qua giao dịch",
        "Close" => "Đóng hồ sơ", "Cancel" => "Hủy hồ sơ", "Respond" => "Phản hồi", "ReviewResult" => "Duyệt kết quả",
        "MoveResidency" => "Chuyển căn hộ cư trú", "EndResidency" => "Kết thúc cư trú", "EndOwnership" => "Kết thúc sở hữu",
        "ChangeStatus" or "SetStatus" or "ScheduleStatus" => "Thay đổi trạng thái", "Deactivate" => "Ngừng sử dụng",
        "Webhook" or "Receive" or "Handle" => "Tiếp nhận sự kiện thanh toán", "ReportTransfer" => "Báo chuyển khoản",
        "Send" or "Publish" => "Gửi thông báo", "MarkRead" or "Read" => "Đánh dấu đã đọc", _ => "Thực hiện thao tác"
    };
}
