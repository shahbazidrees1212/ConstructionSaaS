
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionSaaS.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.Web.Controllers;

public class ProjectAssignmentsController : Controller
{
    private readonly IConfiguration _configuration;
    private string ApiBaseUrl => ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(_configuration) + "";

    private readonly IHttpClientFactory _httpClientFactory;

    public ProjectAssignmentsController(
        IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    private string? GetToken()
    {
        return HttpContext.Session.GetString("JwtToken");
    }

    private HttpClient CreateAuthorizedClient(string token)
    {
        var client = _httpClientFactory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private IActionResult HandleUnauthorized()
    {
        HttpContext.Session.Remove("JwtToken");

        return RedirectToAction("Login", "Account");
    }

    // LOAD PROJECTS, MEMBERS AND ASSIGNMENTS
    private async Task LoadPageDataAsync(
        HttpClient client,
        ProjectAssignmentsViewModel model)
    {
        var projectsResponse = await client.GetAsync(
            $"{ApiBaseUrl}/Projects");

        var membersResponse = await client.GetAsync(
            $"{ApiBaseUrl}/Auth/team-members");

        if (projectsResponse.StatusCode == HttpStatusCode.Unauthorized ||
            membersResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException();
        }

        if (projectsResponse.StatusCode == HttpStatusCode.Forbidden ||
            membersResponse.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new System.Security.SecurityException();
        }

        projectsResponse.EnsureSuccessStatusCode();
        membersResponse.EnsureSuccessStatusCode();

        model.Projects = await projectsResponse.Content
            .ReadFromJsonAsync<List<ProjectOptionViewModel>>()
            ?? new();

        var members = await membersResponse.Content
            .ReadFromJsonAsync<List<TeamMemberViewModel>>()
            ?? new();

        model.Members = members
            .Where(x => x.IsActive &&
                (x.Role == "ProjectManager" ||
                 x.Role == "SiteEngineer"))
            .ToList();

        if (model.ProjectId <= 0 && model.Projects.Count > 0)
        {
            model.ProjectId = model.Projects[0].Id;
        }

        if (model.ProjectId > 0)
        {
            // Never request assignments for a project outside
            // the projects returned for the current company.
            if (!model.Projects.Any(x => x.Id == model.ProjectId))
                return;

            var response = await client.GetAsync(
                $"{ApiBaseUrl}/ProjectAssignments/project/{model.ProjectId}");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException();

            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new System.Security.SecurityException();

            response.EnsureSuccessStatusCode();

            model.Assignments = await response.Content
                .ReadFromJsonAsync<List<ProjectAssignmentItemViewModel>>()
                ?? new();
        }
    }

    // INDEX - GET
    [HttpGet]
    public async Task<IActionResult> Index(int? projectId)
    {
        var token = GetToken();

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction("Login", "Account");

        var model = new ProjectAssignmentsViewModel
        {
            ProjectId = projectId ?? 0
        };

        try
        {
            var client = CreateAuthorizedClient(token);

            await LoadPageDataAsync(client, model);

            if (projectId.HasValue &&
                !model.Projects.Any(p => p.Id == projectId.Value))
            {
                return NotFound();
            }
        }
        catch (UnauthorizedAccessException)
        {
            return HandleUnauthorized();
        }
        catch (System.Security.SecurityException)
        {
            return StatusCode(403);
        }
        catch (HttpRequestException)
        {
            ViewBag.ErrorMessage =
                "Unable to load project assignments. Check API connection.";
        }
        catch (JsonException)
        {
            ViewBag.ErrorMessage = "Invalid API response.";
        }

        return View(model);
    }

    // ASSIGN - POST
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        ProjectAssignmentsViewModel model)
    {
        var token = GetToken();

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction("Login", "Account");

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] =
                "Please select a valid project and employee.";

            return RedirectToAction(nameof(Index),
                new { projectId = model.ProjectId });
        }

        try
        {
            var client = CreateAuthorizedClient(token);

            var payload = new
            {
                model.ProjectId,
                model.UserId
            };

            var response = await client.PostAsJsonAsync(
                $"{ApiBaseUrl}/ProjectAssignments",
                payload);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(403);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                TempData["ErrorMessage"] =
                    "This employee is already assigned to the project.";
            }
            else if (response.IsSuccessStatusCode)
            {
                TempData["SuccessMessage"] =
                    "Employee assigned successfully.";
            }
            else
            {
                TempData["ErrorMessage"] =
                    "Unable to assign employee. Check project and employee details.";
            }
        }
        catch (HttpRequestException)
        {
            TempData["ErrorMessage"] =
                "Cannot connect to API server.";
        }

        return RedirectToAction(nameof(Index),
            new { projectId = model.ProjectId });
    }

    // REMOVE - POST
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(
        int id,
        int projectId)
    {
        var token = GetToken();

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction("Login", "Account");

        if (id <= 0 || projectId <= 0)
            return BadRequest();

        try
        {
            var client = CreateAuthorizedClient(token);

            var response = await client.DeleteAsync(
                $"{ApiBaseUrl}/ProjectAssignments/{id}");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(403);

            if (response.IsSuccessStatusCode)
            {
                TempData["SuccessMessage"] =
                    "Assignment removed successfully.";
            }
            else
            {
                TempData["ErrorMessage"] =
                    "Unable to remove assignment.";
            }
        }
        catch (HttpRequestException)
        {
            TempData["ErrorMessage"] =
                "Cannot connect to API server.";
        }

        return RedirectToAction(nameof(Index),
            new { projectId });
    }
}
