using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.Web.Client.Services.Api;

public record ApiResult(bool IsSuccess, int? StatusCode = null, string? Code = null, string? Message = null,
    string? TraceId = null, IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
public sealed record ApiResult<T>(bool IsSuccess, T? Data = default, int? StatusCode = null, string? Code = null,
    string? Message = null, string? TraceId = null, IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
    : ApiResult(IsSuccess, StatusCode, Code, Message, TraceId, ValidationErrors);
public sealed record EmptyResponse;
public sealed record ApiFileContent(byte[] Bytes, string ContentType);

public class ApiClient(HttpClient http, ILogger<ApiClient> logger)
{
    private sealed record Problem(string? Title, string? Detail, string? Code, string? TraceId, Dictionary<string, string[]>? Errors);
    public async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body = null, bool csrf = false, IReadOnlyDictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        try
        {
            string? csrfToken = null;
            if (csrf)
            {
                var protection = await SendAsync<CsrfResponse>(HttpMethod.Get, "api/v1/auth/csrf", ct: ct);
                if (!protection.IsSuccess) return new(false, StatusCode: protection.StatusCode, Code: protection.Code, Message: protection.Message);
                csrfToken = protection.Data!.Token;
            }
            using var request = new HttpRequestMessage(method, path);
            if (headers is not null) foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
            if (body != null) request.Content = JsonContent.Create(body, body.GetType());
            if (csrfToken != null) request.Headers.Add("X-CSRF-TOKEN", csrfToken);
            using var response = await http.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                Problem? problem = null;
                if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
                {
                    try { problem = await response.Content.ReadFromJsonAsync<Problem>(cancellationToken: ct); }
                    catch (JsonException) { logger.LogWarning("Invalid API error body for status {Status}.", status); }
                }
                return new(false, StatusCode: status, Code: problem?.Code, Message: problem?.Detail ?? problem?.Title ?? MessageFor(status),
                    TraceId: problem?.TraceId, ValidationErrors: problem?.Errors);
            }
            if (typeof(T) == typeof(EmptyResponse)) return new(true, (T)(object)new EmptyResponse(), status);
            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return data == null ? new(false, StatusCode: status, Code: "invalid_response", Message: "Dữ liệu phản hồi không hợp lệ.") : new(true, data, status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(false, Code: "timeout", Message: "Yêu cầu quá thời gian chờ. Vui lòng thử lại."); }
        catch (HttpRequestException) { return new(false, Code: "network", Message: "Không kết nối được máy chủ. Vui lòng kiểm tra kết nối."); }
        catch (JsonException) { logger.LogWarning("API response did not match the typed contract."); return new(false, Code: "invalid_response", Message: "Dữ liệu phản hồi không đúng định dạng."); }
    }

    public async Task<ApiResult<T>> SendMultipartAsync<T>(string path, MultipartFormDataContent body, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body };
    public async Task<ApiResult<T>> SendMultipartAsync<T>(HttpMethod method, string path, MultipartFormDataContent content, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path) { Content = content };
            request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
            using var response = await http.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                Problem? problem = null;
                if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
                {
                    try { problem = await response.Content.ReadFromJsonAsync<Problem>(cancellationToken: ct); }
                    catch (JsonException) { logger.LogWarning("Invalid multipart API error body for status {Status}.", status); }
                }
                return new(false, StatusCode: status, Code: problem?.Code, Message: problem?.Detail ?? problem?.Title ?? MessageFor(status),
                    TraceId: problem?.TraceId, ValidationErrors: problem?.Errors);
            }
            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return data is null
                ? new(false, StatusCode: status, Code: "invalid_response", Message: "Dữ liệu phản hồi không hợp lệ.")
                : new(true, data, status);
                var problem = await ReadProblemAsync(response, ct);
                return new(false, StatusCode: status, Code: problem?.Code, Message: problem?.Detail ?? problem?.Title ?? MessageFor(status), TraceId: problem?.TraceId, ValidationErrors: problem?.Errors);
            }
            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return data is null ? new(false, StatusCode: status, Code: "invalid_response", Message: "Dữ liệu phản hồi không hợp lệ.") : new(true, data, status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(false, Code: "timeout", Message: "Yêu cầu quá thời gian chờ. Vui lòng thử lại."); }
        catch (HttpRequestException) { return new(false, Code: "network", Message: "Không kết nối được máy chủ. Vui lòng kiểm tra kết nối."); }
        catch (JsonException) { logger.LogWarning("Multipart API response did not match the typed contract."); return new(false, Code: "invalid_response", Message: "Dữ liệu phản hồi không đúng định dạng."); }
    }

    public async Task<ApiResult<ApiFileContent>> GetFileAsync(string path, CancellationToken ct = default)
        catch (JsonException) { return new(false, Code: "invalid_response", Message: "Dữ liệu phản hồi không đúng định dạng."); }
    }
    public async Task<ApiResult<byte[]>> GetBytesAsync(string path, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
            using var response = await http.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                Problem? problem = null;
                if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
                {
                    try { problem = await response.Content.ReadFromJsonAsync<Problem>(cancellationToken: ct); }
                    catch (JsonException) { logger.LogWarning("Invalid file API error body for status {Status}.", status); }
                }
                return new(false, StatusCode: status, Code: problem?.Code, Message: problem?.Detail ?? problem?.Title ?? MessageFor(status), TraceId: problem?.TraceId);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return new(true, new ApiFileContent(bytes, contentType), status);
                var problem = await ReadProblemAsync(response, ct);
                return new(false, StatusCode: status, Code: problem?.Code, Message: problem?.Detail ?? problem?.Title ?? MessageFor(status), TraceId: problem?.TraceId, ValidationErrors: problem?.Errors);
            }
            return new(true, await response.Content.ReadAsByteArrayAsync(ct), status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(false, Code: "timeout", Message: "Yêu cầu quá thời gian chờ. Vui lòng thử lại."); }
        catch (HttpRequestException) { return new(false, Code: "network", Message: "Không kết nối được máy chủ. Vui lòng kiểm tra kết nối."); }
    }
    private async Task<Problem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true) return null;
        try { return await response.Content.ReadFromJsonAsync<Problem>(cancellationToken: ct); }
        catch (JsonException) { logger.LogWarning("Invalid API error body for status {Status}.", (int)response.StatusCode); return null; }
    }
    private static string MessageFor(int status) => status switch
    {
        400 => "Vui lòng kiểm tra thông tin đã nhập.", 401 => "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.",
        403 => "Bạn không có quyền thực hiện thao tác này.", 404 => "Không tìm thấy thông tin yêu cầu.",
        409 => "Thông tin đã thay đổi hoặc đang được sử dụng.", 429 => "Bạn gửi yêu cầu quá nhanh. Vui lòng thử lại sau.",
        _ => "Máy chủ chưa thể xử lý yêu cầu. Vui lòng thử lại sau."
    };
}
public sealed class AuthenticatedApiClient(HttpClient http, ILogger<ApiClient> logger) : ApiClient(http, logger);
