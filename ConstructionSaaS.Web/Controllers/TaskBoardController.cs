using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using ConstructionSaaS.Application.DTOs.ProjectTasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ConstructionSaaS.Web.Controllers;
public partial class TaskBoardController(IHttpClientFactory factory, IConfiguration config) : Controller
{
    private string Api => ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(config);
    private bool Manager => ViewData["TaskManager"] is true;
    private HttpClient Client() { var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", HttpContext.Session.GetString("JwtToken")); return client; }
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        var handler = new JwtSecurityTokenHandler();
        try {
            if (string.IsNullOrWhiteSpace(token) || !handler.CanReadToken(token) || handler.ReadJwtToken(token).ValidTo <= DateTime.UtcNow) { context.Result = RedirectToAction("Login", "Account"); return; }
            var roles = handler.ReadJwtToken(token).Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role" || c.Type == "roles").Select(c => c.Value).ToList();
            if (!roles.Contains("ProjectManager") && !roles.Contains("SiteEngineer")) { context.Result = StatusCode(403); return; }
            ViewData["TaskManager"] = roles.Contains("ProjectManager");
            ViewData["IsManager"] = true;
        } catch (ArgumentException) { context.Result = RedirectToAction("Login", "Account"); return; }
        var result = await next();
        if (result.Exception is HttpRequestException or TaskCanceledException or JsonException) {
            result.ExceptionHandled = true; result.Result = View("~/Views/Workspace/WorkspaceError.cshtml");
        }
    }
    [HttpGet] public async Task<IActionResult> Index(int? projectId, string? status, string? q, string tab = "tasks")
    {
        using var client = Client(); using var response = await client.GetAsync(Api + "/task-workspace" + (projectId.HasValue ? $"?projectId={projectId}" : ""));
        if (!response.IsSuccessStatusCode) return Failure(response.StatusCode);
        var model = await response.Content.ReadFromJsonAsync<TaskWorkspaceDto>() ?? new();
        ViewData["ProjectId"] = projectId; ViewData["Status"] = status; ViewData["Query"] = q; ViewData["Tab"] = tab;
        return View(model);
    }
    private IActionResult Failure(HttpStatusCode code) => code == HttpStatusCode.Unauthorized ? RedirectToAction("Login", "Account") : StatusCode((int)code);
    private async Task<IActionResult> Send(HttpMethod method, string route, object? input, int? projectId, string message)
    {
        if (!ModelState.IsValid) { TempData["ErrorMessage"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)); return RedirectToAction(nameof(Index), new { projectId }); }
        using var client = Client(); using var request = new HttpRequestMessage(method, Api + route) { Content = input == null ? null : JsonContent.Create(input) };
        using var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode) TempData["SuccessMessage"] = message;
        else if (response.StatusCode == HttpStatusCode.Unauthorized) return Failure(response.StatusCode);
        else {
            var body = await response.Content.ReadAsStringAsync();
            var error = response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden ? "This task or project is no longer available to your account." : "Could not save changes. Review your inputs and refresh the page.";
            try { var json = JsonDocument.Parse(body); if (json.RootElement.ValueKind == JsonValueKind.String) error = json.RootElement.GetString() ?? error; else if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("message", out var text)) error = text.GetString() ?? error; } catch (JsonException) { if (response.StatusCode == HttpStatusCode.BadRequest && body.Length < 500) error = body; }
            TempData["ErrorMessage"] = error;
        }
        return RedirectToAction(nameof(Index), new { projectId });
    }
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Create(CreateProjectTaskDto input) => Manager ? Send(HttpMethod.Post, "/project-tasks", input, input.ProjectId, string.IsNullOrWhiteSpace(input.AssignedToUserId) ? "Task created. Assign an engineer when ready." : "Task created and assigned engineer notified.") : Task.FromResult<IActionResult>(StatusCode(403));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Edit(int id, int projectId, UpdateProjectTaskDto input) => Manager ? Send(HttpMethod.Put, $"/project-tasks/{id}", input, projectId, "Task updated.") : Task.FromResult<IActionResult>(StatusCode(403));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Delete(int id, int projectId) => Manager ? Send(HttpMethod.Delete, $"/project-tasks/{id}", null, projectId, "Task removed.") : Task.FromResult<IActionResult>(StatusCode(403));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Status(int id, int projectId, TaskStatusInput input) => Send(HttpMethod.Post, $"/task-workspace/tasks/{id}/status", input, projectId, "Task status updated.");
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Read(long id) { await Send(HttpMethod.Post, $"/task-workspace/notifications/{id}/read", null, null, "Notification marked as read."); return RedirectToAction(nameof(Index), new { tab = "notifications" }); }
    [HttpGet] public async Task<IActionResult> Export(int? projectId) {
        using var client = Client(); using var response = await client.GetAsync(Api + "/task-workspace/export" + (projectId.HasValue ? $"?projectId={projectId}" : ""));
        if (!response.IsSuccessStatusCode) return Failure(response.StatusCode);
        Response.Headers.CacheControl = "no-store";
        return File(await response.Content.ReadAsByteArrayAsync(), "text/csv", "task-progress.csv");
    }
}
