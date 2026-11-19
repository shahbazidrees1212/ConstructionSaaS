using System.Security.Claims;
using System.Text;
using ConstructionSaaS.Application.DTOs.ProjectTasks;
using ConstructionSaaS.Application.DTOs.Workspace;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Controllers;
[ApiController, Route("api/task-workspace"), Authorize(Roles = "ProjectManager,SiteEngineer")]
public partial class TaskWorkspaceController(ApplicationDbContext db) : ControllerBase
{
    private int CompanyId => int.TryParse(User.FindFirstValue("company_id"), out var id) ? id : 0;
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private bool Manager => User.IsInRole("ProjectManager");
    private IQueryable<int> Projects => db.Projects.Where(p => p.CompanyId == CompanyId && p.Assignments.Any(a => a.UserId == UserId)).Select(p => p.Id);
    private IQueryable<Domain.Entities.ProjectTask> Tasks => db.ProjectTasks.Where(t => t.CompanyId == CompanyId && Projects.Contains(t.ProjectId) && (Manager || t.AssignedToUserId == UserId));

    [HttpGet]
    public async Task<IActionResult> Overview(int? projectId = null)
    {
        if (CompanyId <= 0 || UserId == "") return Forbid();
        if (projectId.HasValue && !await Projects.ContainsAsync(projectId.Value)) return NotFound();
        var projectIds = Projects.Where(id => !projectId.HasValue || id == projectId.Value);
        var model = new TaskWorkspaceDto { IsManager = Manager };
        model.Projects = await db.Projects.Where(p => Projects.Contains(p.Id)).OrderBy(p => p.Name).Select(p => new TaskProjectDto { Id = p.Id, Name = p.Name }).ToListAsync();
        var engineers = await (from a in db.ProjectAssignments join u in db.Users on a.UserId equals u.Id
            join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id
            where Projects.Contains(a.ProjectId) && u.CompanyId == CompanyId && u.IsActive && r.Name == "SiteEngineer"
            select new TaskEngineerDto { ProjectId = a.ProjectId, UserId = u.Id, Email = u.Email ?? "" }).Distinct().ToListAsync();
        model.Engineers = Manager ? engineers : new();
        var rows = await Tasks.Where(t => projectIds.Contains(t.ProjectId)).OrderBy(t => t.Status == "Completed").ThenBy(t => t.DueDateUtc).ToListAsync();
        var assigneeIds = rows.Select(t => t.AssignedToUserId).Distinct().ToList();
        var emails = await db.Users.Where(u => u.CompanyId == CompanyId && assigneeIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email);
        model.Tasks = rows.Select(t => new TaskItemDto { Id = t.Id, ProjectId = t.ProjectId, ProjectName = model.Projects.First(p => p.Id == t.ProjectId).Name,
            Title = t.Title, Description = t.Description, Status = t.Status, Priority = t.Priority, AssignedToUserId = t.AssignedToUserId,
            AssigneeEmail = t.AssignedToUserId == null ? null : emails.GetValueOrDefault(t.AssignedToUserId),
            AssigneeEligible = engineers.Any(e => e.ProjectId == t.ProjectId && e.UserId == t.AssignedToUserId), DueDateUtc = t.DueDateUtc, CreatedAtUtc = t.CreatedAtUtc, UpdatedAtUtc = t.UpdatedAtUtc }).ToList();
        model.Notifications = await db.WorkspaceNotifications.Where(n => n.CompanyId == CompanyId && n.RecipientUserId == UserId && projectIds.Contains(n.ProjectId))
            .OrderBy(n => n.ReadAtUtc != null).ThenByDescending(n => n.CreatedAtUtc).Take(100)
            .Select(n => new TaskNoticeDto { Id = n.Id, ProjectId = n.ProjectId, Message = n.Message, CreatedAtUtc = n.CreatedAtUtc, IsRead = n.ReadAtUtc != null }).ToListAsync();
        foreach (var t in model.Tasks.Where(t => t.Status != "Completed" && t.DueDateUtc.HasValue && t.DueDateUtc.Value.Date <= DateTime.UtcNow.Date.AddDays(3)))
            model.Notifications.Insert(0, new TaskNoticeDto { ProjectId = t.ProjectId, Message = $"{(t.IsOverdue ? "Overdue" : "Due soon")}: {t.Title} — {t.DueDateUtc:dd MMM yyyy}", CreatedAtUtc = t.DueDateUtc!.Value });
        var milestones = await db.ProjectMilestones.Where(m => projectIds.Contains(m.ProjectId) && m.Progress < 100 && m.Project.Status != Domain.Enums.ProjectStatus.Completed && m.Project.Status != Domain.Enums.ProjectStatus.Cancelled && m.DueDateUtc < DateTime.UtcNow.Date.AddDays(4)).ToListAsync();
        model.OverdueMilestones = milestones.Count(m => m.DueDateUtc.Date < DateTime.UtcNow.Date);
        foreach (var m in milestones) model.Notifications.Insert(0, new TaskNoticeDto { ProjectId = m.ProjectId, Message = $"Milestone {(m.DueDateUtc.Date < DateTime.UtcNow.Date ? "overdue" : "due soon")}: {m.Title} — {m.DueDateUtc:dd MMM yyyy}", CreatedAtUtc = m.DueDateUtc });
        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        model.History = await db.WorkspaceActivities.Where(a => a.CompanyId == CompanyId && a.Category != "Payments" && a.ProjectId.HasValue && projectIds.Contains(a.ProjectId.Value) && (Manager || a.Actor == email))
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id).Take(100)
            .Select(a => new ActivityDto { Id = a.Id, Category = a.Category, Description = a.Description, Actor = a.Actor, OccurredAtUtc = a.OccurredAtUtc }).ToListAsync();
        model.WorkEntries = await db.SiteWorkEntries.AsNoTracking().Where(e => projectIds.Contains(e.ProjectId) && (Manager || (e.TaskId.HasValue ? Tasks.Select(t => t.Id).Contains(e.TaskId.Value) : e.AuthorUserId == UserId)))
            .OrderByDescending(e => e.CreatedAtUtc).Take(200)
            .Select(e => new WorkEntryDto { Id = e.Id, ProjectId = e.ProjectId, TaskId = e.TaskId, AuthorEmail = e.AuthorEmail, Kind = e.Kind, Summary = e.Summary, Blockers = e.Blockers, ReportDate = e.ReportDate, CreatedAtUtc = e.CreatedAtUtc, FileName = e.FileName }).ToListAsync();
        return Ok(model);
    }

    [HttpPost("tasks/{id:int}/status")]
    public async Task<IActionResult> Status(int id, TaskStatusInput input)
    {
        var task = await Tasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return NotFound();
        if (task.Status != input.ExpectedStatus) return Conflict(new { message = "Task was updated by someone else. Refresh and try again." });
        if (!Manager && (input.Status == "Completed" || task.Status is "PendingReview" or "Completed"))
            return BadRequest(new { message = "Submit your completed work for review. Only a manager can approve or reopen submitted work." });
        if (input.Status == "Completed" && task.Status != "PendingReview")
            return BadRequest(new { message = "Only submitted work can be approved." });
        if (input.Status == "PendingReview" && string.IsNullOrWhiteSpace(task.AssignedToUserId))
            return BadRequest(new { message = "Assign an engineer before submitting work." });
        if ((input.Status is "OnHold" or "PendingReview" || task.Status is "Completed" or "PendingReview") && string.IsNullOrWhiteSpace(input.Note))
            return BadRequest(new { message = "Add a blocker reason, completion summary or review/reopening reason." });
        if (input.Status == task.Status) return NoContent();
        if (!string.IsNullOrWhiteSpace(input.Note))
            db.SiteWorkEntries.Add(new Domain.Entities.SiteWorkEntry { ProjectId = task.ProjectId, TaskId = task.Id, AuthorUserId = UserId, AuthorEmail = User.FindFirstValue(ClaimTypes.Email) ?? "",
                Kind = input.Status == "PendingReview" ? "Submitted" : input.Status == "Completed" ? "Approved" : input.Status == "OnHold" ? "Blocked" : task.Status is "Completed" or "PendingReview" ? "Reopened" : "Progress",
                Summary = input.Note.Trim() });
        task.Status = input.Status; task.UpdatedAtUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Task changed. Refresh and try again." }); }
        return NoContent();
    }
    [HttpPost("notifications/{id:long}/read")]
    public async Task<IActionResult> Read(long id)
    {
        var notice = await db.WorkspaceNotifications.FirstOrDefaultAsync(n => n.Id == id && n.CompanyId == CompanyId && n.RecipientUserId == UserId && Projects.Contains(n.ProjectId));
        if (notice == null) return NotFound();
        notice.ReadAtUtc ??= DateTime.UtcNow; await db.SaveChangesAsync(); return NoContent();
    }
    [HttpGet("export")]
    public async Task<IActionResult> Export(int? projectId = null)
    {
        var result = await Overview(projectId);
        if (result is not OkObjectResult { Value: TaskWorkspaceDto model }) return result;
        static string Cell(string? s) { s ??= ""; if (s.TrimStart().StartsWith('=') || s.TrimStart().StartsWith('+') || s.TrimStart().StartsWith('-') || s.TrimStart().StartsWith('@')) s = "'" + s; return "\"" + s.Replace("\"", "\"\"") + "\""; }
        var csv = new StringBuilder("Project,Task,Status,Priority,Engineer,Due UTC,Overdue\r\n");
        foreach (var t in model.Tasks) csv.AppendLine(string.Join(",", new[] { t.ProjectName, t.Title, t.Status, t.Priority, t.AssigneeEmail, t.DueDateUtc?.ToString("yyyy-MM-dd"), t.IsOverdue ? "Yes" : "No" }.Select(Cell)));
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "task-progress.csv");
    }
}
