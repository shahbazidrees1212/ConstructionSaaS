using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Identity;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

static class ManagerWorkspaceChecks
{
    public static async Task RunAsync(IServiceProvider services, string apiUrl, int projectId, int companyId, int foreignProject, string employeeId, Action<bool, string> check)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var manager = new ApplicationUser { UserName = "managerworkspace@example.test", Email = "managerworkspace@example.test", CompanyId = companyId, IsActive = true };
        check((await users.CreateAsync(manager, "Manager!Pass123")).Succeeded, "Create manager test account");
        await users.AddToRoleAsync(manager, "ProjectManager");
        var hidden = new Project { CompanyId = companyId, Name = "UNASSIGNED PRIVATE", ProjectCode = "HIDDEN", Country = "PK", CurrencyCode = "PKR" };
        db.Projects.Add(hidden);
        var assignment = new ProjectAssignment { ProjectId = projectId, UserId = manager.Id };
        db.ProjectAssignments.Add(assignment);
        await db.SaveChangesAsync();
        using var client = new HttpClient { BaseAddress = new Uri(apiUrl) };
        string Token() => scope.ServiceProvider.GetRequiredService<ITokenService>().CreateToken(manager.Id, manager.Email!, new[] { "ProjectManager" }, companyId, manager.SecurityStamp);
        var token = Token();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            db.WorkspaceActivities.Add(new WorkspaceActivity { CompanyId = companyId, ProjectId = projectId, Category = "Payments", Actor = manager.Email!, Description = "PRIVATE-FINANCIAL-AUDIT" });
            await db.SaveChangesAsync();
            check(!(await client.GetStringAsync("/api/workspace/activity")).Contains("PRIVATE-FINANCIAL-AUDIT"), "Manager activity excludes financial audit even for matching actor");
            check(!(await client.GetStringAsync("/api/task-workspace")).Contains("PRIVATE-FINANCIAL-AUDIT"), "Manager project history does not expose invoice financial audit");
            using var profile = await client.GetAsync("/api/account");
            check(profile.IsSuccessStatusCode && (await profile.Content.ReadAsStringAsync()).Contains(manager.Email!), "Manager sees own profile");
            using var activate = await client.PostAsync($"/api/account/team/{employeeId}/activate", null);
            check(activate.StatusCode == HttpStatusCode.Forbidden, "Manager cannot administer team accounts");
            var report = await client.GetFromJsonAsync<JsonElement>("/api/workspace/reports");
            check(report.GetProperty("projects").GetArrayLength() == 1 && report.GetProperty("projects")[0].GetProperty("id").GetInt32() == projectId, "Manager reports only assigned projects");
            var export = await client.GetStringAsync("/api/workspace/reports/export");
            check(!export.Contains("UNASSIGNED PRIVATE") && !export.Contains("PRIVATE OTHER TENANT"), "Manager CSV excludes inaccessible projects");
            object Input() => new { title = "Manager checkpoint", dueDateUtc = "2030-01-01", progress = 20 };
            foreach (var blockedId in new[] { hidden.Id, foreignProject })
            {
                using var list = await client.GetAsync($"/api/workspace/projects/{blockedId}/milestones");
                check(list.StatusCode == HttpStatusCode.NotFound, "Manager cannot read unassigned or foreign milestones");
                using var create = await client.PostAsJsonAsync($"/api/workspace/projects/{blockedId}/milestones", Input());
                check(create.StatusCode == HttpStatusCode.NotFound, "Manager cannot change unassigned or foreign project");
                using var docs = await client.GetAsync($"/api/workspace/projects/{blockedId}/documents");
                check(docs.StatusCode == HttpStatusCode.NotFound, "Manager cannot list inaccessible documents");
            }
            using var created = await client.PostAsJsonAsync($"/api/workspace/projects/{projectId}/milestones", Input());
            check(created.StatusCode == HttpStatusCode.Created, "Manager creates assigned milestone");
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            using var updated = await client.PutAsJsonAsync($"/api/workspace/milestones/{id}", new { title = "Manager checkpoint", dueDateUtc = "2030-01-01", progress = 100 });
            check(updated.IsSuccessStatusCode, "Manager updates milestone progress");
            var admin = await (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id where u.CompanyId == companyId && r.Name == "CompanyAdmin" select u).FirstAsync();
            var protectedDocs = new[] {
                new ProjectDocument { ProjectId = projectId, FileName = "admin-owned.txt", ContentType = "text/plain", Content = Encoding.UTF8.GetBytes("Admin file"), Size = 10, UploadedBy = admin.Email!, UploadedByUserId = admin.Id },
                new ProjectDocument { ProjectId = projectId, FileName = "other-manager.txt", ContentType = "text/plain", Content = Encoding.UTF8.GetBytes("Other file"), Size = 10, UploadedBy = manager.Email!, UploadedByUserId = employeeId },
                new ProjectDocument { ProjectId = projectId, FileName = "legacy.txt", ContentType = "text/plain", Content = Encoding.UTF8.GetBytes("Legacy file"), Size = 11, UploadedBy = manager.Email! }
            };
            db.ProjectDocuments.AddRange(protectedDocs); await db.SaveChangesAsync();
            foreach (var protectedDoc in protectedDocs) {
                using var denied = await client.DeleteAsync($"/api/workspace/documents/{protectedDoc.Id}");
                check(denied.StatusCode == HttpStatusCode.Forbidden, $"Manager cannot delete {protectedDoc.FileName}, including a matching email");
                using var readable = await client.GetAsync($"/api/workspace/documents/{protectedDoc.Id}/download");
                check(readable.IsSuccessStatusCode, "Protected document remains available to download");
            }
            var protectedList = await client.GetFromJsonAsync<JsonElement>($"/api/workspace/projects/{projectId}/documents");
            check(protectedList.EnumerateArray().Where(d => protectedDocs.Any(p => p.Id == d.GetProperty("id").GetInt32())).All(d => !d.GetProperty("canDelete").GetBoolean()), "API hides deletion capability for non-owned and legacy files");
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("Manager document")), "file", "manager.txt");
            using var uploaded = await client.PostAsync($"/api/workspace/projects/{projectId}/documents", form);
            check(uploaded.StatusCode == HttpStatusCode.Created, "Manager uploads assigned project document");
            var doc = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            check(await client.GetStringAsync($"/api/workspace/documents/{doc}/download") == "Manager document", "Manager downloads exact document content");
            var activity = await client.GetFromJsonAsync<JsonElement>("/api/workspace/activity");
            check(activity.GetProperty("items").EnumerateArray().All(a => a.GetProperty("actor").GetString() == manager.Email), "Manager activity excludes other users");
            await WorkspaceWebChecks.RunAsync(apiUrl, token, projectId, check, manager: true);
            db.ProjectAssignments.Remove(assignment); await db.SaveChangesAsync();
            using var revokedDoc = await client.GetAsync($"/api/workspace/documents/{doc}/download");
            check(revokedDoc.StatusCode == HttpStatusCode.NotFound, "Removed assignment immediately revokes document access");
            using var revokedEdit = await client.PutAsJsonAsync($"/api/workspace/milestones/{id}", Input());
            check(revokedEdit.StatusCode == HttpStatusCode.NotFound, "Removed assignment immediately revokes milestone update");
            using var revokedDelete = await client.DeleteAsync($"/api/workspace/documents/{doc}");
            check(revokedDelete.StatusCode == HttpStatusCode.NotFound, "Removed assignment blocks document deletion");
            db.ProjectAssignments.Add(new ProjectAssignment { ProjectId = projectId, UserId = manager.Id }); await db.SaveChangesAsync();
            using var ownAfterReassign = await client.GetAsync($"/api/workspace/documents/{doc}/download");
            check(ownAfterReassign.IsSuccessStatusCode, "Manager regains own document after reassignment");
            using var deleteMilestone = await client.DeleteAsync($"/api/workspace/milestones/{id}");
            check(deleteMilestone.IsSuccessStatusCode, "Manager removes milestone");
            using var password = await client.PostAsJsonAsync("/api/account/password", new { currentPassword = "Manager!Pass123", newPassword = "Updated!Manager123", confirmPassword = "Updated!Manager123" });
            check(password.IsSuccessStatusCode, "Manager updates password");
            using var oldToken = await client.GetAsync("/api/account");
            check(oldToken.StatusCode == HttpStatusCode.Unauthorized, "Manager password change revokes old token");
            async Task Login(string email) {
                using var login = await client.PostAsJsonAsync("/api/Auth/login", new { email, password = "Updated!Manager123" });
                check(login.IsSuccessStatusCode, "Manager can sign in after account change");
                var jwt = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            }
            await Login(manager.Email!);
            using var emailChange = await client.PostAsJsonAsync("/api/account/email", new { email = "updatedmanager@example.test", currentPassword = "Updated!Manager123" });
            check(emailChange.IsSuccessStatusCode, "Manager changes own profile email");
            using var revokedEmail = await client.GetAsync("/api/account");
            check(revokedEmail.StatusCode == HttpStatusCode.Unauthorized, "Manager email change invalidates old session");
            await Login("updatedmanager@example.test");
            var ownedList = await client.GetFromJsonAsync<JsonElement>($"/api/workspace/projects/{projectId}/documents");
            check(ownedList.EnumerateArray().Single(d => d.GetProperty("id").GetInt32() == doc).GetProperty("canDelete").GetBoolean(), "Own document remains deletable after email change");
            using var ownDelete = await client.DeleteAsync($"/api/workspace/documents/{doc}");
            check(ownDelete.IsSuccessStatusCode, "Manager deletes own document after email change");
            using var adminClient = new HttpClient { BaseAddress = new Uri(apiUrl) };
            adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<ITokenService>().CreateToken(admin.Id, admin.Email!, new[] { "CompanyAdmin" }, companyId, admin.SecurityStamp));
            foreach (var protectedDoc in protectedDocs) {
                using var adminDelete = await adminClient.DeleteAsync($"/api/workspace/documents/{protectedDoc.Id}");
                check(adminDelete.IsSuccessStatusCode, "Admin can remove own, other-user and legacy documents");
            }
            using var signout = await client.PostAsync("/api/account/sign-out-all", null);
            check(signout.IsSuccessStatusCode, "Manager signs out all sessions");
            using var revokedSession = await client.GetAsync("/api/account");
            check(revokedSession.StatusCode == HttpStatusCode.Unauthorized, "Manager all-session signout revokes token");
        }
        finally
        {
            await db.ProjectAssignments.Where(a => a.UserId == manager.Id).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == hidden.Id).ExecuteDeleteAsync();
        }
    }
}
