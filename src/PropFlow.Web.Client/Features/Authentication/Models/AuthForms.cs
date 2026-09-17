using System.ComponentModel.DataAnnotations;

namespace PropFlow.Web.Client.Features.Authentication.Models;

public class LoginForm
{
    [Required(ErrorMessage = "Nhập tên đăng nhập."), StringLength(50, ErrorMessage = "Tên đăng nhập tối đa 50 ký tự.")]
    public string Username { get; set; } = "";
    [Required(ErrorMessage = "Nhập mật khẩu."), StringLength(128, ErrorMessage = "Mật khẩu tối đa 128 ký tự.")]
    public string Password { get; set; } = "";
}
public class RegistrationForm : PasswordForm
{
    [Required(ErrorMessage = "Nhập tên đăng nhập."), StringLength(50, MinimumLength = 3, ErrorMessage = "Tên đăng nhập từ 3 đến 50 ký tự."), RegularExpression(@"[a-zA-Z0-9_.-]+", ErrorMessage = "Tên đăng nhập chỉ gồm chữ, số, dấu chấm, gạch dưới hoặc gạch ngang.")]
    public string Username { get; set; } = "";
    [Required(ErrorMessage = "Nhập tên hiển thị."), StringLength(150, ErrorMessage = "Tên hiển thị tối đa 150 ký tự.")]
    public string DisplayName { get; set; } = "";
    [Required(ErrorMessage = "Nhập email đã đăng ký với ban quản lý."), EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(255, ErrorMessage = "Email tối đa 255 ký tự.")]
    public string Email { get; set; } = "";
    [StringLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    public string? PhoneNumber { get; set; }
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
