
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ConstructionSaaS.Domain.Enums;
using ConstructionSaaS.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.Web.Controllers;

public class ProjectsController : Controller
{
    private readonly IConfiguration _configuration;
    private string ApiBaseUrl => ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(_configuration) + "/Projects";

    private readonly IHttpClientFactory _httpClientFactory;

    public ProjectsController(
        IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    private HttpClient CreateAuthorizedClient(string token)
    {
        var client = _httpClientFactory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private IActionResult RedirectToLogin()
    {
        return RedirectToAction("Login", "Account");
    }

    private IActionResult HandleUnauthorized()
    {
        HttpContext.Session.Remove("JwtToken");
        return RedirectToLogin();
    }

    // ==========================================
    // GET ALL PROJECTS
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var token = HttpContext.Session.GetString("JwtToken");

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToLogin();

        var projects = new List<DashboardProjectViewModel>();

        try
        {
            var client = CreateAuthorizedClient(token);

            var response = await client.GetAsync(ApiBaseUrl);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(403);

            if (!response.IsSuccessStatusCode)
            {
                ViewBag.ErrorMessage =
                    "Unable to load projects.";

                return View(projects);
            }

            projects = await response.Content
                .ReadFromJsonAsync<List<DashboardProjectViewModel>>()
                ?? new List<DashboardProjectViewModel>();
            try
            {
                using var reportResponse = await client.GetAsync(ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(_configuration) + "/workspace/reports");
                if (reportResponse.IsSuccessStatusCode)
                {
                    var report = await reportResponse.Content.ReadFromJsonAsync<ConstructionSaaS.Application.DTOs.Workspace.WorkspaceReportDto>();
                    foreach (var project in projects)
                        project.Progress = report?.Projects.FirstOrDefault(p => p.Id == project.Id)?.Progress;
                }
                else ViewBag.ProgressError = "Progress is temporarily unavailable. Project details are still available.";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                ViewBag.ProgressError = "Progress is temporarily unavailable. Project details are still available.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ViewBag.ErrorMessage =
                "Cannot connect to API server.";
        }
        catch (JsonException)
        {
            ViewBag.ErrorMessage =
                "Invalid projects response from API.";
        }

        return View(projects);
    }

    // ==========================================
    // CREATE PROJECT - GET
    // ==========================================

    [HttpGet]
    public IActionResult Create()
    {
        var token = HttpContext.Session.GetString("JwtToken");

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToLogin();

        return View(new CreateProjectViewModel());
    }

    // ==========================================
    // CREATE PROJECT - POST
    // ==========================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateProjectViewModel model)
    {
        var token = HttpContext.Session.GetString("JwtToken");

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToLogin();

        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var client = CreateAuthorizedClient(token);

            var payload = new
            {
                model.ProjectCode,
                model.Name,
                model.Description,
                model.AddressLine1,
                model.City,
                model.StateOrProvince,
                model.PostalCode,
                model.Country,
                model.Budget,

                CurrencyCode =
                    model.CurrencyCode.Trim().ToUpperInvariant(),

                PlannedStartDateUtc =
                    model.PlannedStartDateUtc.HasValue
                        ? DateTime.SpecifyKind(
                            model.PlannedStartDateUtc.Value.Date,
                            DateTimeKind.Utc)
                        : (DateTime?)null,

                PlannedEndDateUtc =
                    model.PlannedEndDateUtc.HasValue
                        ? DateTime.SpecifyKind(
                            model.PlannedEndDateUtc.Value.Date,
                            DateTimeKind.Utc)
                        : (DateTime?)null
            };

            var response = await client.PostAsJsonAsync(
                ApiBaseUrl,
                payload);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "You don't have permission to create projects.");

                return View(model);
            }

            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Project could not be created. Check the details and try again.");

                return View(model);
            }

            TempData["SuccessMessage"] =
                "Project created successfully!";

            return RedirectToAction(nameof(Index));
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Cannot connect to the API server.");

