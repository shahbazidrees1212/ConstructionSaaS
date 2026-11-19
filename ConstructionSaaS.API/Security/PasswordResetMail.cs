using System.Net;
using System.Net.Mail;
namespace ConstructionSaaS.API.Security;
public interface IPasswordResetMail
{
    bool IsConfigured { get; }
    Task SendAsync(string email, string link);
}
public class PasswordResetMail(IConfiguration config) : IPasswordResetMail
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Smtp:Host"]) && !string.IsNullOrWhiteSpace(config["Smtp:From"]);
    public async Task SendAsync(string email, string link)
    {
        using var client = new SmtpClient(config["Smtp:Host"], config.GetValue("Smtp:Port", 587)) { EnableSsl = true, UseDefaultCredentials = false };
        if (!string.IsNullOrWhiteSpace(config["Smtp:Username"])) client.Credentials = new NetworkCredential(config["Smtp:Username"], config["Smtp:Password"]);
        using var mail = new MailMessage(config["Smtp:From"]!, email) { Subject = "Reset your ConstructionSaaS password", Body = $"A password reset was requested for your account. Open this link within one hour:\n\n{link}\n\nIf you did not request this, ignore this email." };
        await client.SendMailAsync(mail);
    }
}
