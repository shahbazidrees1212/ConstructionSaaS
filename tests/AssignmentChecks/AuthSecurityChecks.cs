using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Identity;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

static class AuthSecurityChecks
{
    public static async Task RunAsync(IServiceProvider services, string url, Action<bool, string> check)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var company = new Company { Name = "Access audit", Email = "access@example.test", Country = "PK" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var user = new ApplicationUser { UserName = company.Email, Email = company.Email, CompanyId = company.Id };
        check((await users.CreateAsync(user, "Audit!Password123")).Succeeded &&
              (await users.AddToRoleAsync(user, "CompanyAdmin")).Succeeded, "Seed isolated authentication audit account");
        using var client = new HttpClient { BaseAddress = new Uri(url) };
        async Task<string> Failed(string email, string password)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
            check(response.StatusCode == HttpStatusCode.Unauthorized, "Invalid or unavailable account cannot sign in");
            return await response.Content.ReadAsStringAsync();
        }
        var unknown = await Failed("unknown-audit@example.test", "Audit!Password123");
        for (var attempt = 0; attempt < users.Options.Lockout.MaxFailedAccessAttempts; attempt++)
            check(await Failed(user.Email!, "Wrong!Password123") == unknown, "Incorrect password has generic response");
        await db.Entry(user).ReloadAsync();
        check(await users.IsLockedOutAsync(user), "Repeated incorrect passwords lock account");
        check(await Failed(user.Email!, "Audit!Password123") == unknown, "Locked account does not disclose its state");
        await users.SetLockoutEndDateAsync(user, null);
        user.IsActive = false;
        await users.UpdateAsync(user);
        check(await Failed(user.Email!, "Audit!Password123") == unknown, "Inactive account does not disclose its state");
        user.IsActive = true;
        await users.UpdateAsync(user);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "Audit!Password123" });
        check(login.IsSuccessStatusCode, "Active unlocked account can sign in");
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var stream = await client.GetAsync("/api/notifications/stream", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(deadline.Token));
        check((await reader.ReadLineAsync(deadline.Token))?.StartsWith("data: ") == true, "Active company can receive live inbox");
        using var deactivate = await client.DeleteAsync("/api/companies/" + company.Id);
        check(deactivate.IsSuccessStatusCode, "Company administrator can deactivate company");
        using var existingToken = await client.GetAsync("/api/workspace/reports");
        check(existingToken.StatusCode == HttpStatusCode.Unauthorized, "Company deactivation rejects existing JWT");
        check(await Failed(user.Email!, "Audit!Password123") == unknown, "Inactive company cannot obtain new JWT");
        services.GetRequiredService<NotificationSignal>().Publish(company.Id, user.Id);
        string? line;
        do { line = await reader.ReadLineAsync(deadline.Token); } while (line != null && !line.StartsWith("event:"));
        check(line == "event: unauthorized", "Open notification stream rejects inactive company on next snapshot");
    }
}
