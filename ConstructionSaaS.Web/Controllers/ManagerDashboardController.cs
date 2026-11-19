
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;
using ConstructionSaaS.Application.DTOs.Projects;
using ConstructionSaaS.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.Web.Controllers;

public class ManagerDashboardController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ManagerDashboardController> _logger;

    public ManagerDashboardController(
        IHttpClientFactory httpClientFactory,
        ILogger<ManagerDashboardController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // 1. Read JWT from session
        var token = HttpContext.Session.GetString("JwtToken");

        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToAction("Login", "Account");
        }

        JwtSecurityToken jwt;

        try
        {
            var handler = new JwtSecurityTokenHandler();

            if (!handler.CanReadToken(token))
            {
                HttpContext.Session.Remove("JwtToken");
                return RedirectToAction("Login", "Account");
            }

            jwt = handler.ReadJwtToken(token);
        }
        catch (Exception)
        {
            HttpContext.Session.Remove("JwtToken");
            return RedirectToAction("Login", "Account");
        }

        // 2. Check expiration
        if (jwt.ValidTo <= DateTime.UtcNow)
        {
            HttpContext.Session.Remove("JwtToken");
            return RedirectToAction("Login", "Account");
        }

        // 3. Check role
        var isManager = jwt.Claims.Any(c =>
            (c.Type == ClaimTypes.Role ||
             c.Type == "role" ||
             c.Type == "roles") &&
            string.Equals(
                c.Value,
                "ProjectManager",
                StringComparison.OrdinalIgnoreCase));

        if (!isManager)
        {
            return ForbidManager();
        }

        // 4. Read company ID
        var companyIdClaim = jwt.Claims
            .FirstOrDefault(c => c.Type == "company_id")
            ?.Value;

        if (!int.TryParse(companyIdClaim, out var companyId) ||
            companyId <= 0)
        {
            return ForbidManager();
        }

        // 5. Prepare dashboard model
        var model = new ManagerDashboardViewModel
        {
            CompanyId = companyId,
            AccountEmail = jwt.Claims
                .FirstOrDefault(c =>
                    c.Type == ClaimTypes.Email ||
                    c.Type == "email")
                ?.Value ?? string.Empty
        };

        // Keep existing Razor view compatible
        ViewBag.CompanyId = model.CompanyId;
        ViewBag.UserEmail = model.AccountEmail;

        // 6. Call Projects API
        try
        {
            var client = _httpClientFactory.CreateClient();

            client.BaseAddress =
                new Uri(ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(HttpContext.RequestServices.GetRequiredService<IConfiguration>()) + "/");

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer", token);

            using var response = await client.GetAsync(
                "Projects",
                HttpContext.RequestAborted);

            // Invalid or expired JWT
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                HttpContext.Session.Remove("JwtToken");
                return RedirectToAction("Login", "Account");
            }

            // Role or company access rejected
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return ForbidManager();
            }

            if (!response.IsSuccessStatusCode)
            {
                model.ErrorMessage =
                    "Unable to load assigned projects.";

                return View(model);
            }

            var projects = await response.Content
                .ReadFromJsonAsync<List<ProjectDto>>(
                    cancellationToken: HttpContext.RequestAborted);

            model.Projects = projects ?? new List<ProjectDto>();

            model.TotalProjects = model.Projects.Count;

            model.ActiveProjects = model.Projects.Count(p =>
                string.Equals(
                    p.Status,
                    "Active",
                    StringComparison.OrdinalIgnoreCase));

            model.CompletedProjects = model.Projects.Count(p =>
                string.Equals(
                    p.Status,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase));

            model.PlanningProjects = model.Projects.Count(p =>
                string.Equals(
                    p.Status,
                    "Planning",
                    StringComparison.OrdinalIgnoreCase));

            model.ProjectsLoaded = true;
            using var reportResponse = await client.GetAsync("workspace/reports", HttpContext.RequestAborted);
            if (reportResponse.IsSuccessStatusCode)
                model.Report = await reportResponse.Content.ReadFromJsonAsync<ConstructionSaaS.Application.DTOs.Workspace.WorkspaceReportDto>(cancellationToken: HttpContext.RequestAborted);
            else
                model.ErrorMessage = "Project insights are temporarily unavailable. You can still open your assigned projects.";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "ProjectManager dashboard API request failed.");

            model.ErrorMessage =
                "Projects service is currently unavailable.";
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(
                ex,
                "Invalid projects API response.");

            model.ErrorMessage =
                "Unable to read project information.";
        }
        catch (TaskCanceledException)
            when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            model.ErrorMessage =
                "Projects service request timed out.";
        }

        return View(model);
    }

    private IActionResult ForbidManager()
    {
        return StatusCode(
            StatusCodes.Status403Forbidden,
            "Access denied.");
    }
}
