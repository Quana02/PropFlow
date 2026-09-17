using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PropFlow.Modules.Authentication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace PropFlow.Web.Client.Services.Authentication;

public sealed class AuthHttpHandler(AuthSession session, NavigationManager navigation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = session.AccessToken;
        request.Headers.Authorization = token == null ? null : new AuthenticationHeaderValue("Bearer", token);
        // Buffered JSON feature calls are replayable; large or streaming bodies are deliberately not replayed.
        HttpRequestMessage? replay = null;
        if (request.Content == null || request.Content.Headers.ContentLength is >= 0 and <= 1_048_576
            || request.Content is System.Net.Http.Json.JsonContent)
        {
            replay = new(request.Method, request.RequestUri);
            foreach (var header in request.Headers) replay.Headers.TryAddWithoutValidation(header.Key, header.Value);
            replay.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
            if (request.Content != null)
            {
                replay.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(ct));
                foreach (var header in request.Content.Headers) replay.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        using (replay)
        {
            var response = await base.SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
            if (replay == null) return response;
            if (await session.RefreshAsync(token))
            {
                replay.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
                if (replay.Headers.Contains("X-CSRF-TOKEN"))
                {
                    using var csrfRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(request.RequestUri!, "/api/v1/auth/csrf"));
                    csrfRequest.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
                    csrfRequest.Headers.Authorization = replay.Headers.Authorization;
                    using var csrfResponse = await base.SendAsync(csrfRequest, ct);
                    if (!csrfResponse.IsSuccessStatusCode) return response;
                    var protection = await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>(cancellationToken: ct);
                    replay.Headers.Remove("X-CSRF-TOKEN");
                    replay.Headers.Add("X-CSRF-TOKEN", protection!.Token);
                }
                response.Dispose();
                response = await base.SendAsync(replay, ct);
                if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
                session.Clear("Phiên đăng nhập không còn hợp lệ. Vui lòng đăng nhập lại.");
            }
            navigation.NavigateTo("/login");
            return response;
        }
    }
}
