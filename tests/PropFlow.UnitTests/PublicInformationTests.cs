using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropFlow.Web.Client.Features.PublicInformation;

namespace PropFlow.UnitTests;

public sealed class PublicInformationTests
{
    [Theory]
    [InlineData(typeof(Terms), "/terms")]
    [InlineData(typeof(Privacy), "/privacy")]
    [InlineData(typeof(Help), "/help")]
    [InlineData(typeof(Contact), "/contact")]
    public void InformationRoutesArePublic(Type page, string route)
    {
        Assert.Equal(route, Assert.Single(page.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.NotNull(page.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Empty(page.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(typeof(PublicInformationLayout), page.GetCustomAttribute<LayoutAttribute>()?.LayoutType);
    }

    [Theory]
    [InlineData("đăng ký", true)]
    [InlineData("DANG KY", true)]
    [InlineData("tai khoan đăng ký", true)]
    [InlineData("chu de khac", false)]
    public void SearchMatchesVietnameseQuestionsKeywordsAndCategory(string query, bool expected)
    {
        var group = HelpContent.Categories.Single(item => item.Id == "account");
        Assert.Equal(expected, HelpSearch.Matches(group.Entries[0], group.Title, query));
    }

    [Theory]
    [InlineData(typeof(Terms))]
    [InlineData(typeof(Privacy))]
    public async Task PoliciesDisplayDraftDisclosureAndMatchingAnchors(Type page)
    {
        var html = await Render(page);
        Assert.Contains("Bản dự thảo · Chưa ban hành", html);
        Assert.Contains("aria-label=\"Mục lục\"", html);
        Assert.Contains("href=\"/contact\"", html);
        Assert.DoesNotContain("<form", html);
    }

    [Fact]
    public async Task ContactContainsRealRoutesAndNoSubmissionForm()
    {
        var html = await Render(typeof(Contact));
        Assert.Contains("chưa công bố email, số điện thoại hoặc giờ hỗ trợ", html);
        Assert.Contains("href=\"/resident/contact-management\"", html);
        Assert.DoesNotContain("<form", html);
        Assert.DoesNotContain("mailto:", html);
        Assert.DoesNotContain("tel:", html);
    }

    [Fact]
    public async Task FormLinksOpenSafelyInSeparateTab()
    {
        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(InformationLinks.NewTab)] = true });
        var html = await Render(typeof(InformationLinks), parameters);
        foreach (var route in new[] { "/terms", "/privacy", "/help", "/contact" })
            Assert.Contains($"href=\"{route}\" target=\"_blank\" rel=\"noopener\"", html);
    }

    private static async Task<string> Render(Type component, ParameterView? parameters = null)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            WebUtility.HtmlDecode((await renderer.RenderComponentAsync(component, parameters ?? ParameterView.Empty)).ToHtmlString()));
    }
}
