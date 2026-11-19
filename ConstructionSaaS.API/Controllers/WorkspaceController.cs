using System.Security.Claims;
using System.Text;
using ConstructionSaaS.Application.DTOs.Workspace;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Controllers;

[ApiController, Route("api/workspace"), Authorize(Roles = "CompanyAdmin,ProjectManager")]
public class WorkspaceController(ApplicationDbContext db) : Controller
{
    private int CompanyId => int.TryParse(User.FindFirstValue("company_id"), out var id) ? id : 0;
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (CompanyId <= 0) context.Result = Forbid();
        base.OnActionExecuting(context);
    }
    private bool IsAdmin => User.IsInRole("CompanyAdmin");
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private IQueryable<Project> AccessibleProjects => db.Projects.Where(p => p.CompanyId == CompanyId &&
        (IsAdmin || p.Assignments.Any(a => a.UserId == UserId)));
    private Task<bool> OwnsProject(int id) => AccessibleProjects.AnyAsync(p => p.Id == id);

    [HttpGet("projects/{projectId:int}/milestones")]
    public async Task<IActionResult> Milestones(int projectId)
    {
        if (!await OwnsProject(projectId)) return NotFound();
        return Ok(await db.ProjectMilestones.AsNoTracking().Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.DueDateUtc).ThenBy(m => m.Id).Select(m => new MilestoneDto
            { Id = m.Id, ProjectId = m.ProjectId, Title = m.Title, Notes = m.Notes, DueDateUtc = m.DueDateUtc, Progress = m.Progress, CompletedAtUtc = m.CompletedAtUtc }).ToListAsync());
    }
    private static string? ValidateMilestone(MilestoneInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) return "Milestone title is required.";
        if (input.DueDateUtc.Year < 2000 || input.DueDateUtc.Year > 2100) return "Enter a due date between 2000 and 2100.";
        return null;
    }
    [HttpPost("projects/{projectId:int}/milestones")]
    public async Task<IActionResult> CreateMilestone(int projectId, MilestoneInput input)
    {
        if (!await OwnsProject(projectId)) return NotFound();
        if (ValidateMilestone(input) is string error) return BadRequest(new { message = error });
        var item = new ProjectMilestone { ProjectId = projectId, Title = input.Title.Trim(), Notes = input.Notes?.Trim(), DueDateUtc = input.DueDateUtc.Date, Progress = input.Progress, CompletedAtUtc = input.Progress == 100 ? DateTime.UtcNow : null };
        db.ProjectMilestones.Add(item); await db.SaveChangesAsync();
        return StatusCode(201, new { item.Id });
    }
    [HttpPut("milestones/{id:int}")]
    public async Task<IActionResult> UpdateMilestone(int id, MilestoneInput input)
    {
        var item = await db.ProjectMilestones.FirstOrDefaultAsync(m => m.Id == id && AccessibleProjects.Select(p => p.Id).Contains(m.ProjectId));
        if (item == null) return NotFound();
        if (ValidateMilestone(input) is string error) return BadRequest(new { message = error });
        item.Title = input.Title.Trim(); item.Notes = input.Notes?.Trim(); item.DueDateUtc = input.DueDateUtc.Date;
        item.Progress = input.Progress; item.CompletedAtUtc = input.Progress == 100 ? item.CompletedAtUtc ?? DateTime.UtcNow : null; item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(); return NoContent();
    }
    [HttpDelete("milestones/{id:int}")]
    public async Task<IActionResult> DeleteMilestone(int id)
    {
        var item = await db.ProjectMilestones.FirstOrDefaultAsync(m => m.Id == id && AccessibleProjects.Select(p => p.Id).Contains(m.ProjectId));
        if (item == null) return NotFound();
        db.ProjectMilestones.Remove(item); await db.SaveChangesAsync(); return NoContent();
    }
    [HttpGet("projects/{projectId:int}/documents")]
    public async Task<IActionResult> Documents(int projectId)
    {
        if (!await OwnsProject(projectId)) return NotFound();
        return Ok(await db.ProjectDocuments.AsNoTracking().Where(d => d.ProjectId == projectId).OrderByDescending(d => d.UploadedAtUtc)
            .Select(d => new DocumentDto { Id = d.Id, ProjectId = d.ProjectId, FileName = d.FileName, Size = d.Size, UploadedBy = d.UploadedBy, UploadedAtUtc = d.UploadedAtUtc, CanDelete = IsAdmin || (d.UploadedByUserId != null && d.UploadedByUserId == UserId) }).ToListAsync());
    }
    [HttpPost("projects/{projectId:int}/documents"), RequestSizeLimit(11 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int projectId, IFormFile file)
    {
        if (!await OwnsProject(projectId)) return NotFound();
        if (file == null || file.Length <= 0 || file.Length > DocumentValidation.MaximumBytes) return BadRequest(new { message = "Choose a nonempty file up to 10 MB." });
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl)) return BadRequest(new { message = "Invalid file name (maximum 200 characters)." });
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        var type = DocumentValidation.ContentType(name, bytes);
        if (type == null) return BadRequest(new { message = "Use a valid PDF, PNG, JPG, UTF-8 TXT or CSV file. Maximum size: 10 MB." });
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var used = await db.ProjectDocuments.Where(d => d.Project.CompanyId == CompanyId).SumAsync(d => (long?)d.Size) ?? 0;
            if (used + bytes.Length > 100L * 1024 * 1024) return BadRequest(new { message = "Company document storage limit of 100 MB reached. Remove unused documents first." });
            var document = new ProjectDocument { ProjectId = projectId, FileName = name, ContentType = type, Content = bytes, Size = bytes.Length, UploadedByUserId = UserId, UploadedBy = User.FindFirstValue(ClaimTypes.Email) ?? "Company admin" };
            db.ProjectDocuments.Add(document); await db.SaveChangesAsync(); await transaction.CommitAsync();
            return StatusCode(201, new { document.Id });
        });
    }
    [HttpGet("documents/{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        var item = await db.ProjectDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && AccessibleProjects.Select(p => p.Id).Contains(d.ProjectId));
        if (item == null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(item.Content, item.ContentType, item.FileName);
    }
    [HttpDelete("documents/{id:int}")]
    public async Task<IActionResult> DeleteDocument(int id)
    {
        var item = await db.ProjectDocuments.FirstOrDefaultAsync(d => d.Id == id && AccessibleProjects.Select(p => p.Id).Contains(d.ProjectId));
        if (item == null) return NotFound();
        if (!IsAdmin && (string.IsNullOrEmpty(item.UploadedByUserId) || item.UploadedByUserId != UserId))
            return StatusCode(403, new { message = "You can only delete documents you uploaded. Contact your company admin for other files." });
        db.ProjectDocuments.Remove(item); await db.SaveChangesAsync(); return NoContent();
    }
    [HttpGet("activity")]
    public async Task<IActionResult> Activity(int page = 1, string? category = null)
    {
        if (page < 1 || page > 10000) return BadRequest();
        var query = db.WorkspaceActivities.AsNoTracking().Where(a => a.CompanyId == CompanyId);
        if (!IsAdmin) query = query.Where(a => a.Category != "Payments" && a.Actor == (User.FindFirstValue(ClaimTypes.Email) ?? ""));
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(a => a.Category == category);
        var rows = await query.OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id).Skip((page - 1) * 25).Take(26)
            .Select(a => new ActivityDto { Id = a.Id, Category = a.Category, Description = a.Description, Actor = a.Actor, OccurredAtUtc = a.OccurredAtUtc }).ToListAsync();
        return Ok(new ActivityPage { Page = page, HasMore = rows.Count > 25, Items = rows.Take(25).ToList() });
    }
    private async Task<WorkspaceReportDto> ReportData()
    {
        var projects = await AccessibleProjects.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        var milestones = await db.ProjectMilestones.AsNoTracking().Where(m => AccessibleProjects.Select(p => p.Id).Contains(m.ProjectId)).ToListAsync();
        var assignments = await db.ProjectAssignments.Where(a => AccessibleProjects.Select(p => p.Id).Contains(a.ProjectId)).GroupBy(a => a.ProjectId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);
        var documents = await db.ProjectDocuments.Where(d => AccessibleProjects.Select(p => p.Id).Contains(d.ProjectId)).GroupBy(d => d.ProjectId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);
        var tasks = await db.ProjectTasks.AsNoTracking().Where(t => t.CompanyId == CompanyId && AccessibleProjects.Select(p => p.Id).Contains(t.ProjectId)).ToListAsync();
        var members = await (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id where u.CompanyId == CompanyId && (IsAdmin || db.ProjectAssignments.Any(a => a.UserId == u.Id && AccessibleProjects.Select(p => p.Id).Contains(a.ProjectId))) && (r.Name == "SiteEngineer" || r.Name == "ProjectManager") select new { u.Id, u.IsActive }).Distinct().ToListAsync();
        return new WorkspaceReportDto
        {
            ActiveMembers = members.Count(u => u.IsActive), InactiveMembers = members.Count(u => !u.IsActive),
            Projects = projects.Select(p =>
            {
                var items = milestones.Where(m => m.ProjectId == p.Id).ToList();
                return new ProjectReportDto { Id = p.Id, Name = p.Name, Code = p.ProjectCode, Status = p.Status.ToString(), Currency = p.CurrencyCode, Budget = p.Budget,
                    Start = p.PlannedStartDateUtc, End = p.PlannedEndDateUtc, Milestones = items.Count, CompletedMilestones = items.Count(m => m.Progress == 100),
                    OverdueMilestones = p.Status is ConstructionSaaS.Domain.Enums.ProjectStatus.Cancelled or ConstructionSaaS.Domain.Enums.ProjectStatus.Completed ? 0 : items.Count(m => m.Progress < 100 && m.DueDateUtc.Date < DateTime.UtcNow.Date),
                    Progress = items.Count == 0 ? null : (int)Math.Round(items.Average(m => m.Progress)), Tasks = tasks.Count(t => t.ProjectId == p.Id), CompletedTasks = tasks.Count(t => t.ProjectId == p.Id && t.Status == "Completed"), OverdueTasks = tasks.Count(t => t.ProjectId == p.Id && t.Status != "Completed" && t.DueDateUtc.HasValue && t.DueDateUtc.Value.Date < DateTime.UtcNow.Date), Assignments = assignments.GetValueOrDefault(p.Id), Documents = documents.GetValueOrDefault(p.Id) };
            }).ToList()
        };
    }
    [HttpGet("reports")]
    public async Task<IActionResult> Reports() => Ok(await ReportData());
    [HttpGet("reports/export")]
    public async Task<IActionResult> Export()
    {
        var report = await ReportData();
        static string Cell(string? value)
        {
            value ??= "";
            if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Project,Code,Status,Currency,Budget,Start,Delivery,Progress,Milestones,Overdue milestones,Assigned members,Documents\r\n");
        foreach (var p in report.Projects)
            csv.AppendLine(string.Join(",", new[] { p.Name, p.Code, p.Status, p.Currency, p.Budget?.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Start?.ToString("yyyy-MM-dd"), p.End?.ToString("yyyy-MM-dd"), p.Progress?.ToString(), p.Milestones.ToString(), p.OverdueMilestones.ToString(), p.Assignments.ToString(), p.Documents.ToString() }.Select(Cell)));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", $"project-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
