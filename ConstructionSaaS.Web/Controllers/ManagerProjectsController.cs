
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using ConstructionSaaS.Application.DTOs.Projects;
using ConstructionSaaS.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.Web.Controllers;

public class ManagerProjectsController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ManagerProjectsController(
        IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // STEP 1: Read JWT from session
        var token = HttpContext.Session.GetString("JwtToken");

        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToAction("Login", "Account");
        }

        // STEP 2: Read token and check expiry
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

        if (jwt.ValidTo <= DateTime.UtcNow)
        {
            HttpContext.Session.Remove("JwtToken");
            return RedirectToAction("Login", "Account");
        }

        // STEP 3: Check ProjectManager role
        var isManager = jwt.Claims.Any(c =>
            (c.Type == ClaimTypes.Role ||
             c.Type == "role" ||
             c.Type == "roles") &&
            c.Value == "ProjectManager");

        if (!isManager)
        {
            return StatusCode(403, "Access denied.");
        }

        // STEP 4: Prepare ViewModel
        var model = new ManagerProjectsViewModel
        {
            AccountEmail = jwt.Claims
                .FirstOrDefault(c =>
                    c.Type == ClaimTypes.Email ||
                    c.Type == "email")
                ?.Value ?? string.Empty
        };

        // STEP 5: Call secured Projects API
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

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                HttpContext.Session.Remove("JwtToken");
                return RedirectToAction("Login", "Account");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return StatusCode(403, "Access denied.");
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

            model.ProjectsLoaded = true;
        }
        catch (HttpRequestException)
        {
            model.ErrorMessage =
                "Projects API is currently unavailable.";
        }
        catch (System.Text.Json.JsonException)
        {
            model.ErrorMessage =
                "Unable to read project information.";
        }
        catch (TaskCanceledException)
            when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            model.ErrorMessage =
                "Projects API request timed out.";
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        // STEP 1: Validate project ID
        if (id <= 0)
        {
            return BadRequest("Invalid project ID.");
        }

        // STEP 2: Get JWT from session
        var token = HttpContext.Session.GetString("JwtToken");

        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToAction("Login", "Account");
        }

        // STEP 3: Read JWT and check ProjectManager role
        try
        {
            var handler = new JwtSecurityTokenHandler();

            if (!handler.CanReadToken(token))
            {
                HttpContext.Session.Remove("JwtToken");
                return RedirectToAction("Login", "Account");
            }

            var jwt = handler.ReadJwtToken(token);

            if (jwt.ValidTo <= DateTime.UtcNow)
            {
                HttpContext.Session.Remove("JwtToken");
                return RedirectToAction("Login", "Account");
            }

            var isManager = jwt.Claims.Any(c =>
                (c.Type == ClaimTypes.Role ||
                 c.Type == "role" ||
                 c.Type == "roles") &&
                c.Value == "ProjectManager");

            if (!isManager)
            {
                return StatusCode(403, "Access denied.");
            }
        }
        catch (Exception)
        {
            HttpContext.Session.Remove("JwtToken");
            return RedirectToAction("Login", "Account");
        }

        // STEP 4: Call secured API
        try
        {
            var client = _httpClientFactory.CreateClient();

            client.BaseAddress =
                new Uri(ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(HttpContext.RequestServices.GetRequiredService<IConfiguration>()) + "/");

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            using var response = await client.GetAsync(
                $"Projects/{id}",
                HttpContext.RequestAborted);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                HttpContext.Session.Remove("JwtToken");
                return RedirectToAction("Login", "Account");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return StatusCode(403, "Access denied.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return NotFound("Project not found or not assigned to you.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(502, "Unable to load project details.");
            }

            var project = await response.Content
                .ReadFromJsonAsync<ProjectDto>(
                    cancellationToken: HttpContext.RequestAborted);

            if (project == null)
            {
                return StatusCode(502, "Invalid project response.");
            }

            return View(project);
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, "Projects API is unavailable.");
        }
        catch (System.Text.Json.JsonException)
        {
            return StatusCode(502, "Invalid project data received.");
        }
        catch (TaskCanceledException)
            when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(504, "Projects API request timed out.");
        }
    }

}

