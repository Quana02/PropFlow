using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.UnitTests;

[Trait("UI", "SharedFeedback")]
public sealed class PropFlowNoticeTests
{
    [Theory]
    [InlineData(NoticeTone.Info, "info", "status")]
    [InlineData(NoticeTone.Warning, "warning", "status")]
    [InlineData(NoticeTone.Success, "success", "status")]
    [InlineData(NoticeTone.Danger, "danger", "alert")]
    public async Task EachToneHasAnAccessibleTitleAndAppropriateAnnouncementRole(NoticeTone tone, string value, string role)
    {
        var html = await Render(new Dictionary<string, object?>
        {
            ["Title"] = "Thông báo trạng thái", ["Message"] = "Nội dung cần đọc", ["Tone"] = tone
        });

        Assert.Contains($"data-tone=\"{value}\"", html);
        Assert.Contains($"role=\"{role}\"", html);
        var labelledBy = Regex.Match(html, "aria-labelledby=\"([^\"]+)\"");
        Assert.True(labelledBy.Success);
        Assert.Contains($"id=\"{labelledBy.Groups[1].Value}\"", html);
        Assert.Contains("aria-hidden=\"true\"", html);
    }

    [Fact]
    public async Task PlainTextFromApiResponsesIsEscapedAndOptionalContentIsOmitted()
    {
        var html = await Render(new Dictionary<string, object?>
        {
            ["Title"] = "<script>alert('title')</script>",
            ["Message"] = "<img src=x onerror=alert('message')>"
        });
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;img", html);

        var titleOnly = await Render(new Dictionary<string, object?> { ["Title"] = "Thông báo", ["Message"] = "  " });
        Assert.DoesNotContain("pf-notice__message", titleOnly);
        Assert.DoesNotContain("pf-notice__detail", titleOnly);
        Assert.DoesNotContain("pf-notice__actions", titleOnly);
    }

    [Fact]
    public async Task DetailAndActionSlotsKeepInteractiveContentInsideTheNotice()
    {
        RenderFragment detail = builder => builder.AddContent(0, "Chi tiết báo giá");
        RenderFragment actions = builder =>
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "type", "button");
            builder.AddAttribute(2, "class", "pf-notice-action--primary");
            builder.AddContent(3, "Xem chi tiết");
            builder.CloseElement();
        };
        var html = await Render(new Dictionary<string, object?>
        {
            ["Title"] = "Báo giá", ["ChildContent"] = detail, ["Actions"] = actions
        });
        Assert.Contains("pf-notice__detail", html);
        Assert.Contains("pf-notice__actions", html);
        Assert.Contains("type=\"button\"", html);
        Assert.Contains("pf-notice-action--primary", html);
        Assert.Contains("Xem chi tiết", System.Net.WebUtility.HtmlDecode(html));
    }

    private static async Task<string> Render(Dictionary<string, object?> parameters)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<PropFlowNotice>(ParameterView.FromDictionary(parameters));
            return result.ToHtmlString();
        });
    }
}
