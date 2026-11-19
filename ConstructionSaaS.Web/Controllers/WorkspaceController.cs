using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ConstructionSaaS.Application.DTOs.Workspace;
using ConstructionSaaS.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ConstructionSaaS.Web.Controllers;
public class WorkspaceController(IHttpClientFactory factory, IConfiguration configuration) : Controller
{
    protected virtual string WorkspaceRole => "CompanyAdmin";
    private string Api => ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(configuration);
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", HttpContext.Session.GetString("JwtToken"));
        return client;
    }
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        var handler = new JwtSecurityTokenHandler();
        try
        {
            if (string.IsNullOrWhiteSpace(token) || !handler.CanReadToken(token) || handler.ReadJwtToken(token).ValidTo <= DateTime.UtcNow)
            { context.Result = Login(); return; }
            if (!handler.ReadJwtToken(token).Claims.Any(c => (c.Type == ClaimTypes.Role || c.Type == "role") && c.Value == WorkspaceRole))
            { context.Result = StatusCode(403); return; }
        }
        catch (ArgumentException) { context.Result = Login(); return; }
        ViewData["IsManager"] = WorkspaceRole != "CompanyAdmin";
        var result = await next();
        if (result.Exception is ApiFailure failure)
        {
            result.ExceptionHandled = true;
            result.Result = failure.Status == HttpStatusCode.Unauthorized ? Login() : failure.Status == HttpStatusCode.Forbidden ? StatusCode(403) : failure.Status == HttpStatusCode.NotFound ? NotFound() : View("~/Views/Workspace/WorkspaceError.cshtml");
        }
        else if (result.Exception is HttpRequestException or TaskCanceledException or JsonException)
        { result.ExceptionHandled = true; result.Result = View("~/Views/Workspace/WorkspaceError.cshtml"); }
    }
    private IActionResult Login() { if (TempData["SuccessMessage"] is string message) TempData["LoginMessage"] = message; HttpContext.Session.Clear(); return RedirectToAction("Login", "Account"); }
    private async Task<T> Get<T>(string route) where T : new()
    {
        using var client = Client(); using var response = await client.GetAsync(Api + route);
        if (!response.IsSuccessStatusCode) throw new ApiFailure(response.StatusCode);
        return await response.Content.ReadFromJsonAsync<T>() ?? new T();
    }
    private async Task<AdminWorkspaceViewModel> Overview(int? projectId)
    {
        var model = new AdminWorkspaceViewModel { Report = await Get<WorkspaceReportDto>("/workspace/reports") };
        model.ProjectId = projectId ?? model.Report.Projects.FirstOrDefault()?.Id ?? 0;
        if (projectId.HasValue && model.SelectedProject == null) throw new ApiFailure(HttpStatusCode.NotFound);
        return model;
    }
    [HttpGet] public async Task<IActionResult> Schedule(int? projectId)
    {
        var model = await Overview(projectId);
        if (model.ProjectId > 0) model.Milestones = await Get<List<MilestoneDto>>($"/workspace/projects/{model.ProjectId}/milestones");
        return View("~/Views/Workspace/" + ControllerContext.ActionDescriptor.ActionName + ".cshtml", model);
    }
    [HttpGet] public async Task<IActionResult> Documents(int? projectId)
    {
        var model = await Overview(projectId);
        if (model.ProjectId > 0) model.Documents = await Get<List<DocumentDto>>($"/workspace/projects/{model.ProjectId}/documents");
        return View("~/Views/Workspace/" + ControllerContext.ActionDescriptor.ActionName + ".cshtml", model);
    }
    [HttpGet] public async Task<IActionResult> Activity(int page = 1, string? category = null) =>
        View("~/Views/Workspace/Activity.cshtml", new AdminWorkspaceViewModel { Category = category, Activity = await Get<ActivityPage>($"/workspace/activity?page={page}&category={Uri.EscapeDataString(category ?? "")}") });
    [HttpGet] public async Task<IActionResult> Reports() => View("~/Views/Workspace/Reports.cshtml", await Overview(null));
    [HttpGet] public async Task<IActionResult> Settings() => View("~/Views/Workspace/Settings.cshtml", new AdminWorkspaceViewModel { Account = await Get<AccountDto>("/account") });
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProfile(ProfileInput input)
    {
        if (!ModelState.IsValid)
        {
            var account = await Get<AccountDto>("/account");
            account.FullName = input.FullName; account.JobTitle = input.JobTitle; account.Phone = input.Phone;
            account.Location = input.Location; account.Bio = input.Bio;
            return View("~/Views/Workspace/Settings.cshtml", new AdminWorkspaceViewModel { Account = account });
        }
        await Send(HttpMethod.Post, "/account/profile", JsonContent.Create(input), "Profile updated.");
        return RedirectToAction(nameof(Settings));
    }
    [HttpGet] public Task<IActionResult> ProfilePhoto() => DownloadFile("/account/photo", "profile-photo");
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(3 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
    public async Task<IActionResult> UploadProfilePhoto(IFormFile file)
    {
        if (file == null || file.Length == 0 || file.Length > 2 * 1024 * 1024) TempData["ErrorMessage"] = "Choose a JPG or PNG photo up to 2 MB.";
        else
        {
            using var form = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream();
            form.Add(new StreamContent(stream), "file", Path.GetFileName(file.FileName.Replace('\\', '/')));
            await Send(HttpMethod.Post, "/account/photo", form, "Profile photo updated.");
        }
        return RedirectToAction(nameof(Settings));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveProfilePhoto()
    {
        await Send(HttpMethod.Delete, "/account/photo", null, "Profile photo removed.");
        return RedirectToAction(nameof(Settings));
    }
    private async Task<bool> Send(HttpMethod method, string route, HttpContent? content, string success, bool showForbiddenMessage = false)
    {
        using var client = Client(); using var request = new HttpRequestMessage(method, Api + route) { Content = content };
        using var response = await client.SendAsync(request);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound || (response.StatusCode == HttpStatusCode.Forbidden && !showForbiddenMessage)) throw new ApiFailure(response.StatusCode);
        if (response.IsSuccessStatusCode) { TempData["SuccessMessage"] = success; return true; }
        var message = "Could not save changes. Please check the form and try again.";
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (body.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String) message = text.GetString()!;
            else if (body.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                message = string.Join(" ", errors.EnumerateObject().SelectMany(p => p.Value.EnumerateArray()).Select(v => v.GetString()));
        }
        catch (JsonException) { }
        TempData["ErrorMessage"] = message; return false;
    }
    private bool ValidForm()
    {
        if (ModelState.IsValid) return true;
        TempData["ErrorMessage"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)); return false;
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMilestone(int projectId, int? id, MilestoneInput input)
    {
        if (ValidForm()) await Send(id.HasValue ? HttpMethod.Put : HttpMethod.Post, id.HasValue ? $"/workspace/milestones/{id}" : $"/workspace/projects/{projectId}/milestones", JsonContent.Create(input), "Milestone saved.");
        return RedirectToAction(nameof(Schedule), new { projectId });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMilestone(int id, int projectId)
    {
        await Send(HttpMethod.Delete, $"/workspace/milestones/{id}", null, "Milestone removed.");
        return RedirectToAction(nameof(Schedule), new { projectId });
    }
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(11 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int projectId, IFormFile file)
    {
        if (file == null || file.Length == 0 || file.Length > DocumentValidation.MaximumBytes) TempData["ErrorMessage"] = "Choose a nonempty file up to 10 MB.";
        else
        {
            using var multipart = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream();
            multipart.Add(new StreamContent(stream), "file", Path.GetFileName(file.FileName.Replace('\\', '/')));
            await Send(HttpMethod.Post, $"/workspace/projects/{projectId}/documents", multipart, "Document uploaded.");
        }
        return RedirectToAction(nameof(Documents), new { projectId });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDocument(int id, int projectId)
    {
        await Send(HttpMethod.Delete, $"/workspace/documents/{id}", null, "Document removed.", showForbiddenMessage: true);
        return RedirectToAction(nameof(Documents), new { projectId });
    }
    [HttpGet] public Task<IActionResult> Download(int id) => DownloadFile($"/workspace/documents/{id}/download", "document");
    [HttpGet] public Task<IActionResult> ExportReport() => DownloadFile("/workspace/reports/export", "project-report.csv");
    private async Task<IActionResult> DownloadFile(string route, string fallback)
    {
        using var client = Client(); using var response = await client.GetAsync(Api + route);
        if (!response.IsSuccessStatusCode) throw new ApiFailure(response.StatusCode);
        Response.Headers["X-Content-Type-Options"] = "nosniff"; Response.Headers.CacheControl = "no-store";
        return File(await response.Content.ReadAsByteArrayAsync(), response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? fallback);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeEmail(ChangeEmailInput input)
    {
        if (ValidForm() && await Send(HttpMethod.Post, "/account/email", JsonContent.Create(input), "Email updated. Please sign in again.")) return Login();
        return RedirectToAction(nameof(Settings));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordInput input)
    {
        if (ValidForm() && await Send(HttpMethod.Post, "/account/password", JsonContent.Create(input), "Password updated. Please sign in again.")) return Login();
        return RedirectToAction(nameof(Settings));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutAll()
    {
        if (await Send(HttpMethod.Post, "/account/sign-out-all", null, "All sessions signed out.")) return Login();
        return RedirectToAction(nameof(Settings));
    }
    private sealed class ApiFailure(HttpStatusCode status) : Exception { public HttpStatusCode Status { get; } = status; }
}

