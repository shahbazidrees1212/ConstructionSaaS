using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ConstructionSaaS.API.Security;
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Application.Services;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Identity;
using ConstructionSaaS.Infrastructure.Persistence;
using ConstructionSaaS.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

static class WorkspaceHttpChecks
{
    public static async Task RunAsync(string connection, int projectId, int companyId, int otherCompanyId, string adminId, string inactiveId, Action<bool, string> check)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Error);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = secret, ["Jwt:Issuer"] = "workspace-tests", ["Jwt:Audience"] = "workspace-tests" });
        builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<IAuditActor, HttpAuditActor>();
        builder.Services.AddSingleton<NotificationSignal>();
        builder.Services.AddDbContext<ApplicationDbContext>((sp, o) => o.UseSqlServer(connection).AddInterceptors(sp.GetRequiredService<NotificationSignal>()));
        builder.Services.AddIdentityCore<ApplicationUser>(o => { o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 8; })
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        builder.Services.AddScoped<ITokenService, JwtTokenService>(); builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<ICompanyService, CompanyService>(); builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
        builder.Services.AddScoped<IProjectAssignmentService, ProjectAssignmentService>(); builder.Services.AddScoped<IProjectAssignmentRepository, ProjectAssignmentRepository>(); builder.Services.AddScoped<IProjectAssignmentUserReader, ProjectAssignmentUserReader>();
        builder.Services.AddScoped<IProjectService, ProjectService>(); builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
        builder.Services.AddScoped<IProjectTaskService, ProjectTaskService>(); builder.Services.AddScoped<IProjectTaskRepository, ProjectTaskRepository>(); builder.Services.AddScoped<IProjectTaskAccessRepository, ProjectTaskAccessRepository>();
        builder.Services.AddControllers().AddApplicationPart(typeof(ConstructionSaaS.API.Controllers.WorkspaceController).Assembly);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), ValidateIssuer = true, ValidIssuer = "workspace-tests", ValidateAudience = true, ValidAudience = "workspace-tests", ValidateLifetime = true, ClockSkew = TimeSpan.Zero };
            o.Events = new JwtBearerEvents { OnTokenValidated = WorkspaceTokenValidation.ValidateAsync };
        });
        builder.Services.AddDataProtection();
        builder.Configuration["Payments:WebBaseUrl"] = "https://localhost:7192";
        builder.Services.AddSingleton<BillingChecks.FakeGateway>();
        builder.Services.AddSingleton<ConstructionSaaS.API.Payments.IPaymentGateway>(sp => sp.GetRequiredService<BillingChecks.FakeGateway>());
        builder.Services.AddScoped<ConstructionSaaS.API.Payments.BillingService>();
        builder.Services.AddScoped<ConstructionSaaS.API.Subscriptions.SubscriptionCatalog>();
        builder.Services.AddScoped<ConstructionSaaS.API.Subscriptions.SubscriptionService>();
        builder.Services.AddHttpClient("payments");
        builder.Services.AddScoped<ConstructionSaaS.API.Subscriptions.ISubscriptionGateway, ConstructionSaaS.API.Subscriptions.SubscriptionGateway>();
        builder.Services.AddRateLimiter(o => o.AddPolicy("payments", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("billing-tests", _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 1000, Window = TimeSpan.FromMinutes(1) })));
        builder.Services.AddSingleton<IPasswordResetMail, CaptureResetMail>();
        builder.Configuration["PasswordRecovery:WebBaseUrl"] = "https://localhost:7192";
        builder.Services.AddRateLimiter(o => o.AddPolicy("password-recovery", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("tests", _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1) })));
        builder.Services.AddAuthorization();
        await using var app = builder.Build(); app.Urls.Add("http://127.0.0.1:0"); app.UseAuthentication(); app.UseAuthorization(); app.UseMiddleware<ConstructionSaaS.API.Subscriptions.SubscriptionAccessMiddleware>(); app.UseRateLimiter(); app.MapControllers();
        int foreignProject;
        string adminEmail;
        string otherToken;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = (await users.FindByIdAsync(adminId))!; adminEmail = admin.Email!;
            check((await users.AddPasswordAsync(admin, "Original!Pass123")).Succeeded, "Seed admin password");
            var other = new ApplicationUser { UserName = "otheradmin@example.test", Email = "otheradmin@example.test", CompanyId = otherCompanyId, IsActive = true };
            check((await users.CreateAsync(other, "Other!Pass123")).Succeeded, "Seed other company admin");
            await users.AddToRoleAsync(other, "CompanyAdmin");
            otherToken = scope.ServiceProvider.GetRequiredService<ITokenService>().CreateToken(other.Id, other.Email!, new[] { "CompanyAdmin" }, other.CompanyId, other.SecurityStamp);
            var p = new Project { CompanyId = otherCompanyId, Name = "PRIVATE OTHER TENANT", ProjectCode = "PRIVATE", Country = "PK", CurrencyCode = "USD", Budget = 777 };
            db.Projects.Add(p); await db.SaveChangesAsync(); foreignProject = p.Id;
        }
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            async Task<string> Login(string email, string password)
            {
                using var response = await client.PostAsJsonAsync("/api/Auth/login", new { email, password });
                check(response.StatusCode == HttpStatusCode.OK, "Real login issues JWT");
                return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
            }
            void Use(string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using (var anonymous = await client.GetAsync("/api/workspace/reports")) check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Reports require authentication");
            var token = await Login(adminEmail, "Original!Pass123"); Use(token);
            using (var subscription = await client.GetAsync("/api/subscriptions")) check(subscription.IsSuccessStatusCode, "CompanyAdmin can open SaaS subscription billing");
            using (var owner = await client.GetAsync("/api/subscriptions/companies")) check(owner.StatusCode == HttpStatusCode.Forbidden, "CompanyAdmin cannot access platform-wide subscriptions");
            builder.Configuration["Subscriptions:Enabled"] = "true";
            try {
                using (var blocked = await client.GetAsync("/api/workspace/reports")) check((int)blocked.StatusCode == 402, "Unsubscribed company is blocked by actual workspace API middleware");
                using (var recovery = await client.GetAsync("/api/subscriptions")) check(recovery.IsSuccessStatusCode, "Unsubscribed admin can still reach billing recovery");
                using (var security = await client.GetAsync("/api/account")) check(security.IsSuccessStatusCode, "Expired subscription does not block account security");
                using (var forgedPlan = await client.PostAsJsonAsync("/api/subscriptions/checkout", new { planKey = "unknown", provider = "Stripe", companyId = otherCompanyId, amount = 0 })) check(forgedPlan.StatusCode == HttpStatusCode.ServiceUnavailable, "Unconfigured plan and browser amount cannot start checkout");
            } finally { builder.Configuration["Subscriptions:Enabled"] = "false"; }
            using (var account = await client.GetAsync("/api/account")) check(account.IsSuccessStatusCode, "Account details available to current admin");
            object Milestone(int progress = 0, string title = "Foundation") => new { title, dueDateUtc = DateTime.UtcNow.Date.AddDays(-1), progress, notes = "Inspect before handover" };
            using (var foreign = await client.PostAsJsonAsync($"/api/workspace/projects/{foreignProject}/milestones", Milestone())) check(foreign.StatusCode == HttpStatusCode.NotFound, "Block cross-company milestone creation");
            using (var invalid = await client.PostAsJsonAsync($"/api/workspace/projects/{projectId}/milestones", Milestone(101))) check(invalid.StatusCode == HttpStatusCode.BadRequest, "Reject invalid milestone progress");
            using (var blank = await client.PostAsJsonAsync($"/api/workspace/projects/{projectId}/milestones", Milestone(0, "  "))) check(blank.StatusCode == HttpStatusCode.BadRequest, "Reject blank milestone title");
            using var created = await client.PostAsJsonAsync($"/api/workspace/projects/{projectId}/milestones", Milestone());
            check(created.StatusCode == HttpStatusCode.Created, "Create milestone");
            var milestoneId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var list = await client.GetFromJsonAsync<JsonElement>($"/api/workspace/projects/{projectId}/milestones");
            check(list[0].GetProperty("isOverdue").GetBoolean(), "Incomplete past-due milestone is overdue");
            Use(otherToken);
            using (var edit = await client.PutAsJsonAsync($"/api/workspace/milestones/{milestoneId}", Milestone(100))) check(edit.StatusCode == HttpStatusCode.NotFound, "Block cross-company milestone update");
            using (var delete = await client.DeleteAsync($"/api/workspace/milestones/{milestoneId}")) check(delete.StatusCode == HttpStatusCode.NotFound, "Block cross-company milestone removal");
            Use(token);
            using (var complete = await client.PutAsJsonAsync($"/api/workspace/milestones/{milestoneId}", Milestone(100))) check(complete.StatusCode == HttpStatusCode.NoContent, "Complete milestone");
            list = await client.GetFromJsonAsync<JsonElement>($"/api/workspace/projects/{projectId}/milestones");
            check(!list[0].GetProperty("isOverdue").GetBoolean() && list[0].GetProperty("completedAtUtc").ValueKind == JsonValueKind.String, "Completed milestone has timestamp and is not overdue");
            using (var reopen = await client.PutAsJsonAsync($"/api/workspace/milestones/{milestoneId}", Milestone(50))) check(reopen.IsSuccessStatusCode, "Reopen milestone at partial progress");
            using var second = await client.PostAsJsonAsync($"/api/workspace/projects/{projectId}/milestones", Milestone(100, "Handover"));
            check(second.StatusCode == HttpStatusCode.Created, "Create second milestone");
            var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var report = await client.GetFromJsonAsync<JsonElement>("/api/workspace/reports");
            check(report.GetProperty("projects").GetArrayLength() == 1 && report.GetProperty("projects")[0].GetProperty("progress").GetInt32() == 75, "Report isolates company and averages milestone progress");
            async Task<HttpResponseMessage> Upload(string name, byte[] bytes, int? project = null)
            {
                using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", name);
                return await client.PostAsync($"/api/workspace/projects/{project ?? projectId}/documents", form);
            }
            using (var bad = await Upload("script.exe", new byte[] { 1, 2, 3 })) check(bad.StatusCode == HttpStatusCode.BadRequest, "Reject executable upload");
            using (var spoof = await Upload("fake.pdf", Encoding.UTF8.GetBytes("not a pdf"))) check(spoof.StatusCode == HttpStatusCode.BadRequest, "Reject spoofed file signature");
            using (var empty = await Upload("empty.txt", Array.Empty<byte>())) check(empty.StatusCode == HttpStatusCode.BadRequest, "Reject empty document");
            using (var oversized = await Upload("large.txt", new byte[10 * 1024 * 1024 + 1])) check(oversized.StatusCode == HttpStatusCode.BadRequest, "Reject document above 10 MB");
            using (var foreignUpload = await Upload("note.txt", Encoding.UTF8.GetBytes("note"), foreignProject)) check(foreignUpload.StatusCode == HttpStatusCode.NotFound, "Block upload to another company");
            var bytes = Encoding.UTF8.GetBytes("Project notes: inspection passed.");
            using var upload = await Upload("../../notes.txt", bytes);
            check(upload.StatusCode == HttpStatusCode.Created, "Upload valid document");
            var documentId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var documents = await client.GetFromJsonAsync<JsonElement>($"/api/workspace/projects/{projectId}/documents");
            check(documents[0].GetProperty("fileName").GetString() == "notes.txt", "Document path is stripped from file name");
            using (var download = await client.GetAsync($"/api/workspace/documents/{documentId}/download"))
                check(download.IsSuccessStatusCode && (await download.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes) && download.Content.Headers.ContentDisposition?.DispositionType == "attachment", "Download exact bytes as attachment");
            Use(otherToken);
            using (var read = await client.GetAsync($"/api/workspace/documents/{documentId}/download")) check(read.StatusCode == HttpStatusCode.NotFound, "Block foreign document download");
            using (var remove = await client.DeleteAsync($"/api/workspace/documents/{documentId}")) check(remove.StatusCode == HttpStatusCode.NotFound, "Block foreign document removal");
            Use(token);
            using (var csv = await client.GetAsync("/api/workspace/reports/export")) check(csv.IsSuccessStatusCode && csv.Content.Headers.ContentType?.MediaType == "text/csv" && !(await csv.Content.ReadAsStringAsync()).Contains("PRIVATE OTHER TENANT"), "CSV export contains only company projects");
            var activity = await client.GetFromJsonAsync<JsonElement>("/api/workspace/activity?category=Documents");
            check(activity.GetProperty("items").EnumerateArray().Any(a => a.GetProperty("description").GetString()!.Contains("notes.txt") && a.GetProperty("actor").GetString() == adminEmail), "Document activity persists with actor email");
            var allActivity = await client.GetStringAsync("/api/workspace/activity");
            check(!allActivity.Contains("PRIVATE OTHER TENANT") && !allActivity.Contains("Original!Pass123"), "Audit isolates company and excludes password data");
            using (var invalidPage = await client.GetAsync("/api/workspace/activity?page=0")) check(invalidPage.StatusCode == HttpStatusCode.BadRequest, "Reject invalid activity pagination");
            using (var removed = await client.DeleteAsync($"/api/workspace/documents/{documentId}")) check(removed.StatusCode == HttpStatusCode.NoContent, "Remove document");
            using (var missing = await client.GetAsync($"/api/workspace/documents/{documentId}/download")) check(missing.StatusCode == HttpStatusCode.NotFound, "Deleted document unavailable");
            using (var deleted = await client.DeleteAsync($"/api/workspace/milestones/{secondId}")) check(deleted.StatusCode == HttpStatusCode.NoContent, "Remove milestone");
            using (var activate = await client.PostAsync($"/api/account/team/{inactiveId}/activate", null)) check(activate.StatusCode == HttpStatusCode.NoContent, "Admin reactivates own employee");
            Use(otherToken);
            using (var activateForeign = await client.PostAsync($"/api/account/team/{inactiveId}/activate", null)) check(activateForeign.StatusCode == HttpStatusCode.NotFound, "Block foreign employee activation");
            Use(token);
            await BillingChecks.RunAsync(app.Services, app.Urls.Single(), token, otherToken, companyId, foreignProject, inactiveId, check);
            await WorkspaceWebChecks.RunAsync(app.Urls.Single(), token, projectId, check);
            await ManagerWorkspaceChecks.RunAsync(app.Services, app.Urls.Single(), projectId, companyId, foreignProject, inactiveId, check);
            await TaskWorkflowChecks.RunAsync(app.Services, app.Urls.Single(), projectId, companyId, foreignProject, otherCompanyId, check);
            using (var wrongPassword = await client.PostAsJsonAsync("/api/account/password", new { currentPassword = "wrong", newPassword = "Updated!Pass123", confirmPassword = "Updated!Pass123" })) check(wrongPassword.StatusCode == HttpStatusCode.BadRequest, "Wrong current password rejected");
            using (var weak = await client.PostAsJsonAsync("/api/account/password", new { currentPassword = "Original!Pass123", newPassword = "weak", confirmPassword = "weak" })) check(weak.StatusCode == HttpStatusCode.BadRequest, "Weak password rejected");
            using (var mismatch = await client.PostAsJsonAsync("/api/account/password", new { currentPassword = "Original!Pass123", newPassword = "Updated!Pass123", confirmPassword = "Different!Pass123" })) check(mismatch.StatusCode == HttpStatusCode.BadRequest, "Password confirmation mismatch rejected");
            using (var changed = await client.PostAsJsonAsync("/api/account/password", new { currentPassword = "Original!Pass123", newPassword = "Updated!Pass123", confirmPassword = "Updated!Pass123" })) check(changed.StatusCode == HttpStatusCode.NoContent, "Change password succeeds");
            using (var oldToken = await client.GetAsync("/api/account")) check(oldToken.StatusCode == HttpStatusCode.Unauthorized, "Password change invalidates old JWT");
            token = await Login(adminEmail, "Updated!Pass123"); Use(token);
            using (var duplicateEmail = await client.PostAsJsonAsync("/api/account/email", new { email = "otheradmin@example.test", currentPassword = "Updated!Pass123" })) check(duplicateEmail.StatusCode == HttpStatusCode.BadRequest, "Reject duplicate account email");
            using (var email = await client.PostAsJsonAsync("/api/account/email", new { email = "renamed@example.test", currentPassword = "Updated!Pass123" })) check(email.StatusCode == HttpStatusCode.NoContent, "Change account email");
            using (var oldEmailToken = await client.GetAsync("/api/account")) check(oldEmailToken.StatusCode == HttpStatusCode.Unauthorized, "Email change invalidates old JWT");
            token = await Login("renamed@example.test", "Updated!Pass123"); Use(token);
            using (var logout = await client.PostAsync("/api/account/sign-out-all", null)) check(logout.StatusCode == HttpStatusCode.NoContent, "Sign out all sessions");
            using (var revoked = await client.GetAsync("/api/workspace/reports")) check(revoked.StatusCode == HttpStatusCode.Unauthorized, "Sign-out-all rejects old JWT");
            await PasswordRecoveryChecks.RunAsync(app.Services, app.Urls.Single(), companyId, check);
            await AuthSecurityChecks.RunAsync(app.Services, app.Urls.Single(), check);
            // Leave a milestone and document for the outer project-deletion cascade test.
            token = await Login("renamed@example.test", "Updated!Pass123"); Use(token);
            using var retained = await Upload("retained.txt", bytes); check(retained.StatusCode == HttpStatusCode.Created, "Create document for project-delete cascade");
        }
        finally { await app.StopAsync(); }
    }
}

