#nullable enable
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using PropFlow.Modules.AiChatbot.Infrastructure;

// Loaded only by the isolated browser rehearsal through DOTNET_STARTUP_HOOKS.
internal static class StartupHook
{
    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("PROPFLOW_NOTIFICATION_GROUP") != "AiAuthorization" ||
            Assembly.GetEntryAssembly()?.GetName().Name != "PropFlow.Api")
            return;

        DiagnosticListener.AllListeners.Subscribe(new Observer<DiagnosticListener>(listener =>
        {
            if (listener.Name != "Microsoft.Extensions.Hosting") return;
            listener.Subscribe(new Observer<KeyValuePair<string, object?>>(message =>
            {
                if (message is { Key: "HostBuilding", Value: IHostBuilder host })
                    host.ConfigureServices((_, services) => Configure(services));
            }));
        }));
    }

    private static void Configure(IServiceCollection services)
    {
        services.PostConfigure<ChatAiOptions>(options =>
        {
            options.Enabled = true;
            options.ApiKey = "synthetic-browser-key";
            options.BaseUrl = "https://gemini.example.invalid/v1beta/";
        });
        foreach (var client in new[] { "ServiceRequestGemini", "ResidentChatGemini" })
            services.PostConfigure<HttpClientFactoryOptions>(client, options =>
                options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = new GeminiBrowserResponse()));
    }

    private sealed class Observer<T>(Action<T> action) : IObserver<T>
    {
        public void OnNext(T value) => action(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    private sealed class GeminiBrowserResponse : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "gemini.example.invalid")
                throw new InvalidOperationException("Địa chỉ provider B7 không phải địa chỉ fixture.");

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (body.RootElement.TryGetProperty("requests", out _))
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { embeddings = new[] { new { values = Enumerable.Repeat(1f, 768) } } })
                };

            var fields = body.RootElement.GetProperty("generationConfig").GetProperty("responseSchema").GetProperty("properties");
            var text = fields.TryGetProperty("categoryCode", out _)
                ? """{"categoryCode":"REPAIR","reason":"Kết quả phân loại tổng hợp."}"""
                : fields.TryGetProperty("priorityCode", out _)
                    ? """{"priorityCode":"HIGH","maintenanceRecommended":true,"action":"Kiểm tra bơm","resources":"Nhân viên kỹ thuật","reason":"Áp suất cấp nước yếu."}"""
                    : """{"supported":true,"answer":"Kiểm tra lịch mở cửa trong tài liệu [1].","citations":[1]}""";
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { candidates = new[] { new { finishReason = "STOP", content = new
                    { parts = new[] { new { text } } } } } })
            };
        }
    }
}
