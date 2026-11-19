using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ConstructionSaaS.Application.DTOs.ProjectTasks;
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Identity;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

static class TaskWorkflowChecks
{
    public static async Task RunAsync(IServiceProvider services, string url, int projectId, int companyId, int foreignProject, int otherCompanyId, Action<bool,string> check)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        async Task<ApplicationUser> User(string name, string role, bool assigned = true, bool active = true, bool foreign = false) {
            var user = new ApplicationUser { UserName = name + "@task.test", Email = name + "@task.test", CompanyId = foreign ? otherCompanyId : companyId, IsActive = active };
            check((await users.CreateAsync(user)).Succeeded, "Seed task user " + name); await users.AddToRoleAsync(user, role);
            if (assigned) db.ProjectAssignments.Add(new ProjectAssignment { ProjectId = foreign ? foreignProject : projectId, UserId = user.Id });
            await db.SaveChangesAsync(); return user;
        }
        var manager = await User("taskmanager", "ProjectManager");
        var observingManager = await User("observingmanager", "ProjectManager");
        var engineer = await User("engineer", "SiteEngineer");
        var colleague = await User("colleague", "SiteEngineer");
        var inactive = await User("inactiveengineer", "SiteEngineer", active:false);
        var unassigned = await User("unassignedengineer", "SiteEngineer", assigned:false);
        var foreign = await User("foreignengineer", "SiteEngineer", foreign:true);
        var outsiderManager = await User("outsidemanager", "ProjectManager", assigned:false);
        string Token(ApplicationUser u, string role) => scope.ServiceProvider.GetRequiredService<ITokenService>().CreateToken(u.Id, u.Email!, new[] { role }, u.CompanyId, u.SecurityStamp);
        using var client = new HttpClient { BaseAddress = new Uri(url) };
        void Use(ApplicationUser u, string role = "SiteEngineer") => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(u,role));
        object Create(string? assignee, int? project = null, string title = "Inspect reinforcement") => new { projectId = project ?? projectId, title, assignedToUserId = assignee, priority = "High", dueDateUtc = DateTime.UtcNow.Date.AddDays(-1), description = "Verify before concrete pour" };
        using (var anonymous = await client.GetAsync("/api/task-workspace")) check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Task board requires JWT");
        Use(manager,"ProjectManager");
        foreach (var invalid in new[] { inactive, unassigned, foreign, manager }) {
            using var response = await client.PostAsJsonAsync("/api/project-tasks", Create(invalid.Id));
            check(response.StatusCode == HttpStatusCode.BadRequest, "Block inactive, unassigned, foreign or non-engineer assignee");
        }
        using (var cross = await client.PostAsJsonAsync("/api/project-tasks", Create(engineer.Id, foreignProject))) check(cross.StatusCode == HttpStatusCode.Forbidden, "Manager cannot create task in foreign project");
        Use(outsiderManager,"ProjectManager");
        using (var access = await client.GetAsync($"/api/project-tasks/project/{projectId}")) check(access.StatusCode == HttpStatusCode.Forbidden, "Unassigned manager cannot list project tasks");
        Use(manager,"ProjectManager");
        using var created = await client.PostAsJsonAsync("/api/project-tasks", Create(engineer.Id));
        check(created.StatusCode == HttpStatusCode.Created, "Manager creates engineer task with production company claim");
        var task = (await created.Content.ReadFromJsonAsync<ProjectTaskDto>())!;
        check(!await db.WorkspaceNotifications.AnyAsync(n => n.RecipientUserId == manager.Id && n.Message.Contains(task.Title)), "Task creator receives no self-notification");
        check(await db.WorkspaceNotifications.AnyAsync(n => n.RecipientUserId == observingManager.Id && n.Message.Contains(task.Title)), "Other assigned manager still receives task notification");
        check(await db.WorkspaceActivities.AnyAsync(a => a.Actor == manager.Email && a.Description.Contains(task.Title)), "Creator action remains in audit history");
        using var other = await client.PostAsJsonAsync("/api/project-tasks", Create(colleague.Id, title:"Private colleague task"));
        check(other.IsSuccessStatusCode, "Assign second engineer task");
        var otherId = (await other.Content.ReadFromJsonAsync<ProjectTaskDto>())!.Id;
        Use(engineer);
        var board = (await client.GetFromJsonAsync<TaskWorkspaceDto>("/api/task-workspace"))!;
        check(board.Tasks.Count == 1 && board.Tasks[0].Id == task.Id, "Engineer sees only own assigned task");
        check(board.Tasks[0].IsOverdue && board.Notifications.Any(n => n.Id == 0), "Engineer sees overdue task alert");
        check(board.Notifications.Any(n => n.Id > 0 && n.Message.Contains(task.Title)), "Task assignment creates persistent notification");
        using (var createDenied = await client.PostAsJsonAsync("/api/project-tasks", Create(engineer.Id))) check(createDenied.StatusCode == HttpStatusCode.Forbidden, "Engineer cannot create tasks");
        using (var deleteDenied = await client.DeleteAsync($"/api/project-tasks/{task.Id}")) check(deleteDenied.StatusCode == HttpStatusCode.Forbidden, "Engineer cannot delete tasks");
        using (var otherStatus = await client.PostAsJsonAsync($"/api/task-workspace/tasks/{otherId}/status", new { status="Completed", expectedStatus="Pending" })) check(otherStatus.StatusCode == HttpStatusCode.NotFound, "Engineer cannot update another engineer task");
        using (var invalid = await client.PostAsJsonAsync($"/api/task-workspace/tasks/{task.Id}/status", new { status="Invalid", expectedStatus="Pending" })) check(invalid.StatusCode == HttpStatusCode.BadRequest, "Invalid task status rejected");
        var engineerNoticesBefore = await db.WorkspaceNotifications.CountAsync(n => n.RecipientUserId == engineer.Id);
        using (var progress = await client.PostAsJsonAsync($"/api/task-workspace/tasks/{task.Id}/status", new { status="InProgress", expectedStatus="Pending" })) check(progress.IsSuccessStatusCode, "Engineer starts assigned task");
        check(await db.WorkspaceNotifications.CountAsync(n => n.RecipientUserId == engineer.Id) == engineerNoticesBefore, "Engineer receives no notification for own progress update");
        check(await db.WorkspaceNotifications.AnyAsync(n => n.RecipientUserId == manager.Id && n.Message.Contains(task.Title) && n.Message.Contains("InProgress")), "Manager receives engineer progress notification");
        using (var stale = await client.PostAsJsonAsync($"/api/task-workspace/tasks/{task.Id}/status", new { status="Completed", expectedStatus="Pending" })) check(stale.StatusCode == HttpStatusCode.Conflict, "Stale status update returns 409");
        using (var denied = await client.PostAsJsonAsync($"/api/task-workspace/tasks/{task.Id}/status", new { status="Completed", expectedStatus="InProgress" })) check(denied.StatusCode == HttpStatusCode.BadRequest, "Engineer cannot self-approve completion");
        async Task<HttpResponseMessage> Work(int project, int? taskId, string summary, string? fileName = null) {
            using var form = new MultipartFormDataContent(); form.Add(new StringContent(project.ToString()), "ProjectId"); form.Add(new StringContent(summary), "Summary");
            if(taskId.HasValue) form.Add(new StringContent(taskId.Value.ToString()), "TaskId"); else form.Add(new StringContent(DateTime.UtcNow.ToString("yyyy-MM-dd")), "ReportDate");
            if(fileName != null) form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("%PDF-1.7\n test evidence")), "file", fileName);
            return await client.PostAsync("/api/task-workspace/work", form);
        }
        using(var wrong = await Work(foreignProject, null, "foreign")) check(wrong.StatusCode == HttpStatusCode.NotFound, "Daily report blocks foreign project");
        using(var wrong = await Work(projectId, otherId, "other task")) check(wrong.StatusCode == HttpStatusCode.NotFound, "Work note blocks colleague task");
        using(var bad = await Work(projectId, task.Id, "evidence", "bad.exe")) check(bad.StatusCode == HttpStatusCode.BadRequest, "Evidence rejects unsupported file");
        using var note = await Work(projectId, task.Id, "Reinforcement inspected", "inspection.pdf");
        check(note.StatusCode == HttpStatusCode.Created, "Save work note and PDF evidence");
        var evidenceId = (await note.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetInt32();
        using(var file = await client.GetAsync($"/api/task-workspace/work/{evidenceId}/evidence")) check(file.IsSuccessStatusCode && file.Content.Headers.ContentDisposition?.DispositionType == "attachment", "Engineer downloads own task evidence");
        Use(colleague);
        using(var file = await client.GetAsync($"/api/task-workspace/work/{evidenceId}/evidence")) check(file.StatusCode == HttpStatusCode.NotFound, "Colleague cannot download another task evidence");
        Use(engineer);
        using(var daily = await Work(projectId, null, "Site inspection complete")) check(daily.StatusCode == HttpStatusCode.Created, "Submit daily site report");
        using(var duplicate = await Work(projectId, null, "Duplicate")) check(duplicate.StatusCode == HttpStatusCode.Conflict, "Daily report duplicate returns 409");
        using(var blocked = await client.PostAsJsonAsync($"/api/task-workspace/tasks/{task.Id}/status", new { status="OnHold", expectedStatus="InProgress" })) check(blocked.StatusCode == HttpStatusCode.BadRequest, "Blocker reason required");
        using(var empty = await client.PostAsJsonAsync($"/api/task-workspace/tasks/{task.Id}/status", new { status="PendingReview", expectedStatus="InProgress" })) check(empty.StatusCode == HttpStatusCode.BadRequest, "Completion summary required");
        using(var submitted = await client.PostAsJsonAsync($"/api/task-workspace/tasks/{task.Id}/status", new { status="PendingReview", expectedStatus="InProgress", note="Inspection finished; evidence attached" })) check(submitted.IsSuccessStatusCode, "Engineer submits for manager review: " + await submitted.Content.ReadAsStringAsync());
        using(var locked = await Work(projectId, task.Id, "changed after submission")) check(locked.StatusCode == HttpStatusCode.BadRequest, "Submitted evidence is locked for engineer");
        Use(manager,"ProjectManager");
        var managerNoticesBefore = await db.WorkspaceNotifications.CountAsync(n => n.RecipientUserId == manager.Id);
        using(var approved = await client.PostAsJsonAsync($"/api/task-workspace/tasks/{task.Id}/status", new { status="Completed", expectedStatus="PendingReview", note="Reviewed and approved" })) check(approved.IsSuccessStatusCode, "Manager approves completion");
        Use(engineer);
        board = (await client.GetFromJsonAsync<TaskWorkspaceDto>("/api/task-workspace"))!;
        check(!board.Tasks[0].IsOverdue, "Completed task clears overdue alert");
        check(await db.WorkspaceNotifications.CountAsync(n => n.RecipientUserId == manager.Id) == managerNoticesBefore, "Manager receives no self-notification for approval or review note");
        check(board.Notifications.Any(n => n.Id > 0 && n.Message.Contains(task.Title) && n.Message.Contains("Completed")), "Engineer receives manager approval notification");
        check(board.History.Any(a => a.Actor == engineer.Email && a.Description.Contains("InProgress -> PendingReview")), "Audit retains actor and before/after status");
        await NotificationChecks.RunAsync(services, url, companyId, projectId, engineer.Id,
            Token(engineer, "SiteEngineer"), Token(colleague, "SiteEngineer"), Token(foreign, "SiteEngineer"), check);
        var noticeId = board.Notifications.First(n => n.Id > 0).Id;
        using (var read = await client.PostAsync($"/api/task-workspace/notifications/{noticeId}/read", null)) check(read.IsSuccessStatusCode, "Engineer marks own notification read");
        Use(colleague);
        using (var wrongRead = await client.PostAsync($"/api/task-workspace/notifications/{noticeId}/read", null)) check(wrongRead.StatusCode == HttpStatusCode.NotFound, "Notification read is recipient scoped");
        Use(manager,"ProjectManager");
        using (var milestone = await client.PostAsJsonAsync($"/api/workspace/projects/{projectId}/milestones", new { title="Task integration milestone", dueDateUtc=DateTime.UtcNow.AddDays(-1), progress=10 })) check(milestone.IsSuccessStatusCode, "Manager creates milestone notification");
        Use(engineer);
        board = (await client.GetFromJsonAsync<TaskWorkspaceDto>("/api/task-workspace"))!;
        check(board.Notifications.Any(n => n.Id > 0 && n.Message.Contains("Task integration milestone")) && board.OverdueMilestones > 0, "Engineer receives milestone updates and overdue alerts");
        check(!await db.WorkspaceNotifications.AnyAsync(n => n.RecipientUserId == manager.Id && n.Message.Contains("Task integration milestone")), "Milestone creator receives no self-notification");
        var export = await client.GetStringAsync("/api/task-workspace/export");
        check(export.Contains(task.Title) && !export.Contains("Private colleague task"), "Engineer CSV includes only own tasks");
        await TaskWebChecks.RunAsync(url, Token(engineer,"SiteEngineer"), Token(manager,"ProjectManager"), projectId, task.Id, check);
        await db.ProjectAssignments.Where(a => a.ProjectId == projectId && a.UserId == engineer.Id).ExecuteDeleteAsync();
        board = (await client.GetFromJsonAsync<TaskWorkspaceDto>("/api/task-workspace"))!;
        check(board.Tasks.Count == 0 && board.Notifications.Count == 0 && board.History.Count == 0, "Assignment removal revokes tasks, notifications and history");
        var revokedInbox = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/notifications");
        check(revokedInbox.GetProperty("items").GetArrayLength() == 0, "Assignment removal revokes bell inbox access");
        using (var lost = await client.PostAsJsonAsync($"/api/task-workspace/tasks/{task.Id}/status", new { status="Pending", expectedStatus="Completed" })) check(lost.StatusCode == HttpStatusCode.NotFound, "Removed engineer cannot update former task");
        Use(manager,"ProjectManager");
        board = (await client.GetFromJsonAsync<TaskWorkspaceDto>("/api/task-workspace"))!;
        check(!board.Tasks.Single(t => t.Id == task.Id).AssigneeEligible, "Manager sees assignment review flag");
        using (var reopen = await client.PostAsJsonAsync($"/api/task-workspace/tasks/{task.Id}/status", new { status="InProgress", expectedStatus="Completed", note="Further inspection required; reassigning to colleague" })) check(reopen.IsSuccessStatusCode, "Manager reopens approved work with reason");
        using (var reassign = await client.PutAsJsonAsync($"/api/project-tasks/{task.Id}", new { title=task.Title, status="InProgress", expectedStatus="InProgress", priority="Critical", assignedToUserId=colleague.Id })) check(reassign.IsSuccessStatusCode, "Manager reassigns task to eligible engineer");
        Use(engineer);
        using (var hiddenProject = await client.GetAsync($"/api/task-workspace?projectId={foreignProject}")) check(hiddenProject.StatusCode == HttpStatusCode.NotFound, "Engineer cannot select a foreign project");
        Use(manager,"ProjectManager");
        using (var delete = await client.DeleteAsync($"/api/project-tasks/{otherId}")) check(delete.IsSuccessStatusCode, "Manager deletes task");
        check(await db.WorkspaceActivities.AnyAsync(a => a.Category == "Tasks" && a.Description.Contains("Removed: Private colleague task")), "Task removal preserves audit record");
        // Keep one task so the outer project deletion test verifies task cleanup too.
    }
}
