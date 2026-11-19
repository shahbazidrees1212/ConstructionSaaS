using System.ComponentModel.DataAnnotations;
namespace ConstructionSaaS.Application.DTOs.Auth;
public class ForgotPasswordInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
}
public class ResetPasswordInput : ForgotPasswordInput
{
    [Required, StringLength(4096)] public string Token { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 8)] public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = "";
}
