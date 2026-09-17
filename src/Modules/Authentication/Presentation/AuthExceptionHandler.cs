using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Authentication.Application;

namespace PropFlow.Modules.Authentication.Presentation;

public sealed class AuthExceptionHandler(ILogger<AuthExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && ct.IsCancellationRequested) return false;
        var failure = exception as AuthFailure;
        var csrf = exception is AntiforgeryValidationException;
        var status = failure?.Status ?? (csrf ? 400 : 500);
        if (status == 500) logger.LogError("Unhandled API failure {ExceptionType}, trace {TraceId}.", exception.GetType().Name, context.TraceIdentifier);
        await Results.Problem(statusCode: status, title: failure?.Message ?? (csrf
            ? "Phiên bảo vệ yêu cầu đã thay đổi. Vui lòng thử lại."
            : "Không thể hoàn tất yêu cầu lúc này. Vui lòng thử lại sau."), extensions: new Dictionary<string, object?>
            { ["code"] = failure?.Code ?? (csrf ? "csrf_invalid" : "server_error"), ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        return true;
    }
}
