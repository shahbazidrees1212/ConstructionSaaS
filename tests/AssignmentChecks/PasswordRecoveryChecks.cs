using System.Net;
using System.Net.Http.Json;
using ConstructionSaaS.API.Security;
using ConstructionSaaS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

sealed class CaptureResetMail : IPasswordResetMail
{
    public bool IsConfigured { get; set; } = true;
    public string? Link { get; private set; }
    public Task SendAsync(string email, string link) { Link = link; return Task.CompletedTask; }
}
static class PasswordRecoveryChecks
{
    public static async Task RunAsync(IServiceProvider services, string url, int company, Action<bool,string> check)
    {
        using var client = new HttpClient { BaseAddress = new Uri(url) };
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "recover@example.test", Email = "recover@example.test", CompanyId = company, IsActive = true };
        check((await users.CreateAsync(user, "Old!Password123")).Succeeded, "Seed password recovery user");
        var mail = (CaptureResetMail)services.GetRequiredService<IPasswordResetMail>();
        using var unknown = await client.PostAsJsonAsync("/api/password-recovery/forgot", new { email = "unknown@example.test" });
        using var known = await client.PostAsJsonAsync("/api/password-recovery/forgot", new { email = user.Email });
        check(known.IsSuccessStatusCode && await known.Content.ReadAsStringAsync() == await unknown.Content.ReadAsStringAsync(), "Recovery response does not reveal account existence");
        check(mail.Link?.StartsWith("https://localhost:7192/PasswordRecovery/Reset?") == true, "Reset email uses configured trusted HTTPS origin");
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(mail.Link!).Query);
        var token = query["token"].ToString();
        using(var bad = await client.PostAsJsonAsync("/api/password-recovery/reset", new { email=user.Email, token="forged", newPassword="New!Password123", confirmPassword="New!Password123" })) check(bad.StatusCode == HttpStatusCode.BadRequest, "Forged reset token rejected");
        using(var reset = await client.PostAsJsonAsync("/api/password-recovery/reset", new { email=user.Email, token, newPassword="New!Password123", confirmPassword="New!Password123" })) check(reset.IsSuccessStatusCode, "Valid emailed token resets password");
        using(var replay = await client.PostAsJsonAsync("/api/password-recovery/reset", new { email=user.Email, token, newPassword="Again!Password123", confirmPassword="Again!Password123" })) check(replay.StatusCode == HttpStatusCode.BadRequest, "Reset token cannot be reused");
        await using var fresh = services.CreateAsyncScope();
        var freshUsers = fresh.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var updated = (await freshUsers.FindByIdAsync(user.Id))!;
        check(await freshUsers.CheckPasswordAsync(updated,"New!Password123") && !await freshUsers.CheckPasswordAsync(updated,"Old!Password123"), "New password works and old password fails");
        check(updated.SecurityStamp != user.SecurityStamp, "Recovery rotates security stamp to revoke existing sessions");
        mail.IsConfigured = false;
        using(var disabled = await client.PostAsJsonAsync("/api/password-recovery/forgot", new { email=user.Email })) check(disabled.StatusCode == HttpStatusCode.ServiceUnavailable, "Missing mail setup produces clear unavailable response");
    }
}