            return View(model);
        }
    }

    // ==========================================
    // EDIT PROJECT - GET
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var token = HttpContext.Session.GetString("JwtToken");

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToLogin();

        if (id <= 0)
            return BadRequest();

        try
        {
            var client = CreateAuthorizedClient(token);

            var response = await client.GetAsync(
                $"{ApiBaseUrl}/{id}");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(403);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotFound();

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(
                    502,
                    "Unable to load project.");
            }

            var project = await response.Content
                .ReadFromJsonAsync<EditProjectViewModel>();

            if (project == null)
                return NotFound();

            project.Id = id;

            // Status is loaded from the API response.
            // Example: "Planning", "Active", "Completed".
            // The Razor dropdown selects the matching value.

            return View(project);
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                503,
                "API connection failed.");
        }
        catch (JsonException)
        {
            return StatusCode(
                502,
                "Invalid project response from API.");
        }
    }

    // ==========================================
    // EDIT PROJECT - POST
    // ==========================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        EditProjectViewModel model)
    {
        var token = HttpContext.Session.GetString("JwtToken");

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToLogin();

        if (id != model.Id || id <= 0)
            return BadRequest();

        if (!ModelState.IsValid)
            return View(model);

        // Convert string status into ProjectStatus enum.
        if (!Enum.TryParse<ProjectStatus>(
                model.Status,
                ignoreCase: false,
                out var projectStatus) ||
            !Enum.IsDefined(typeof(ProjectStatus), projectStatus))
        {
            ModelState.AddModelError(
                nameof(model.Status),
                "Please select a valid project status.");

            return View(model);
        }

        try
        {
            var client = CreateAuthorizedClient(token);

            var payload = new
            {
                model.ProjectCode,
                model.Name,
                model.Description,
                model.AddressLine1,
                model.City,
                model.StateOrProvince,
                model.PostalCode,
                model.Country,
                model.Budget,

                CurrencyCode =
                    model.CurrencyCode.Trim().ToUpperInvariant(),

                PlannedStartDateUtc =
                    model.PlannedStartDateUtc.HasValue
                        ? DateTime.SpecifyKind(
                            model.PlannedStartDateUtc.Value.Date,
                            DateTimeKind.Utc)
                        : (DateTime?)null,

                PlannedEndDateUtc =
                    model.PlannedEndDateUtc.HasValue
                        ? DateTime.SpecifyKind(
                            model.PlannedEndDateUtc.Value.Date,
                            DateTimeKind.Utc)
                        : (DateTime?)null,

                // Send numeric enum value to API.
                Status = (int)projectStatus
            };

            var response = await client.PutAsJsonAsync(
                $"{ApiBaseUrl}/{id}",
                payload);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "You don't have permission to edit projects.");

                return View(model);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotFound();

            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Project update failed. Check the details.");

                return View(model);
            }

            TempData["SuccessMessage"] =
                "Project updated successfully!";

            return RedirectToAction(nameof(Index));
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Cannot connect to the API server.");

            return View(model);
        }
    }

    // ==========================================
    // DELETE PROJECT
    // ==========================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var token = HttpContext.Session.GetString("JwtToken");

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToLogin();

        if (id <= 0)
            return BadRequest();

        try
        {
            var client = CreateAuthorizedClient(token);

            var response = await client.DeleteAsync(
                $"{ApiBaseUrl}/{id}");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                TempData["ErrorMessage"] =
                    "Only Company Admin can delete projects.";

                return RedirectToAction(nameof(Index));
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                TempData["ErrorMessage"] =
                    "Project not found.";

                return RedirectToAction(nameof(Index));
            }

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] =
                    response.StatusCode == HttpStatusCode.Conflict
                        ? "This project has invoices. Keep it to preserve financial records."
                        : "Unable to delete project.";

                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] =
                "Project deleted successfully!";

            return RedirectToAction(nameof(Index));
        }
        catch (HttpRequestException)
        {
            TempData["ErrorMessage"] =
                "Cannot connect to API server.";

            return RedirectToAction(nameof(Index));
        }
    }
}
