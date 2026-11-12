using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropFlow.Web.Client.Features.Administration.Components;
using PropFlow.Web.Client.Features.Administration.Models;

namespace PropFlow.UnitTests;

[Trait("UseCase", "FE-15 Overview")]
public sealed class ServiceRequestTrendChartTests
{
    [Fact]
    public async Task Sparse_daily_counts_show_exact_dates_counts_and_zero_days()
    {
        var start = new DateOnly(2026, 10, 6);
        var items = Enumerable.Range(0, 30).Select(i => new AdministrationOverviewTrend(start.AddDays(i), i == 4 ? 2 : 0)).ToArray();
        var html = await Render(items);
        Assert.Contains("10/10/2026: 2 yêu cầu mới", html);
        Assert.Contains("06/10/2026: 0 yêu cầu mới", html);
        Assert.Contains("04/11/2026: 0 yêu cầu mới", html);
        Assert.Equal(30, html.Split("class=\"day-column ").Length - 1);
        Assert.Equal(1, html.Split("class=\"bar\"").Length - 1);
        Assert.Contains("Cao nhất 2 yêu cầu/ngày", System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", ""));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_or_all_zero_counts_show_an_honest_empty_state(bool hasDays)
    {
        var html = await Render(hasDays ? [new(new(2026, 11, 4), 0)] : []);
        Assert.Contains("Không có yêu cầu mới", html);
        Assert.DoesNotContain("class=\"bar\"", html);
    }

    private static async Task<string> Render(AdministrationOverviewTrend[] items)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode((await renderer.RenderComponentAsync<ServiceRequestTrendChart>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ServiceRequestTrendChart.Items)] = items }))).ToHtmlString()));
    }
}
