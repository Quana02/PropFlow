using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.UnitTests;

#pragma warning disable BL0006
public sealed class ConfirmationDialogTests
{
    [Theory]
    [InlineData(DialogTone.Confirm, "Gửi hóa đơn?", "Cư dân sẽ nhận được hóa đơn này.", "Gửi hóa đơn")]
    [InlineData(DialogTone.Danger, "Thu hồi tin nhắn?", "Cả hai bên sẽ không còn thấy nội dung của tin nhắn đã chọn.", "Thu hồi")]
    [InlineData(DialogTone.Info, "Chưa thể ngừng quản lý", "Căn hộ vẫn còn quan hệ cư trú đang hiệu lực.", "Đã hiểu")]
    [InlineData(DialogTone.Decision, "Rà soát giao dịch", "Chọn cách xử lý khoản tiền nhận từ ngân hàng.", "Gắn hóa đơn")]
    public async Task Renders_accessible_semantic_variants(DialogTone tone, string title, string message, string action)
    {
        await using var services = Services();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<PropFlowConfirmationDialog>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Tone"] = tone,
                ["Title"] = title,
                ["Message"] = message,
                ["ConfirmText"] = action,
                ["Detail"] = tone == DialogTone.Danger ? "Thao tác áp dụng cho mọi tin đã chọn." : null
            }));
            return root.ToHtmlString();
        });

        var variant = tone.ToString().ToLowerInvariant();
        Assert.Contains($"data-tone=\"{variant}\"", html);
        Assert.Contains("role=\"dialog\"", html);
        Assert.Contains("aria-modal=\"true\"", html);
        var titleId = System.Text.RegularExpressions.Regex.Match(html, "aria-labelledby=\"([^\"]+)\"").Groups[1].Value;
        var descriptionIds = System.Text.RegularExpressions.Regex.Match(html, "aria-describedby=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(titleId);
        Assert.NotEmpty(descriptionIds);
        Assert.Contains($"id=\"{titleId}\"", html);
        foreach (var id in descriptionIds.Split(' ')) Assert.Contains($"id=\"{id}\"", html);
        Assert.Equal(tone != DialogTone.Info, html.Contains("pf-dialog__secondary"));
        Assert.Contains(action, System.Net.WebUtility.HtmlDecode(html));

        if (Environment.GetEnvironmentVariable("PROPFLOW_DIALOG_PREVIEW_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, $"{variant}.html"), html);
        }
    }

    [Theory]
    [InlineData(DialogTone.Confirm, false, false, 1, 1)]
    [InlineData(DialogTone.Danger, false, false, 1, 1)]
    [InlineData(DialogTone.Info, false, false, 0, 2)]
    [InlineData(DialogTone.Danger, true, false, 0, 0)]
    [InlineData(DialogTone.Danger, false, true, 0, 1)]
    public async Task Dispatches_only_the_available_actions_and_releases_the_modal(
        DialogTone tone, bool busy, bool invalid, int confirmations, int cancellations)
    {
        await using var services = Services();
        var renderer = new DialogRenderer(services);
        var confirmed = 0;
        var cancelled = 0;
        var id = await renderer.Mount(new()
        {
            ["Title"] = "Kiểm tra thao tác", ["Tone"] = tone, ["Busy"] = busy, ["ConfirmDisabled"] = invalid,
            ["OnConfirm"] = EventCallback.Factory.Create(this, () => { confirmed++; }),
            ["OnCancel"] = EventCallback.Factory.Create(this, () => { cancelled++; })
        });
        try
        {
            await renderer.Click(id, "pf-dialog__primary");
            await renderer.Click(id, tone == DialogTone.Info ? "pf-dialog__close" : "pf-dialog__secondary");
            Assert.Equal(confirmations, confirmed);
            Assert.Equal(cancellations, cancelled);
            Assert.Contains("openDialog", services.GetRequiredService<DialogJs>().Calls);
        }
        finally
        {
            await renderer.DisposeAsync();
        }
        Assert.Contains("close", services.GetRequiredService<DialogJs>().Calls);
    }

    private static ServiceProvider Services() => new ServiceCollection().AddLogging()
        .AddSingleton<DialogJs>().AddSingleton<IJSRuntime>(services => services.GetRequiredService<DialogJs>())
        .BuildServiceProvider();

    private sealed class DialogRenderer(IServiceProvider services) : Renderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Dialog rendering failed.", exception);
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

        public Task<int> Mount(Dictionary<string, object?> parameters) => Dispatcher.InvokeAsync(async () =>
        {
            var id = AssignRootComponentId(InstantiateComponent(typeof(PropFlowConfirmationDialog)));
            await RenderRootComponentAsync(id, ParameterView.FromDictionary(parameters));
            return id;
        });

        public Task Click(int id, string buttonClass) => Dispatcher.InvokeAsync(() =>
        {
            var frames = GetCurrentRenderTreeFrames(id);
            for (var i = 0; i < frames.Count; i++)
            {
                var button = frames.Array[i];
                if (button.FrameType != RenderTreeFrameType.Element || button.ElementName != "button") continue;
                var attributes = frames.Array.Skip(i + 1).TakeWhile(frame => frame.FrameType == RenderTreeFrameType.Attribute).ToArray();
                if (!attributes.Any(frame => frame.AttributeName == "class" && Equals(frame.AttributeValue, buttonClass))) continue;
                var handler = attributes.Single(frame => frame.AttributeName == "onclick").AttributeEventHandlerId;
                return DispatchEventAsync(handler, null, new MouseEventArgs());
            }
            throw new InvalidOperationException($"No action found for {buttonClass}.");
        });
    }

    private sealed class DialogJs : IJSRuntime, IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            return ValueTask.FromResult(identifier is "import" or "openDialog" ? (T)(object)this : default!);
        }

        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<T>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
#pragma warning restore BL0006
