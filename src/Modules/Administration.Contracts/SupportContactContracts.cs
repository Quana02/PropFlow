using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PropFlow.Modules.Administration.Contracts;

public sealed record PublicSupportContacts(string? Email, string? Phone);
public sealed record SupportContactsState(string? Email, string? Phone, string Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateSupportContacts : IValidatableObject
{
    public string? Email { get; set; }
    public string? Phone { get; set; }
    [Required(ErrorMessage = "Vui lòng tải cấu hình trước khi lưu.")]
    [RegularExpression("^[A-Fa-f0-9]{64}$", ErrorMessage = "Phiên bản cấu hình không hợp lệ.")]
    public string ExpectedVersion { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => SupportContactRules.Validate(Email, Phone);
}

public static class SupportContactRules
{
    public const string EmailKey = "support.contact_email";
    public const string PhoneKey = "support.contact_phone";
    public static bool IsSupportedKey(string key) => key is EmailKey or PhoneKey;
    public static string? NormalizeEmail(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static string? NormalizePhone(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : Regex.Replace(value.Trim(), "[ .()\\-]", "");
    public static IEnumerable<ValidationResult> Validate(string? email, string? phone)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (email?.Any(char.IsControl) == true || normalizedEmail is not null && (email!.Length > 254
            || normalizedEmail.Any(char.IsWhiteSpace) || !new EmailAddressAttribute().IsValid(normalizedEmail)))
            yield return new("Nhập email hỗ trợ hợp lệ, tối đa 254 ký tự.", [nameof(UpdateSupportContacts.Email)]);
        var normalizedPhone = NormalizePhone(phone);
        if (phone?.Any(char.IsControl) == true || normalizedPhone is not null && (phone!.Length > 32 || !Regex.IsMatch(normalizedPhone, "^\\+?[0-9]{8,15}$")))
            yield return new("Nhập số hỗ trợ từ 8 đến 15 chữ số, có thể bắt đầu bằng +.", [nameof(UpdateSupportContacts.Phone)]);
    }
}
