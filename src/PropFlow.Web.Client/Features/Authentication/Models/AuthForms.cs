using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace PropFlow.Web.Client.Features.Authentication.Models;

public class LoginForm
{
    [Required(ErrorMessage = "Nhập tên đăng nhập."), StringLength(50, ErrorMessage = "Tên đăng nhập tối đa 50 ký tự.")]
    public string Username { get; set; } = "";
    [Required(ErrorMessage = "Nhập mật khẩu."), StringLength(128, ErrorMessage = "Mật khẩu tối đa 128 ký tự.")]
    public string Password { get; set; } = "";
}
public class RegistrationForm : PasswordForm, IValidatableObject
{
    [Required(ErrorMessage = "Nhập tên đăng nhập."), StringLength(50, MinimumLength = 3, ErrorMessage = "Tên đăng nhập từ 3 đến 50 ký tự."), RegularExpression(@"[a-zA-Z0-9_.-]+", ErrorMessage = "Tên đăng nhập chỉ gồm chữ, số, dấu chấm, gạch dưới hoặc gạch ngang.")]
    public string Username { get; set; } = "";
    [Required(ErrorMessage = "Nhập tên hiển thị."), StringLength(150, ErrorMessage = "Tên hiển thị tối đa 150 ký tự.")]
    public string DisplayName { get; set; } = "";
    [Required(ErrorMessage = "Nhập email đã đăng ký với ban quản lý."), StringLength(255, ErrorMessage = "Email tối đa 255 ký tự.")]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "Nhập số điện thoại."), StringLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    public string PhoneNumber { get; set; } = "";
    [Required(ErrorMessage = "Chọn loại giấy tờ.")]
    public string IdentityType { get; set; } = "";
    [Required(ErrorMessage = "Nhập số giấy tờ."), StringLength(30, ErrorMessage = "Số giấy tờ tối đa 30 ký tự.")]
    public string IdentityNumber { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(IdentityType) || string.IsNullOrWhiteSpace(IdentityNumber)) yield break;
        if (!MailAddress.TryCreate(Email.Trim(), out _))
            yield return new ValidationResult("Email không hợp lệ.", [nameof(Email)]);
        if (!System.Text.RegularExpressions.Regex.IsMatch(PhoneNumber.Trim(), @"^(?:\+84|0)\d{9,10}$"))
            yield return new ValidationResult("Số điện thoại không đúng định dạng.", [nameof(PhoneNumber)]);
        if (!System.Text.RegularExpressions.Regex.IsMatch(IdentityNumber.Trim(), @"^[0-9\s./-]+$"))
        {
            yield return new ValidationResult("Số giấy tờ chỉ được gồm chữ số và dấu phân cách.", [nameof(IdentityNumber)]);
            yield break;
        }
        var digits = new string(IdentityNumber.Where(character => character is >= '0' and <= '9').ToArray());
        if (IdentityType == "CCCD" && digits.Length != 12)
            yield return new ValidationResult("Số CCCD phải gồm đúng 12 chữ số.", [nameof(IdentityNumber)]);
        if (IdentityType == "CMND" && digits.Length is not (9 or 12))
            yield return new ValidationResult("Số CMND phải gồm 9 hoặc 12 chữ số.", [nameof(IdentityNumber)]);
    }
}
public class PasswordForm
{
    [Required(ErrorMessage = "Nhập mật khẩu mới."), StringLength(128, MinimumLength = 12, ErrorMessage = "Mật khẩu phải có từ 12 đến 128 ký tự.")]
    public string Password { get; set; } = "";
    [Required(ErrorMessage = "Nhập lại mật khẩu mới."), Compare(nameof(Password), ErrorMessage = "Hai mật khẩu không trùng nhau.")]
    public string Confirmation { get; set; } = "";
}
public class ChangePasswordForm : PasswordForm
{
    [Required(ErrorMessage = "Nhập mật khẩu hiện tại."), StringLength(128, ErrorMessage = "Mật khẩu tối đa 128 ký tự.")]
    public string CurrentPassword { get; set; } = "";
}
public class EmailForm
{
    [Required(ErrorMessage = "Nhập email của tài khoản."), EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(255, ErrorMessage = "Email tối đa 255 ký tự.")]
    public string Email { get; set; } = "";
}
public class OtpForm
{
    [Required(ErrorMessage = "Nhập mã xác minh."), RegularExpression(@"\d{6}", ErrorMessage = "Mã xác minh gồm 6 chữ số.")]
    public string Code { get; set; } = "";
}
public class ProfileForm
{
    [Required(ErrorMessage = "Nhập tên hiển thị."), StringLength(150, ErrorMessage = "Tên hiển thị tối đa 150 ký tự.")]
    public string DisplayName { get; set; } = "";
    [StringLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    public string? PhoneNumber { get; set; }
}
