using ConstructionSaaS.Application.DTOs.Auth;
using ConstructionSaaS.API.Security;
using ConstructionSaaS.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ConstructionSaaS.API.Controllers;
[ApiController, Route("api/password-recovery"), AllowAnonymous, EnableRateLimiting("password-recovery")]
public class PasswordRecoveryController(UserManager<ApplicationUser> users, IPasswordResetMail mail, IConfiguration config, ILogger<PasswordRecoveryController> logger) : ControllerBase
{
    [HttpPost("forgot")]
    public async Task<IActionResult> Forgot(ForgotPasswordInput input)
    {
        if (!mail.IsConfigured || !Uri.TryCreate(config["PasswordRecovery:WebBaseUrl"], UriKind.Absolute, out var baseUri) || baseUri.Scheme != "https")
            return StatusCode(503, new { message = "Password recovery is not available yet. Contact your company administrator." });
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user?.IsActive == true) {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var link = new Uri(baseUri, "/PasswordRecovery/Reset").AbsoluteUri + "?email=" + Uri.EscapeDataString(user.Email!) + "&token=" + Uri.EscapeDataString(token);
            try { await mail.SendAsync(user.Email!, link); }
            catch (Exception ex) when (ex is System.Net.Mail.SmtpException or InvalidOperationException or FormatException) {
                // Do not expose account existence or include token/SMTP credentials in logs.
                logger.LogError("Password reset delivery failed. Check SMTP configuration and service availability.");
            }
        }
        return Ok(new { message = "If an active account matches this address, a reset link will be sent. Check your inbox and spam folder." });
    }
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(ResetPasswordInput input)
    {
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user?.IsActive != true) return BadRequest(new { message = "Reset link is invalid or expired. Request a new link." });
        var result = await users.ResetPasswordAsync(user, input.Token, input.NewPassword);
        if (!result.Succeeded) return BadRequest(new { message = "Reset failed. Use a fresh link and a password with uppercase, lowercase, a number and a symbol (at least 8 characters)." });
        // Identity rotates the security stamp, invalidating prior JWTs and this reset token.
        return Ok(new { message = "Password reset. Sign in with your new password." });
    }
}
