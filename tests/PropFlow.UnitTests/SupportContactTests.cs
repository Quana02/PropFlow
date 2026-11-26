using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PropFlow.Api.Composition.Auditing;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Domain.SystemConfigurations;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Web.Client.Features.Administration.Services;
using PropFlow.Web.Client.Features.PublicInformation;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

[Trait("UseCase", "FE-15.8")]
public sealed class SupportContactTests
{
    [Theory]
    [InlineData("support.contact_email", "support@example.invalid", true)]
    [InlineData("support.contact_phone", "19001234", true)]
    [InlineData("other.secret", "private-value", false)]
    public void Audit_exposes_only_allowlisted_public_contact_values(string key, string value, bool visible)
    {
        using var db = new AdministrationDbContext(new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entry = db.Add(new SystemConfiguration(key, value, DateTimeOffset.UtcNow));
        var audit = new AuditCapture(new HttpContextAccessor(), TimeProvider.System).FromEntity(entry)!;
        Assert.Equal(visible ? value : "[Đã ẩn nội dung]", audit.Changes.Single(x => x.Field == "ConfigValue").After);
    }
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("   ", " ", true)]
    [InlineData("support@example.invalid", "+84 (24) 1234-5678", true)]
    [InlineData(null, "1900 1234", true)]
    [InlineData("bad-email", null, false)]
    [InlineData("a@b.invalid\r\nBcc:x@y.invalid", null, false)]
    [InlineData("support@example.invalid\n", null, false)]
    [InlineData(null, "19001234\t", false)]
    [InlineData(null, "１９００１２３４", false)]
    [InlineData(null, "1900\u200b1234", false)]
    [InlineData(null, "javascript:12345678", false)]
    [InlineData(null, "123", false)]
    [InlineData(null, "+1234567890123456", false)]
    public void Validates_only_public_contact_fields(string? email, string? phone, bool valid)
        => Assert.Equal(valid, !SupportContactRules.Validate(email, phone).Any());

    [Fact]
    public void Rejects_overlong_raw_values_even_when_normalized_values_would_fit()
    {
        Assert.NotEmpty(SupportContactRules.Validate(new string(' ', 254) + "a@b.invalid", null));
        Assert.NotEmpty(SupportContactRules.Validate(null, new string(' ', 32) + "19001234"));
    }

    [Fact]
    public void Update_requires_opaque_version_and_normalizes_without_account_defaults()
    {
        var input = new UpdateSupportContacts { Email = " support@example.invalid ", Phone = "+84 (24) 1234-5678" };
        var failures = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), failures, true));
        input.ExpectedVersion = new string('A',64);
        Assert.True(Validator.TryValidateObject(input, new ValidationContext(input), [], true));
        Assert.Equal("support@example.invalid", SupportContactRules.NormalizeEmail(input.Email));
        Assert.Equal("+842412345678", SupportContactRules.NormalizePhone(input.Phone));
        Assert.Null(SupportContactRules.NormalizeEmail("  "));
    }

    [Theory]
    [InlineData(typeof(Contact))]
    [InlineData(typeof(Help))]
    public async Task Public_pages_use_real_contact_result_and_distinguish_unset_from_failure(Type page)
    {
        var api = new FakeApi { Result = new(true, new("support@example.invalid", "19001234")) };
        var html = await Render(page, api);
        Assert.Contains("mailto:support%40example.invalid", html);
        Assert.Contains("tel:19001234", html);
        api.Result = new(true, new(null,null));
        html = await Render(page, api);
        Assert.Contains("Chưa công bố email hoặc số điện thoại hỗ trợ",html);
        Assert.DoesNotContain("mailto:", html);
        api.Result = new(false, StatusCode:503);
        html = await Render(page, api);
        Assert.Contains("Chưa thể tải thông tin liên hệ",html);
        Assert.DoesNotContain("Chưa công bố email hoặc số điện thoại hỗ trợ", html);
    }

    private static async Task<string> Render(Type page, ISupportContactApi api)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton(api).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => System.Net.WebUtility.HtmlDecode((await renderer.RenderComponentAsync(page, ParameterView.Empty)).ToHtmlString()));
    }

    internal sealed class FakeApi : ISupportContactApi
    {
        public ApiResult<PublicSupportContacts> Result { get; set; } = new(true, new(null,null));
        public Task<ApiResult<PublicSupportContacts>> PublicAsync(CancellationToken ct=default) => Task.FromResult(Result);
        public Task<ApiResult<SupportContactsState>> ReadAsync(CancellationToken ct=default) => throw new NotSupportedException();
        public Task<ApiResult<SupportContactsState>> UpdateAsync(UpdateSupportContacts input, CancellationToken ct=default) => throw new NotSupportedException();
    }
}
