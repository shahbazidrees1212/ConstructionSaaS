
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using ConstructionSaaS.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace ConstructionSaaS.Web.Controllers
{
    public class DashboardController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;

        private readonly IConfiguration _configuration;
    private string ApiBaseUrl => ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(_configuration) + "";

        public DashboardController(
            IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        }

        public async Task<IActionResult> Index()
        {
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
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is SecurityTokenException)
            {
                HttpContext.Session.Remove("JwtToken");
                return RedirectToAction("Login", "Account");
            }

            if (jwt.ValidTo <= DateTime.UtcNow)
            {
                HttpContext.Session.Remove("JwtToken");
                return RedirectToAction("Login", "Account");
            }

            var roles = jwt.Claims
                .Where(c =>
                    c.Type == ClaimTypes.Role ||
                    c.Type == "role" ||
                    c.Type == "roles")
                .Select(c => c.Value)
                .ToList();

            if (!roles.Contains(
                "CompanyAdmin",
                StringComparer.OrdinalIgnoreCase))
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    "Access denied. CompanyAdmin role required.");
            }

            var model = new DashboardViewModel
            {
                AccountEmail = jwt.Claims.FirstOrDefault(c =>
                    c.Type == ClaimTypes.Email || c.Type == "email")?.Value ?? string.Empty
            };

            var client = _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            // PROJECTS
            try
            {
                var response = await client.GetAsync(
                    $"{ApiBaseUrl}/Projects");

                if (response.StatusCode ==
                    HttpStatusCode.Unauthorized)
                {
                    HttpContext.Session.Remove("JwtToken");

                    return RedirectToAction(
                        "Login",
                        "Account");
                }

                if (response.StatusCode ==
                    HttpStatusCode.Forbidden)
                {
                    ViewBag.ProjectsError =
                        "You do not have permission to view projects.";
                }
                else if (!response.IsSuccessStatusCode)
                {
                    ViewBag.ProjectsError =
                        "Unable to load projects. API returned " +
                        (int)response.StatusCode + ".";
                }
                else
                {
                    var projects = await response.Content
                        .ReadFromJsonAsync<
                            List<DashboardProjectViewModel>>();

                    model.Projects =
                        projects ??
                        new List<DashboardProjectViewModel>();

                    model.TotalProjects = model.Projects.Count;

                    model.ActiveProjects = model.Projects.Count(
                        p => string.Equals(
                            p.Status,
                            "Active",
                            StringComparison.OrdinalIgnoreCase));

                    model.CompletedProjects = model.Projects.Count(
                        p => string.Equals(
                            p.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (HttpRequestException)
            {
                ViewBag.ProjectsError =
                    "Cannot connect to the Projects API.";
            }
            catch (JsonException)
            {
                ViewBag.ProjectsError =
                    "Unexpected project data received from API.";
            }
            catch (TaskCanceledException)
            {
                ViewBag.ProjectsError =
                    "Projects API request timed out.";
            }

            // TEAM MEMBERS
            try
            {
                var response = await client.GetAsync(
                    $"{ApiBaseUrl}/Auth/team-members");

                if (response.StatusCode ==
                    HttpStatusCode.Unauthorized)
                {
                    HttpContext.Session.Remove("JwtToken");

                    return RedirectToAction(
                        "Login",
                        "Account");
                }

                if (response.StatusCode ==
                    HttpStatusCode.Forbidden)
                {
                    ViewBag.TeamError =
                        "You do not have permission to view team members.";
                }
                else if (!response.IsSuccessStatusCode)
                {
                    ViewBag.TeamError =
                        "Unable to load team members. API returned " +
                        (int)response.StatusCode + ".";
                }
                else
                {
                    var members = await response.Content
                        .ReadFromJsonAsync<
                            List<TeamMemberViewModel>>();

                    members ??= new List<TeamMemberViewModel>();

                    model.TotalTeamMembers = members.Count;

                    model.ActiveTeamMembers =
                        members.Count(m => m.IsActive);

                    model.InactiveTeamMembers =
                        members.Count(m => !m.IsActive);

                    model.TeamMembersLoaded = true;
                }
            }
            catch (HttpRequestException)
            {
                ViewBag.TeamError =
                    "Cannot connect to the Team Members API.";
            }
            catch (JsonException)
            {
                ViewBag.TeamError =
                    "Unexpected team member data received from API.";
            }
            catch (TaskCanceledException)
            {
                ViewBag.TeamError =
                    "Team Members API request timed out.";
            }

            try
            {
                using var reportResponse = await client.GetAsync($"{ApiBaseUrl}/workspace/reports");
                using var activityResponse = await client.GetAsync($"{ApiBaseUrl}/workspace/activity");
                if (reportResponse.StatusCode == HttpStatusCode.Unauthorized || activityResponse.StatusCode == HttpStatusCode.Unauthorized)
                {
                    HttpContext.Session.Clear();
                    return RedirectToAction("Login", "Account");
                }
                if (reportResponse.IsSuccessStatusCode)
                    model.WorkspaceReport = await reportResponse.Content.ReadFromJsonAsync<ConstructionSaaS.Application.DTOs.Workspace.WorkspaceReportDto>();
                else model.WorkspaceError = "Workspace analytics are unavailable right now.";
                if (activityResponse.IsSuccessStatusCode)
                    model.RecentActivity = (await activityResponse.Content.ReadFromJsonAsync<ConstructionSaaS.Application.DTOs.Workspace.ActivityPage>())?.Items.Take(5).ToList() ?? new();
                else model.WorkspaceError = "Some workspace updates could not be loaded.";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            { model.WorkspaceError = "Workspace updates are unavailable right now."; }
            return View(model);
        }
    }
}

