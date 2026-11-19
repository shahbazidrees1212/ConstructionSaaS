using ConstructionSaaS.Application.DTOs.ProjectTasks;
using ConstructionSaaS.Application.DTOs.Workspace;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
namespace ConstructionSaaS.Web.Controllers;
public partial class TaskBoardController
{
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(11 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    public async Task<IActionResult> Work(WorkEntryInput input, IFormFile? file)
    {
        if (file?.Length > DocumentValidation.MaximumBytes) ModelState.AddModelError("file", "Evidence must be at most 10 MB.");
        if (!ModelState.IsValid) TempData["ErrorMessage"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        else {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(input.ProjectId.ToString()), "ProjectId");
            if(input.TaskId.HasValue) content.Add(new StringContent(input.TaskId.Value.ToString()), "TaskId");
            content.Add(new StringContent(input.Summary), "Summary");
            content.Add(new StringContent(input.Blockers ?? ""), "Blockers");
            if(input.ReportDate.HasValue) content.Add(new StringContent(input.ReportDate.Value.ToString("yyyy-MM-dd")), "ReportDate");
            if(file != null) content.Add(new StreamContent(file.OpenReadStream()), "file", Path.GetFileName(file.FileName.Replace('\\','/')));
            using var client = Client(); using var response = await client.PostAsync(Api + "/task-workspace/work", content);
            if(response.IsSuccessStatusCode) TempData["SuccessMessage"] = "Work update saved. Your project managers have been notified.";
            else {
                var error = "Could not save this update. Check project access and form values.";
                try { var json = await response.Content.ReadFromJsonAsync<JsonElement>(); if(json.TryGetProperty("message",out var text)) error = text.GetString() ?? error; } catch(JsonException) { }
                TempData["ErrorMessage"] = error;
            }
        }
        return RedirectToAction(nameof(Index), new { projectId = input.ProjectId, tab = input.TaskId.HasValue ? "tasks" : "daily" });
    }
    [HttpGet] public async Task<IActionResult> Evidence(int id)
    {
        using var client = Client(); using var response = await client.GetAsync(Api + $"/task-workspace/work/{id}/evidence");
        if(!response.IsSuccessStatusCode) return Failure(response.StatusCode);
        Response.Headers.CacheControl = "no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(await response.Content.ReadAsByteArrayAsync(),response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "evidence");
    }
}
