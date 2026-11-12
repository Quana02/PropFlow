using System.ComponentModel.DataAnnotations;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Features.Authentication.Models;

namespace PropFlow.UnitTests;

public sealed class ResidentSelfRegistrationTests
{
    [Theory]
    [InlineData("CCCD", "064204010555", true)]
    [InlineData("CCCD", "06420401055", false)]
    [InlineData("CMND", "123456789", true)]
    [InlineData("CMND", "123456789012", true)]
    [InlineData("CMND", "1234567890", false)]
    public void Public_contract_enforces_identity_formats(string type, string number, bool valid)
    {
        var request = new RegisterRequest("resident_user", "Cư dân", "resident@example.test",
            "A secure passphrase 123!", "0363602027", type, number);

        Assert.Equal(valid, Validate(request).Count == 0);
    }

    [Fact]
    public void Registration_form_requires_phone_and_identity_fields()
    {
        var form = new RegistrationForm
        {
            Username = "resident_user",
            DisplayName = "Cư dân",
            Email = "resident@example.test",
            Password = "A secure passphrase 123!",
            Confirmation = "A secure passphrase 123!"
        };

        var errors = Validate(form);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(form.PhoneNumber)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(form.IdentityType)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(form.IdentityNumber)));
    }

    [Fact]
    public void Registration_page_uses_identity_dropdown_and_submits_all_four_matching_fields()
    {
        var source = File.ReadAllText(Source("Features", "Authentication", "Pages", "Register.razor"));

        Assert.Contains("IdentityTypeOptions", source, StringComparison.Ordinal);
        Assert.Contains("new(\"CCCD\",\"CCCD\")", source, StringComparison.Ordinal);
        Assert.Contains("new(\"CMND\",\"CMND\")", source, StringComparison.Ordinal);
        Assert.Contains("form.PhoneNumber, form.IdentityType, form.IdentityNumber", source, StringComparison.Ordinal);
        Assert.Contains("<ValidationMessage For=\"()=>form.IdentityNumber\" />", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_page_uses_dismissible_auto_hiding_popup_for_api_feedback()
    {
        var source = File.ReadAllText(Source("Features", "Authentication", "Pages", "Register.razor"));

        Assert.DoesNotContain("<ApiFeedback", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<p class=\"feedback\"", source, StringComparison.Ordinal);
        Assert.Contains("Toasts.Notify(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<PropFlowToast", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_page_does_not_duplicate_field_errors_in_an_inline_validation_summary()
    {
        var source = File.ReadAllText(Source("Features", "Authentication", "Pages", "Register.razor"));

        Assert.DoesNotContain("<ValidationSummary", source, StringComparison.Ordinal);
        Assert.Contains("OnInvalidSubmit=\"HandleInvalidSubmit\"", source, StringComparison.Ordinal);
        Assert.Contains("ShowToast(\"Vui lòng kiểm tra các trường được đánh dấu bên dưới.\", \"error\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Features_share_the_root_toast_host_without_local_containers()
    {
        var root = Path.GetDirectoryName(Source("Routes.razor"))!;
        Assert.Contains("ToastHost", File.ReadAllText(Path.Combine(root, "Routes.razor")), StringComparison.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Features"), "*.razor", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("<PropFlowToast", source, StringComparison.Ordinal);
            Assert.DoesNotContain("<ToastHost", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Registration_identity_select_matches_text_inputs_in_light_dark_and_invalid_states()
    {
        var styles = File.ReadAllText(Source("Features", "Authentication", "Components", "AuthLayout.razor.css"));

        Assert.Contains("::deep .auth-register-grid .pf-select__trigger", styles, StringComparison.Ordinal);
        Assert.DoesNotContain(".auth-register-grid ::deep .pf-select__trigger", styles, StringComparison.Ordinal);
        Assert.Contains("height:32px", styles, StringComparison.Ordinal);
        Assert.Contains("min-height:32px", styles, StringComparison.Ordinal);
        Assert.Contains("html.dark .auth-content ::deep .auth-register-grid .pf-select__trigger", styles, StringComparison.Ordinal);
        Assert.Contains("html.dark .auth-content ::deep .auth-register-grid .pf-select.modified.invalid .pf-select__trigger", styles, StringComparison.Ordinal);
    }

    private static IReadOnlyList<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, true);
        return results;
    }

    private static string Source(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PropFlow.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, "src", "PropFlow.Web.Client", .. segments]);
    }
}
