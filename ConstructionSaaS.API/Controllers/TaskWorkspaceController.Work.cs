using System.Security.Claims;
using ConstructionSaaS.Application.DTOs.ProjectTasks;
using ConstructionSaaS.Application.DTOs.Workspace;
using ConstructionSaaS.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Controllers;
public partial class TaskWorkspaceController
{
    [HttpPost("work"), RequestSizeLimit(11 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    public async Task<IActionResult> AddWork([FromForm] WorkEntryInput input, IFormFile? file)
    {
        if (!await Projects.ContainsAsync(input.ProjectId)) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Summary)) return BadRequest(new { message = "Enter a work summary." });
        if (input.TaskId.HasValue) {
            var task = await Tasks.FirstOrDefaultAsync(t => t.Id == input.TaskId && t.ProjectId == input.ProjectId);
            if (task == null) return NotFound();
            if (!Manager && task.Status is "Completed" or "PendingReview") return BadRequest(new { message = "Submitted work is locked. Ask your manager to reopen it before adding more evidence." });
        } else if (!input.ReportDate.HasValue || input.ReportDate.Value.Date > DateTime.UtcNow.Date || input.ReportDate.Value.Year < 2000)
            return BadRequest(new { message = "Select a report date from 2000 through today (UTC)." });
        var entry = new SiteWorkEntry { ProjectId = input.ProjectId, TaskId = input.TaskId, AuthorUserId = UserId, AuthorEmail = User.FindFirstValue(ClaimTypes.Email) ?? "",
            Kind = input.TaskId.HasValue ? "Work note" : "Daily report", Summary = input.Summary.Trim(), Blockers = input.Blockers?.Trim(), ReportDate = input.TaskId.HasValue ? null : input.ReportDate!.Value.Date };
        if (file != null) {
            if (file.Length <= 0 || file.Length > DocumentValidation.MaximumBytes) return BadRequest(new { message = "Evidence must be a nonempty file up to 10 MB." });
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            if (name.Length == 0 || name.Length > 200 || name.Any(char.IsControl)) return BadRequest(new { message = "Invalid evidence file name." });
            using var buffer = new MemoryStream(); await file.CopyToAsync(buffer);
            var bytes = buffer.ToArray(); var type = DocumentValidation.ContentType(name, bytes);
            if (type is not ("application/pdf" or "image/png" or "image/jpeg")) return BadRequest(new { message = "Attach a valid PDF, PNG or JPG evidence file." });
            entry.FileName = name; entry.ContentType = type; entry.Content = bytes;
        }
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () => {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            if (!await Projects.ContainsAsync(input.ProjectId)) return NotFound();
            if (input.TaskId.HasValue) {
                var current = await Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == input.TaskId && t.ProjectId == input.ProjectId);
                if (current == null) return NotFound();
                if (!Manager && current.Status is "Completed" or "PendingReview")
                    return BadRequest(new { message = "Work has been submitted. Refresh before adding evidence." });
            }
            if (entry.ReportDate.HasValue && await db.SiteWorkEntries.AnyAsync(e => e.ProjectId == input.ProjectId && e.TaskId == null && e.AuthorUserId == UserId && e.ReportDate == entry.ReportDate))
                return Conflict(new { message = "You already submitted a report for this project and date." });
            var used = await db.SiteWorkEntries.Where(e => e.Project.CompanyId == CompanyId).SumAsync(e => (long?)(e.Content == null ? 0 : e.Content.Length)) ?? 0;
            if (used + (entry.Content?.LongLength ?? 0) > 100L * 1024 * 1024) return BadRequest(new { message = "Company evidence storage limit of 100 MB reached." });
            db.SiteWorkEntries.Add(entry); await db.SaveChangesAsync(); await transaction.CommitAsync(); return StatusCode(201, new { entry.Id });
        });
    }
    [HttpGet("work/{id:int}/evidence")]
    public async Task<IActionResult> Evidence(int id)
    {
        var entry = await db.SiteWorkEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id && Projects.Contains(e.ProjectId) && (Manager || (e.TaskId.HasValue ? Tasks.Select(t => t.Id).Contains(e.TaskId.Value) : e.AuthorUserId == UserId)));
        if (entry?.Content == null) return NotFound();
        Response.Headers.CacheControl = "no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(entry.Content, entry.ContentType!, entry.FileName);
    }
}
